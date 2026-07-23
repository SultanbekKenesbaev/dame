using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DailyGate.Shared;

namespace DailyGate.Windows.Service;

public sealed class DeviceApiClient(HttpClient http, DeviceCredentialStore credentials)
{
    public async Task<DeviceSyncResponse> SyncAsync(CancellationToken cancellationToken = default)
        => await SendAsync<DeviceSyncResponse>(HttpMethod.Get, "/api/v1/device/sync", null, cancellationToken);

    public async Task<EmployeeLoginResponse> LoginAsync(EmployeeLoginRequest request, CancellationToken cancellationToken = default)
        => await SendAsync<EmployeeLoginResponse>(HttpMethod.Post, "/api/v1/device/employee/login", request, cancellationToken);

    public async Task ChangePasswordAsync(PasswordChangeRequest request, CancellationToken cancellationToken = default)
        => await SendAsync<JsonElement>(HttpMethod.Post, "/api/v1/device/employee/change-password", request, cancellationToken);

    public async Task<CompletionReceipt> SubmitAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
        => await SendAsync<CompletionReceipt>(HttpMethod.Post, "/api/v1/device/submissions", request, cancellationToken);

    public async Task<EmergencyUnlockResponse> EmergencyAsync(EmergencyUnlockRequest request, CancellationToken cancellationToken = default)
        => await SendAsync<EmergencyUnlockResponse>(HttpMethod.Post, "/api/v1/device/emergency/verify", request, cancellationToken);

    public async Task HeartbeatAsync(DeviceHeartbeatRequest request, CancellationToken cancellationToken = default)
        => await SendAsync<JsonElement>(HttpMethod.Post, "/api/v1/device/heartbeat", request, cancellationToken);

    public async Task EventAsync(object request, CancellationToken cancellationToken = default)
        => await SendAsync<JsonElement>(HttpMethod.Post, "/api/v1/device/events", request, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        var settings = DeviceCredentialStore.LoadSettings();
        var json = payload is null ? "" : JsonSerializer.Serialize(payload, JsonDefaults.Options);
        using var request = new HttpRequestMessage(method, new Uri(new Uri(settings.ApiBaseUrl), path));
        if (payload is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var canonical = $"{method.Method}\n{path}\n{unix}\n{nonce}\n{bodyHash}";
        using var key = credentials.LoadPrivateKey();
        var signature = key.SignData(Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256);
        request.Headers.Add("X-Device-Id", settings.DeviceId.ToString());
        request.Headers.Add("X-Device-Timestamp", unix.ToString());
        request.Headers.Add("X-Device-Nonce", nonce);
        request.Headers.Add("X-Device-Signature", Convert.ToBase64String(signature));

        using var response = await http.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"DailyGate API returned {(int)response.StatusCode}: {responseJson}", null, response.StatusCode);
        if (typeof(T) == typeof(JsonElement) && string.IsNullOrWhiteSpace(responseJson)) return (T)(object)default(JsonElement);
        return JsonSerializer.Deserialize<T>(responseJson, JsonDefaults.Options)
            ?? throw new InvalidOperationException("DailyGate API response was empty.");
    }
}
