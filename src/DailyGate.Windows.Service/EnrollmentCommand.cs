using System.Management;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DailyGate.Shared;
using Microsoft.Win32;

namespace DailyGate.Windows.Service;

public static class EnrollmentCommand
{
    public const string DefaultApiBaseUrl = "https://mydomen.uz";

    public static async Task<int> RunAsync(string[] args)
    {
        var apiUrl = Value(args, "--api-url")?.TrimEnd('/') ?? throw new ArgumentException("--api-url is required.");
        var code = Value(args, "--code") ?? throw new ArgumentException("--code is required.");
        var name = Value(args, "--name") ?? Environment.MachineName;
        var demoMode = args.Any(x => x.Equals("--demo-mode", StringComparison.OrdinalIgnoreCase));

        try
        {
            var enrolled = await EnrollAsync(apiUrl, code, name, demoMode);
            Console.WriteLine($"DailyGate device {enrolled.DeviceId} was enrolled for {enrolled.EmployeeLogin} ({(demoMode ? "desktop" : "kiosk")} mode).");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    public static async Task<DeviceEnrollmentResponse> EnrollAsync(
        string apiUrl,
        string code,
        string name,
        bool desktopMode,
        CancellationToken cancellationToken = default)
    {
        ServicePaths.Ensure();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.4.0";
        var request = new DeviceEnrollmentRequest(
            code.Trim(), name, HardwareFingerprint(), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), version);
        using var http = new HttpClient { BaseAddress = new Uri(apiUrl.TrimEnd('/')) };
        using var response = await http.PostAsJsonAsync("/api/v1/device/enroll", request, JsonDefaults.Options, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(ApiError(responseText, (int)response.StatusCode));
        }
        var enrolled = await response.Content.ReadFromJsonAsync<DeviceEnrollmentResponse>(JsonDefaults.Options, cancellationToken)
            ?? throw new InvalidOperationException("Enrollment response is empty.");

        var protector = new LocalProtector();
        new DeviceCredentialStore(protector).SavePrivateKey(key.ExportECPrivateKey());
        DeviceCredentialStore.SaveSettings(new ServiceSettings(
            apiUrl.TrimEnd('/'), enrolled.DeviceId, enrolled.EmployeeId, enrolled.EmployeeLogin,
            enrolled.ServerPublicKey, enrolled.TimeZoneId, enrolled.WorkdayStartHour, DemoMode: desktopMode));
        return enrolled;
    }

    private static string ApiError(string responseText, int statusCode)
    {
        try
        {
            using var json = JsonDocument.Parse(responseText);
            if (json.RootElement.TryGetProperty("message", out var message)
                && !string.IsNullOrWhiteSpace(message.GetString()))
            {
                var value = message.GetString()!;
                return value.Equals("Enrollment code is invalid or expired.", StringComparison.OrdinalIgnoreCase)
                    ? "Код подключения недействителен или его срок истёк. Создайте новый код в админке."
                    : value;
            }
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(responseText)
            ? $"Сервер отклонил подключение устройства ({statusCode})."
            : responseText;
    }

    private static string? Value(string[] args, string key)
    {
        var index = Array.FindIndex(args, x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string HardwareFingerprint()
    {
        var machineGuid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "unknown")?.ToString();
        var bios = "unknown";
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
            bios = searcher.Get().Cast<ManagementObject>().FirstOrDefault()?["SerialNumber"]?.ToString() ?? "unknown";
        }
        catch { }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{machineGuid}|{bios}|{Environment.MachineName}")));
    }
}
