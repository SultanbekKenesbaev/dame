using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DailyGate.Api.Data;

public sealed class DailyGateDesignTimeFactory : IDesignTimeDbContextFactory<DailyGateDbContext>
{
    public DailyGateDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=dailygate;Username=dailygate;Password=dailygate_dev";
        return new DailyGateDbContext(new DbContextOptionsBuilder<DailyGateDbContext>()
            .UseNpgsql(connection, provider =>
                provider.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .Options);
    }
}
