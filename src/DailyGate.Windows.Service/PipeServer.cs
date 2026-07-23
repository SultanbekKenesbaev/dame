using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using DailyGate.Shared;

namespace DailyGate.Windows.Service;

public sealed class PipeServer(ClientCommandHandler handler, ILogger<PipeServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                AuthorizeClient(pipe);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(stoppingToken);
                if (line is null) continue;
                var request = JsonSerializer.Deserialize<PipeRequest>(line, JsonDefaults.Options);
                var response = request is null
                    ? PipeResponse.Fail("unknown", "Malformed client request.")
                    : await handler.HandleAsync(request, stoppingToken);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonDefaults.Options));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "DailyGate named pipe failed.");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(ServicePaths.PipeName, PipeDirection.InOut, 4,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 8192, 8192, security);
    }

    private static void AuthorizeClient(NamedPipeServerStream pipe)
    {
        string? sid = null;
        var isAdministrator = false;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(true);
            if (identity is null) return;
            sid = identity.User?.Value;
            isAdministrator = new WindowsPrincipal(identity)
                .IsInRole(WindowsBuiltInRole.Administrator);
        });
        if (string.IsNullOrWhiteSpace(sid) || isAdministrator)
            throw new UnauthorizedAccessException("Only the managed standard kiosk profile may use the DailyGate pipe.");

        var settings = DeviceCredentialStore.LoadSettings();
        var allowedSid = settings.AllowedClientSid;
        if (string.IsNullOrWhiteSpace(allowedSid))
        {
            DeviceCredentialStore.SaveSettings(settings with { AllowedClientSid = sid });
            return;
        }
        if (!allowedSid.Equals(sid, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The DailyGate pipe is bound to another Windows profile.");
    }
}
