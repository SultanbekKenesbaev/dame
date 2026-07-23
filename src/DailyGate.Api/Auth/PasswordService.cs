using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace DailyGate.Api.Auth;

public sealed class PasswordService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemoryKb = 65_536;
    private const int Iterations = 3;
    private const int Parallelism = 2;

    public string Hash(string password)
    {
        ValidateStrength(password);
        return HashCore(password);
    }

    public string HashAdmin(string password)
    {
        if (password.Length < 8)
            throw new ArgumentException("Administrator password must contain at least 8 characters.");
        return HashCore(password);
    }

    private static string HashCore(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, MemoryKb, Iterations, Parallelism);
        return $"$argon2id$v=19$m={MemoryKb},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split('$', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5 || parts[0] != "argon2id") return false;

            var parameters = parts[2].Split(',')
                .Select(x => x.Split('='))
                .ToDictionary(x => x[0], x => int.Parse(x[1]));
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Derive(password, salt, parameters["m"], parameters["t"], parameters["p"]);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
        catch (KeyNotFoundException) { return false; }
    }

    public string CreateOfflineVerifier(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, MemoryKb, Iterations, Parallelism);
        return $"v1:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static void ValidateStrength(string password)
    {
        if (password.Length < 12 || !password.Any(char.IsUpper) || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            throw new ArgumentException("Password must contain at least 12 characters, upper/lower case, a number and a symbol.");
        }
    }

    private static byte[] Derive(string password, byte[] salt, int memory, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        return argon.GetBytes(HashSize);
    }
}
