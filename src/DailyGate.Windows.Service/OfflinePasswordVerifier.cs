using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace DailyGate.Windows.Service;

public sealed class OfflinePasswordVerifier
{
    public bool Verify(string password, string verifier)
    {
        try
        {
            var parts = verifier.Split(':');
            if (parts.Length != 3 || parts[0] != "v1") return false;
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt, MemorySize = 65_536, Iterations = 3, DegreeOfParallelism = 2
            };
            return CryptographicOperations.FixedTimeEquals(argon.GetBytes(32), expected);
        }
        catch { return false; }
    }
}
