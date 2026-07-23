using DailyGate.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DailyGate.Api.Infrastructure;

public sealed class PostgresHealthCheck(DailyGateDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
            : HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
}
