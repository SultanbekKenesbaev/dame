using System.Security.Claims;

namespace DailyGate.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid DeviceId(this ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue("device_id")
            ?? throw new InvalidOperationException("Device claim is missing."));

    public static Guid AdminId(this ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Administrator claim is missing."));
}
