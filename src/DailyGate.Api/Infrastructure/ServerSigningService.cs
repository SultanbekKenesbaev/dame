using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DailyGate.Api.Infrastructure;

public sealed class ServerSigningService : IDisposable
{
    private readonly ECDsa _key;

    public ServerSigningService(IOptions<DailyGateOptions> options, IWebHostEnvironment environment)
    {
        var path = options.Value.SigningKeyPath;
        if (!Path.IsPathRooted(path)) path = Path.Combine(environment.ContentRootPath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (File.Exists(path))
        {
            _key.ImportFromPem(File.ReadAllText(path));
        }
        else
        {
            File.WriteAllText(path, _key.ExportECPrivateKeyPem());
        }
    }

    public string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    public string Sign(string payload)
        => Convert.ToBase64String(_key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256));

    public void Dispose() => _key.Dispose();
}
