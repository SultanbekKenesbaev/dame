using System.Security.Cryptography;
using System.Text;

namespace DailyGate.Windows.Service;

public sealed class SignatureVerifier
{
    public bool Verify(string payload, string signature)
    {
        try
        {
            var settings = DeviceCredentialStore.LoadSettings();
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(settings.ServerPublicKey), out _);
            return key.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromBase64String(signature), HashAlgorithmName.SHA256);
        }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
    }
}
