using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DailyGate.Shared;

namespace DailyGate.Windows.Service;

public sealed class DeviceCredentialStore(LocalProtector protector)
{
    public bool IsEnrolled => File.Exists(ServicePaths.Credentials) && File.Exists(ServicePaths.Settings);

    public void SavePrivateKey(byte[] privateKey)
    {
        ServicePaths.Ensure();
        File.WriteAllText(ServicePaths.Credentials, protector.Protect(Convert.ToBase64String(privateKey)), Encoding.UTF8);
    }

    public ECDsa LoadPrivateKey()
    {
        var encoded = protector.Unprotect(File.ReadAllText(ServicePaths.Credentials, Encoding.UTF8));
        var key = ECDsa.Create();
        key.ImportECPrivateKey(Convert.FromBase64String(encoded), out _);
        return key;
    }

    public static ServiceSettings LoadSettings()
        => JsonSerializer.Deserialize<ServiceSettings>(File.ReadAllText(ServicePaths.Settings), JsonDefaults.Options)
            ?? throw new InvalidOperationException("DailyGate service settings are invalid.");

    public static void SaveSettings(ServiceSettings settings)
    {
        ServicePaths.Ensure();
        File.WriteAllText(ServicePaths.Settings, JsonSerializer.Serialize(settings, JsonDefaults.Options), Encoding.UTF8);
    }
}
