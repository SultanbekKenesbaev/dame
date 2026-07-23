using System.Net;
using System.Text.Json;
using DailyGate.Shared;

namespace DailyGate.Windows.Service;

public sealed class ClientCommandHandler(
    LocalRepository repository,
    DeviceCredentialStore credentials,
    DeviceApiClient api,
    SignatureVerifier signatures,
    OfflinePasswordVerifier offlinePasswords,
    ClientSession session,
    ILogger<ClientCommandHandler> logger)
{
    public async Task<PipeResponse> HandleAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return request.Operation switch
            {
                PipeOperations.Status => PipeResponse.Ok(request.Id, await StatusAsync()),
                PipeOperations.Login => PipeResponse.Ok(request.Id, await LoginAsync(Parse<ClientLoginCommand>(request), cancellationToken)),
                PipeOperations.ChangePassword => PipeResponse.Ok(request.Id, await ChangePasswordAsync(Parse<ClientPasswordChangeCommand>(request), cancellationToken)),
                PipeOperations.Submit => PipeResponse.Ok(request.Id, await SubmitAsync(Parse<ClientSubmitCommand>(request), cancellationToken)),
                PipeOperations.EmergencyUnlock => PipeResponse.Ok(request.Id, await EmergencyAsync(Parse<ClientEmergencyCommand>(request), cancellationToken)),
                PipeOperations.SyncNow => PipeResponse.Ok(request.Id, await SyncAsync(cancellationToken)),
                _ => PipeResponse.Fail(request.Id, "Unknown client operation.")
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Client pipe operation {Operation} failed.", request.Operation);
            return PipeResponse.Fail(request.Id, Friendly(exception));
        }
    }

    private async Task<ClientStatus> StatusAsync()
    {
        if (!credentials.IsEnrolled)
            return new ClientStatus(DateOnly.FromDateTime(DateTime.Today), false, false, false, false, null, null, null, null, null, 0, "Устройство ещё не зарегистрировано.", "not_enrolled");
        var settings = DeviceCredentialStore.LoadSettings();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var now = DateTimeOffset.UtcNow;
        var workday = WorkdayCalculator.GetWorkday(now, zone, settings.WorkdayStartHour);
        var next = WorkdayCalculator.StartOfWorkday(workday.AddDays(1), zone, settings.WorkdayStartHour);
        var seconds = Math.Max(0, (int)(next - now).TotalSeconds);
        var warning = seconds <= 60 ? "Через минуту Windows завершит текущий сеанс. Сохраните работу."
            : seconds <= 600 ? "В 04:00 начнётся новый рабочий день. Сохраните открытые документы." : null;
        var test = await repository.GetTestAsync(workday);
        if (test is not null && !signatures.Verify(test.PayloadJson, test.Signature))
        {
            test = null;
            warning = "Подпись локального теста повреждена. Требуется синхронизация или аварийный код.";
        }
        var lease = DateTimeOffset.TryParse(await repository.GetAsync("offline_lease"), out var parsedLease) ? parsedLease : (DateTimeOffset?)null;
        var employeeActive = !bool.TryParse(await repository.GetAsync("employee_active"), out var active) || active;
        if (!employeeActive) warning = "Учётная запись сотрудника деактивирована администратором.";
        return new ClientStatus(workday, employeeActive && await repository.HasCompletionAsync(workday), session.Authenticated,
            true, employeeActive, settings.EmployeeLogin, session.FullName, test, session.TestStartedAt, lease, seconds, warning,
            await repository.GetAsync("connection_state") ?? "unknown");
    }

    private async Task<ClientLoginResult> LoginAsync(ClientLoginCommand command, CancellationToken cancellationToken)
    {
        var settings = DeviceCredentialStore.LoadSettings();
        if (!settings.EmployeeLogin.Equals(command.Login.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Логин не соответствует сотруднику, закреплённому за ПК.");

        string fullName;
        bool mustChange;
        try
        {
            var response = await api.LoginAsync(new EmployeeLoginRequest(command.Login, command.Password), cancellationToken);
            fullName = response.FullName; mustChange = response.MustChangePassword;
            await repository.SetAsync("offline_verifier", response.OfflineVerifier);
            await repository.SetAsync("offline_lease", response.OfflineLeaseExpiresAt.ToString("O"));
            await repository.SetAsync("employee_name", response.FullName);
            await SyncAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            var verifier = await repository.GetAsync("offline_verifier");
            var lease = DateTimeOffset.TryParse(await repository.GetAsync("offline_lease"), out var parsed) ? parsed : DateTimeOffset.MinValue;
            if (verifier is null || lease <= DateTimeOffset.UtcNow || !offlinePasswords.Verify(command.Password, verifier))
                throw new UnauthorizedAccessException("Офлайн-вход недоступен: неверный пароль или срок офлайн-доступа истёк.");
            fullName = await repository.GetAsync("employee_name") ?? command.Login;
            mustChange = false;
            await repository.SetAsync("connection_state", "offline");
        }

        session.Authenticate(fullName);
        var status = await StatusAsync();
        if (status.Test is null) throw new InvalidOperationException("Тест на текущий рабочий день не загружен.");
        return new ClientLoginResult(fullName, mustChange, status.Test, session.TestStartedAt!.Value);
    }

    private async Task<ClientSubmitResult> SubmitAsync(ClientSubmitCommand command, CancellationToken cancellationToken)
    {
        if (!session.Authenticated) throw new UnauthorizedAccessException("Сначала войдите в систему.");
        var status = await StatusAsync();
        if (status.Test is null) throw new InvalidOperationException("Тест не найден.");
        var submission = new SubmissionRequest(Guid.NewGuid(), status.Test.InstanceId, Guid.NewGuid().ToString("N"),
            command.Status, command.StartedAt, DateTimeOffset.UtcNow, false, command.Answers);
        await repository.QueueSubmissionAsync(submission);
        await repository.MarkCompletionAsync(status.Workday, command.Status.ToString());

        var queuedOffline = true;
        try
        {
            var receipt = await api.SubmitAsync(submission, cancellationToken);
            if (!signatures.Verify(receipt.ReceiptJson, receipt.Signature)) throw new InvalidOperationException("Server receipt signature is invalid.");
            await repository.MarkSubmissionSyncedAsync(submission.SubmissionId);
            await repository.MarkCompletionAsync(status.Workday, receipt.Status.ToString(), receipt.ReceiptJson, receipt.Signature);
            await repository.SetAsync("connection_state", "online");
            queuedOffline = false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            var offlineCopy = submission with { WasOffline = true };
            await repository.QueueSubmissionAsync(offlineCopy);
            await repository.SetAsync("connection_state", "offline");
        }
        session.Clear();
        return new ClientSubmitResult(queuedOffline, status.Workday, command.Status.ToString());
    }

    private async Task<ClientLoginResult> ChangePasswordAsync(ClientPasswordChangeCommand command, CancellationToken cancellationToken)
    {
        var settings = DeviceCredentialStore.LoadSettings();
        await api.ChangePasswordAsync(new PasswordChangeRequest(command.CurrentPassword, command.NewPassword), cancellationToken);
        var response = await api.LoginAsync(new EmployeeLoginRequest(settings.EmployeeLogin, command.NewPassword), cancellationToken);
        await repository.SetAsync("offline_verifier", response.OfflineVerifier);
        await repository.SetAsync("offline_lease", response.OfflineLeaseExpiresAt.ToString("O"));
        await repository.SetAsync("employee_name", response.FullName);
        session.Authenticate(response.FullName);
        var status = await StatusAsync();
        if (status.Test is null) throw new InvalidOperationException("Тест на текущий рабочий день не загружен.");
        return new ClientLoginResult(response.FullName, false, status.Test, session.TestStartedAt!.Value);
    }

    private async Task<ClientSubmitResult> EmergencyAsync(ClientEmergencyCommand command, CancellationToken cancellationToken)
    {
        var status = await StatusAsync();
        var response = await api.EmergencyAsync(new EmergencyUnlockRequest(command.Code, status.Workday), cancellationToken);
        if (!response.Accepted || !signatures.Verify(response.ReceiptJson, response.Signature))
            throw new UnauthorizedAccessException("Аварийный код отклонён.");
        await repository.MarkCompletionAsync(status.Workday, SubmissionKind.EmergencyUnlocked.ToString(), response.ReceiptJson, response.Signature);
        session.Clear();
        return new ClientSubmitResult(false, status.Workday, SubmissionKind.EmergencyUnlocked.ToString());
    }

    public async Task<DeviceSyncResponse> SyncAsync(CancellationToken cancellationToken)
    {
        var response = await api.SyncAsync(cancellationToken);
        if (response.Tests.Any(test => !signatures.Verify(test.PayloadJson, test.Signature)))
            throw new InvalidOperationException("Server returned a test with an invalid signature.");
        var settings = DeviceCredentialStore.LoadSettings();
        if (settings.EmployeeId != response.EmployeeId)
        {
            await repository.ClearEmployeeDataAsync();
            session.Clear();
            DeviceCredentialStore.SaveSettings(settings with
            {
                EmployeeId = response.EmployeeId,
                EmployeeLogin = response.EmployeeLogin,
                ServerPublicKey = response.ServerPublicKey,
                TimeZoneId = response.TimeZoneId,
                WorkdayStartHour = response.WorkdayStartHour
            });
        }
        await repository.SaveTestsAsync(response.Tests);
        await repository.SetAsync("employee_active", response.EmployeeActive.ToString());
        await repository.SetAsync("offline_lease", response.OfflineLeaseExpiresAt.ToString("O"));
        await repository.SetAsync("last_server_time", response.ServerTime.ToString("O"));
        await repository.SetAsync("connection_state", "online");
        return response;
    }

    private static T Parse<T>(PipeRequest request) => JsonSerializer.Deserialize<T>(request.PayloadJson, JsonDefaults.Options)
        ?? throw new InvalidOperationException("Client request payload is empty.");

    private static string Friendly(Exception exception) => exception switch
    {
        UnauthorizedAccessException => exception.Message,
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => "Сервер отклонил авторизацию.",
        HttpRequestException => "Нет связи с сервером.",
        _ => exception.Message
    };
}
