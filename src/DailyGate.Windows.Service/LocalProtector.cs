using System.Security.Cryptography;
using System.Text;

namespace DailyGate.Windows.Service;

public sealed class LocalProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DailyGate.LocalData.v1");

    public string Protect(string value)
        => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.LocalMachine));

    public string Unprotect(string value)
        => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.LocalMachine));
}
