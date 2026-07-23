using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DailyGate.Api.Domain;
using DailyGate.Api.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DailyGate.Api.Auth;

public sealed class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public string CreateAdminToken(AdminUser user)
        => CreateToken([
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Login),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        ], _options.Audience, TimeSpan.FromHours(8));

    public string CreateEmployeeToken(Employee employee, Device device)
        => CreateToken([
            new Claim(JwtRegisteredClaimNames.Sub, employee.Id.ToString()),
            new Claim(ClaimTypes.Name, employee.Login),
            new Claim("device_id", device.Id.ToString()),
            new Claim("token_type", "employee")
        ], "DailyGate.Employee", TimeSpan.FromMinutes(20));

    private string CreateToken(IEnumerable<Claim> claims, string audience, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(lifetime),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
