using System.Reflection;
using DailyGate.Shared;

namespace DailyGate.Windows.Service;

public sealed class DailyGateWorker(
    LocalRepository repository,
    DeviceCredentialStore credentials,
    DeviceApiClient api,
    SignatureVerifier signatures,
    ClientCommandHandler commands,
    WindowsSessionController sessions,
    ILogger<DailyGateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await repository.InitializeAsync();
        if (!credentials.IsEnrolled)
        {
            logger.LogWarning("DailyGate service is not enrolled. Run DailyGate.Service.exe enroll first.");
            return;
        }

        await TrySyncAsync(stoppingToken);
        var settings = DeviceCredentialStore.LoadSettings();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var workday = WorkdayCalculator.GetWorkday(DateTimeOffset.UtcNow, zone, settings.WorkdayStartHour);
        var storedWorkday = await repository.GetAsync("last_workday");
        if (!settings.DemoMode && storedWorkday is not null && storedWorkday != workday.ToString("yyyy-MM-dd"))
            sessions.ForceLogoffActiveSession();
        await repository.SetAsync("last_workday", workday.ToString("yyyy-MM-dd"));

        var nextNetworkCycle = DateTimeOffset.UtcNow;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var current = WorkdayCalculator.GetWorkday(DateTimeOffset.UtcNow, zone, settings.WorkdayStartHour);
            if (current != workday)
            {
                workday = current;
                await repository.SetAsync("last_workday", current.ToString("yyyy-MM-dd"));
                if (!settings.DemoMode) sessions.ForceLogoffActiveSession();
            }

            if (DateTimeOffset.UtcNow < nextNetworkCycle) continue;
            await TrySyncAsync(stoppingToken);
            nextNetworkCycle = DateTimeOffset.UtcNow.AddMinutes(5).AddSeconds(Random.Shared.Next(0, 25));
        }
    }

    private async Task TrySyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            var lastServerTimeText = await repository.GetAsync("last_server_time");
            if (DateTimeOffset.TryParse(lastServerTimeText, out var lastServerTime)
                && DateTimeOffset.UtcNow < lastServerTime.AddMinutes(-5))
            {
                await api.EventAsync(new { type = "ClockTamper", details = "Local clock moved backwards.", deviceTime = DateTimeOffset.UtcNow }, cancellationToken);
            }

            foreach (var pending in await repository.PendingAsync())
            {
                var receipt = await api.SubmitAsync(pending, cancellationToken);
                if (!signatures.Verify(receipt.ReceiptJson, receipt.Signature))
                    throw new InvalidOperationException("Submission receipt signature is invalid.");
                await repository.MarkSubmissionSyncedAsync(pending.SubmissionId);
                await repository.MarkCompletionAsync(receipt.Workday, receipt.Status.ToString(), receipt.ReceiptJson, receipt.Signature);
            }

            await commands.SyncAsync(cancellationToken);

            await api.HeartbeatAsync(new DeviceHeartbeatRequest(
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0",
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0",
                "running", DateTimeOffset.UtcNow, null), cancellationToken);
            await repository.SetAsync("connection_state", "online");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("DailyGate is offline: {Message}", exception.Message);
            await repository.SetAsync("connection_state", "offline");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "DailyGate synchronization failed.");
            await repository.SetAsync("connection_state", "error");
        }
    }
}
