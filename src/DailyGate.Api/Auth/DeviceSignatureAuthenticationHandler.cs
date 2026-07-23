using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using DailyGate.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DailyGate.Api.Auth;

public sealed class DeviceSignatureAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DailyGateDbContext db,
    IMemoryCache cache)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "DeviceSignature";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Device-Id", out var deviceIdHeader)
            || !Guid.TryParse(deviceIdHeader, out var deviceId)
            || !Request.Headers.TryGetValue("X-Device-Timestamp", out var timestampHeader)
            || !long.TryParse(timestampHeader, out var unixSeconds)
            || !Request.Headers.TryGetValue("X-Device-Nonce", out var nonceHeader)
            || !Request.Headers.TryGetValue("X-Device-Signature", out var signatureHeader))
        {
            return AuthenticateResult.NoResult();
        }

        DateTimeOffset requestTime;
        try { requestTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
        catch (ArgumentOutOfRangeException) { return AuthenticateResult.Fail("Device timestamp is invalid."); }
        if (Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalMinutes) > 5)
            return AuthenticateResult.Fail("Device timestamp is outside the allowed window.");

        var nonce = nonceHeader.ToString();
        if (nonce.Length is < 16 or > 128 || signatureHeader.ToString().Length > 4096)
            return AuthenticateResult.Fail("Device signature headers are malformed.");
        var nonceKey = $"device-nonce:{deviceId}:{nonce}";
        if (cache.TryGetValue(nonceKey, out _))
            return AuthenticateResult.Fail("Device nonce was already used.");

        var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deviceId);
        if (device is null || device.Revoked)
            return AuthenticateResult.Fail("Device is unknown or revoked.");

        Request.EnableBuffering();
        using var memory = new MemoryStream();
        await Request.Body.CopyToAsync(memory);
        Request.Body.Position = 0;
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(memory.ToArray()));
        var canonical = $"{Request.Method}\n{Request.Path}{Request.QueryString}\n{unixSeconds}\n{nonce}\n{bodyHash}";

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(device.PublicKey), out _);
            var valid = ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(canonical),
                Convert.FromBase64String(signatureHeader.ToString()),
                HashAlgorithmName.SHA256);
            if (!valid) return AuthenticateResult.Fail("Invalid device signature.");
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return AuthenticateResult.Fail("Malformed device signature.");
        }

        cache.Set(nonceKey, true, TimeSpan.FromMinutes(10));
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, device.Id.ToString()),
            new Claim("device_id", device.Id.ToString()),
            new Claim("employee_id", device.EmployeeId.ToString()),
            new Claim("auth_type", "device")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
    }
}
