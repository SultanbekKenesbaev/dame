using System.Text.Json;

namespace DailyGate.Shared;

public sealed record PipeRequest(string Id, string Operation, string PayloadJson)
{
    public static PipeRequest Create<T>(string operation, T payload)
        => new(Guid.NewGuid().ToString("N"), operation, JsonSerializer.Serialize(payload, JsonDefaults.Options));
}

public sealed record PipeResponse(string Id, bool Success, string PayloadJson, string? Error)
{
    public T Payload<T>() => JsonSerializer.Deserialize<T>(PayloadJson, JsonDefaults.Options)
        ?? throw new InvalidOperationException("The pipe response payload is empty.");

    public static PipeResponse Ok<T>(string id, T payload)
        => new(id, true, JsonSerializer.Serialize(payload, JsonDefaults.Options), null);

    public static PipeResponse Fail(string id, string error)
        => new(id, false, "{}", error);
}

public sealed record ClientStatus(
    DateOnly Workday,
    bool Unlocked,
    bool Authenticated,
    bool Enrolled,
    bool EmployeeActive,
    string? EmployeeLogin,
    string? EmployeeName,
    SignedDailyTest? Test,
    DateTimeOffset? TestStartedAt,
    DateTimeOffset? OfflineLeaseExpiresAt,
    int SecondsUntilNextWorkday,
    string? Warning,
    string ConnectionState);

public sealed record ClientLoginCommand(string Login, string Password);
public sealed record ClientLoginResult(string FullName, bool MustChangePassword, SignedDailyTest Test, DateTimeOffset StartedAt);
public sealed record ClientPasswordChangeCommand(string CurrentPassword, string NewPassword);
public sealed record ClientSubmitCommand(
    SubmissionKind Status,
    DateTimeOffset StartedAt,
    IReadOnlyList<SubmissionAnswerRequest> Answers);
public sealed record ClientSubmitResult(bool QueuedOffline, DateOnly Workday, string Status);
public sealed record ClientEmergencyCommand(string Code);

public static class PipeOperations
{
    public const string Status = "status";
    public const string Login = "login";
    public const string ChangePassword = "change_password";
    public const string Submit = "submit";
    public const string EmergencyUnlock = "emergency_unlock";
    public const string SyncNow = "sync_now";
}
