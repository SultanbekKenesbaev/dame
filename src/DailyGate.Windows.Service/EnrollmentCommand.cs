using System.Management;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using DailyGate.Shared;
using Microsoft.Win32;

namespace DailyGate.Windows.Service;

public static class EnrollmentCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var apiUrl = Value(args, "--api-url")?.TrimEnd('/') ?? throw new ArgumentException("--api-url is required.");
        var code = Value(args, "--code") ?? throw new ArgumentException("--code is required.");
        var name = Value(args, "--name") ?? Environment.MachineName;
        var demoMode = args.Any(x => x.Equals("--demo-mode", StringComparison.OrdinalIgnoreCase));

        ServicePaths.Ensure();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new DeviceEnrollmentRequest(
            code, name, HardwareFingerprint(), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), "0.1.0");
        using var http = new HttpClient { BaseAddress = new Uri(apiUrl) };
        var response = await http.PostAsJsonAsync("/api/v1/device/enroll", request, JsonDefaults.Options);
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(await response.Content.ReadAsStringAsync());
            return 2;
        }
        var enrolled = await response.Content.ReadFromJsonAsync<DeviceEnrollmentResponse>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Enrollment response is empty.");

        var protector = new LocalProtector();
        new DeviceCredentialStore(protector).SavePrivateKey(key.ExportECPrivateKey());
        DeviceCredentialStore.SaveSettings(new ServiceSettings(
            apiUrl, enrolled.DeviceId, enrolled.EmployeeId, enrolled.EmployeeLogin,
            enrolled.ServerPublicKey, enrolled.TimeZoneId, enrolled.WorkdayStartHour, DemoMode: demoMode));
        Console.WriteLine($"DailyGate device {enrolled.DeviceId} was enrolled for {enrolled.EmployeeLogin} ({(demoMode ? "demo" : "kiosk")} mode).");
        return 0;
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
