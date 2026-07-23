using System.Text.Json;
using DailyGate.Shared;
using Microsoft.Data.Sqlite;

namespace DailyGate.Windows.Service;

public sealed class LocalRepository(LocalProtector protector)
{
    private string ConnectionString => $"Data Source={ServicePaths.Database};Mode=ReadWriteCreate;Cache=Shared";

    public async Task InitializeAsync()
    {
        ServicePaths.Ensure();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Settings(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Tests(Workday TEXT PRIMARY KEY, InstanceId TEXT NOT NULL, EncryptedPayload TEXT NOT NULL, Signature TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS PendingSubmissions(Id TEXT PRIMARY KEY, EncryptedPayload TEXT NOT NULL, Synced INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Completions(Workday TEXT PRIMARY KEY, Status TEXT NOT NULL, ReceiptJson TEXT, Signature TEXT, CreatedAt TEXT NOT NULL);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task SetAsync(string key, string value)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Settings(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", protector.Protect(value));
        await command.ExecuteNonQueryAsync();
    }

    public async Task ClearEmployeeDataAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM Tests; DELETE FROM PendingSubmissions; DELETE FROM Completions; DELETE FROM Settings;";
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task<string?> GetAsync(string key)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key=$key";
        command.Parameters.AddWithValue("$key", key);
        var value = await command.ExecuteScalarAsync();
        return value is string text ? protector.Unprotect(text) : null;
    }

    public async Task SaveTestsAsync(IEnumerable<SignedDailyTest> tests)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var test in tests)
        {
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "INSERT INTO Tests(Workday,InstanceId,EncryptedPayload,Signature) VALUES($day,$id,$payload,$signature) ON CONFLICT(Workday) DO UPDATE SET InstanceId=excluded.InstanceId,EncryptedPayload=excluded.EncryptedPayload,Signature=excluded.Signature";
            command.Parameters.AddWithValue("$day", test.Workday.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$id", test.InstanceId.ToString());
            command.Parameters.AddWithValue("$payload", protector.Protect(test.PayloadJson));
            command.Parameters.AddWithValue("$signature", test.Signature);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    public async Task<SignedDailyTest?> GetTestAsync(DateOnly workday)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT InstanceId,EncryptedPayload,Signature FROM Tests WHERE Workday=$day";
        command.Parameters.AddWithValue("$day", workday.ToString("yyyy-MM-dd"));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new SignedDailyTest(Guid.Parse(reader.GetString(0)), workday, protector.Unprotect(reader.GetString(1)), reader.GetString(2));
    }

    public async Task<bool> HasCompletionAsync(DateOnly workday)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM Completions WHERE Workday=$day";
        command.Parameters.AddWithValue("$day", workday.ToString("yyyy-MM-dd"));
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    public async Task MarkCompletionAsync(DateOnly workday, string status, string? receiptJson = null, string? signature = null)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Completions(Workday,Status,ReceiptJson,Signature,CreatedAt) VALUES($day,$status,$receipt,$signature,$created) ON CONFLICT(Workday) DO UPDATE SET Status=excluded.Status,ReceiptJson=excluded.ReceiptJson,Signature=excluded.Signature";
        command.Parameters.AddWithValue("$day", workday.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$receipt", (object?)receiptJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$signature", (object?)signature ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task QueueSubmissionAsync(SubmissionRequest request)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO PendingSubmissions(Id,EncryptedPayload,Synced,CreatedAt) VALUES($id,$payload,0,$created) ON CONFLICT(Id) DO UPDATE SET EncryptedPayload=excluded.EncryptedPayload,Synced=0";
        command.Parameters.AddWithValue("$id", request.SubmissionId.ToString());
        command.Parameters.AddWithValue("$payload", protector.Protect(JsonSerializer.Serialize(request, JsonDefaults.Options)));
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<SubmissionRequest>> PendingAsync()
    {
        var result = new List<SubmissionRequest>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT EncryptedPayload FROM PendingSubmissions WHERE Synced=0 ORDER BY CreatedAt";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var request = JsonSerializer.Deserialize<SubmissionRequest>(protector.Unprotect(reader.GetString(0)), JsonDefaults.Options);
            if (request is not null) result.Add(request);
        }
        return result;
    }

    public async Task MarkSubmissionSyncedAsync(Guid id)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE PendingSubmissions SET Synced=1 WHERE Id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync();
    }
}
