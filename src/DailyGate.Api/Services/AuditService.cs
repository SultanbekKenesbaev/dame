using System.Security.Claims;
using System.Text.Json;
using DailyGate.Api.Data;
using DailyGate.Api.Domain;

namespace DailyGate.Api.Services;

public sealed class AuditService(DailyGateDbContext db, IHttpContextAccessor accessor)
{
    public async Task WriteAsync(string action, string entityType, object? entityId = null, object? payload = null)
    {
        var user = accessor.HttpContext?.User;
        var adminIdClaim = user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user?.FindFirstValue("sub");
        var adminId = Guid.TryParse(adminIdClaim, out var parsed) ? parsed : (Guid?)null;
        db.AuditEvents.Add(new AuditEvent
        {
            AdminUserId = adminId,
            Actor = user?.Identity?.Name ?? "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            PayloadJson = payload is null ? "{}" : JsonSerializer.Serialize(payload)
        });
        await db.SaveChangesAsync();
    }
}
