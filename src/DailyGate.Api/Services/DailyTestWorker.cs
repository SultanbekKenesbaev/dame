using DailyGate.Api.Data;
using DailyGate.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DailyGate.Api.Services;

public sealed class DailyTestWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DailyTestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Daily test worker cycle failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DailyGateDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<WorkdayService>();
        var provisioner = scope.ServiceProvider.GetRequiredService<DailyTestProvisioner>();
        var current = clock.Current();

        var overdue = await db.DailyTestInstances
            .Where(x => x.Workday < current
                && (x.State == TestInstanceState.Assigned || x.State == TestInstanceState.InProgress))
            .ToListAsync(cancellationToken);
        foreach (var instance in overdue) instance.State = TestInstanceState.Missed;
        if (overdue.Count > 0) await db.SaveChangesAsync(cancellationToken);

        var employeeIds = await db.Employees.AsNoTracking()
            .Where(x => x.State == EmployeeState.Active)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var employeeId in employeeIds)
            await provisioner.EnsureWindowAsync(employeeId, 7, cancellationToken);
    }
}
