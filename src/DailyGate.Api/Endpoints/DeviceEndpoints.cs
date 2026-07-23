using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DailyGate.Api.Auth;
using DailyGate.Api.Contracts;
using DailyGate.Api.Data;
using DailyGate.Api.Domain;
using DailyGate.Api.Infrastructure;
using DailyGate.Api.Services;
using DailyGate.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DailyGate.Api.Endpoints;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/device/enroll", EnrollAsync)
            .AllowAnonymous().WithTags("Device").RequireRateLimiting("auth");

        var admin = app.MapGroup("/api/v1/admin/devices").WithTags("Devices");
        admin.MapGet("/", async (DailyGateDbContext db) => Results.Ok(await db.Devices.AsNoTracking()
            .Include(x => x.Employee).OrderBy(x => x.Name).Select(x => new
            {
                x.Id, x.Name, x.HardwareFingerprint, x.ClientVersion, x.ServiceVersion,
                x.LastSeenAt, x.LastSyncAt, x.OfflineLeaseExpiresAt, x.ForceSync, x.Revoked,
                employee = new { x.Employee.Id, x.Employee.FullName, x.Employee.Login }
            }).ToListAsync())).RequireAuthorization("Viewer");
        admin.MapPost("/enrollment-codes", CreateEnrollmentCodeAsync).RequireAuthorization("AdminOnly");
        admin.MapPost("/{id:guid}/force-sync", async (Guid id, DailyGateDbContext db, AuditService audit) =>
        {
            var device = await db.Devices.FindAsync(id);
            if (device is null) return Results.NotFound();
            device.ForceSync = true;
            await db.SaveChangesAsync();
            await audit.WriteAsync("device.force_sync", nameof(Device), id);
            return Results.NoContent();
        }).RequireAuthorization("AdminOnly");
        admin.MapPost("/{id:guid}/reassign", async (Guid id, ReassignDeviceRequest request,
            DailyGateDbContext db, AuditService audit) =>
        {
            var device = await db.Devices.FindAsync(id);
            var employee = await db.Employees.FindAsync(request.EmployeeId);
            if (device is null || employee is null) return Results.NotFound();
            if (await db.Devices.AnyAsync(x => x.EmployeeId == request.EmployeeId && x.Id != id))
                return Results.Conflict(new { message = "The employee already has a device." });
            device.EmployeeId = request.EmployeeId;
            device.ForceSync = true;
            device.Revoked = false;
            device.OfflineLeaseExpiresAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await audit.WriteAsync("device.reassigned", nameof(Device), id, new { request.EmployeeId });
            return Results.NoContent();
        }).RequireAuthorization("AdminOnly");
        admin.MapDelete("/{id:guid}", async (Guid id, DailyGateDbContext db, AuditService audit) =>
        {
            var device = await db.Devices.FindAsync(id);
            if (device is null) return Results.NotFound();
            device.Revoked = true;
            await db.SaveChangesAsync();
            await audit.WriteAsync("device.revoked", nameof(Device), id);
            return Results.NoContent();
        }).RequireAuthorization("AdminOnly");

        var deviceRoutes = app.MapGroup("/api/v1/device").WithTags("Device")
            .RequireAuthorization(DeviceSignatureAuthenticationHandler.Scheme);
        deviceRoutes.MapPost("/heartbeat", HeartbeatAsync);
        deviceRoutes.MapPost("/events", RecordEventAsync);
        deviceRoutes.MapGet("/sync", SyncAsync);
        deviceRoutes.MapPost("/submissions", SubmitAsync);
        deviceRoutes.MapPost("/emergency/verify", VerifyEmergencyAsync);
        return app;
    }

    private static async Task<IResult> EnrollAsync(DeviceEnrollmentRequest request, DailyGateDbContext db,
        ServerSigningService signing, WorkdayService workday, IOptions<DailyGateOptions> options)
    {
        if (string.IsNullOrWhiteSpace(request.EnrollmentCode)
            || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200
            || string.IsNullOrWhiteSpace(request.HardwareFingerprint) || request.HardwareFingerprint.Length > 512
            || string.IsNullOrWhiteSpace(request.PublicKey) || request.PublicKey.Length > 4096)
            return Results.BadRequest(new { message = "Device enrollment payload is invalid." });
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(request.PublicKey), out _);
        }
        catch (Exception)
        {
            return Results.BadRequest(new { message = "Invalid device public key." });
        }

        var normalizedCode = NormalizeCode(request.EnrollmentCode);
        var hash = HashCode(normalizedCode);
        var enrollment = await db.DeviceEnrollmentCodes.Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.CodeHash == hash && x.UsedAt == null);
        if (enrollment is null || enrollment.ExpiresAt <= DateTimeOffset.UtcNow)
            return Results.BadRequest(new { message = "Enrollment code is invalid or expired." });
        if (enrollment.Employee.State != EmployeeState.Active)
            return Results.BadRequest(new { message = "Employee is not active." });
        if (await db.Devices.AnyAsync(x => x.EmployeeId == enrollment.EmployeeId && !x.Revoked))
            return Results.Conflict(new { message = "Employee already has an active device." });
        var fingerprintOwner = await db.Devices.SingleOrDefaultAsync(x => x.HardwareFingerprint == request.HardwareFingerprint);
        if (fingerprintOwner is { Revoked: false } || fingerprintOwner is not null && fingerprintOwner.EmployeeId != enrollment.EmployeeId)
            return Results.Conflict(new { message = "This workstation is already registered." });

        var now = DateTimeOffset.UtcNow;
        var device = await db.Devices.SingleOrDefaultAsync(x => x.EmployeeId == enrollment.EmployeeId);
        if (device is null)
        {
            device = new Device
            {
                Name = request.Name.Trim(),
                HardwareFingerprint = request.HardwareFingerprint.Trim(),
                PublicKey = request.PublicKey,
                ClientVersion = request.ClientVersion,
                EmployeeId = enrollment.EmployeeId,
                LastSeenAt = now,
                LastSyncAt = now,
                OfflineLeaseExpiresAt = now.AddDays(options.Value.OfflineLeaseDays)
            };
            db.Devices.Add(device);
        }
        else
        {
            device.Name = request.Name.Trim();
            device.HardwareFingerprint = request.HardwareFingerprint.Trim();
            device.PublicKey = request.PublicKey;
            device.ClientVersion = request.ClientVersion;
            device.ServiceVersion = "unknown";
            device.LastSeenAt = now;
            device.LastSyncAt = now;
            device.OfflineLeaseExpiresAt = now.AddDays(options.Value.OfflineLeaseDays);
            device.ForceSync = true;
            device.Revoked = false;
        }
        enrollment.UsedAt = now;
        db.AuditEvents.Add(new AuditEvent
        {
            AdminUserId = enrollment.CreatedByAdminId,
            Actor = "enrollment",
            Action = "device.enrolled",
            EntityType = nameof(Device),
            EntityId = device.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new { enrollment.EmployeeId, device.Name })
        });
        await db.SaveChangesAsync();

        return Results.Ok(new DeviceEnrollmentResponse(
            device.Id, enrollment.EmployeeId, enrollment.Employee.Login, signing.PublicKey,
            now, workday.TimeZoneId, workday.StartHour));
    }

    private static async Task<IResult> CreateEnrollmentCodeAsync(CreateEnrollmentCodeRequest request,
        HttpContext context, DailyGateDbContext db, AuditService audit)
    {
        var employee = await db.Employees.FindAsync(request.EmployeeId);
        if (employee is null) return Results.NotFound();
        if (employee.State != EmployeeState.Active) return Results.BadRequest(new { message = "Employee is not active." });

        var code = GenerateCode(10);
        var entity = new DeviceEnrollmentCode
        {
            CodeHash = HashCode(code),
            CodeLast4 = code[^4..],
            EmployeeId = request.EmployeeId,
            CreatedByAdminId = context.User.AdminId(),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        db.DeviceEnrollmentCodes.Add(entity);
        await db.SaveChangesAsync();
        await audit.WriteAsync("device.enrollment_code_created", nameof(DeviceEnrollmentCode), entity.Id,
            new { request.EmployeeId, entity.ExpiresAt, entity.CodeLast4 });
        return Results.Ok(new { code, entity.ExpiresAt });
    }

    private static async Task<IResult> HeartbeatAsync(DeviceHeartbeatRequest request, HttpContext context,
        DailyGateDbContext db)
    {
        var device = await db.Devices.FindAsync(context.User.DeviceId());
        if (device is null) return Results.NotFound();
        device.LastSeenAt = DateTimeOffset.UtcNow;
        device.ClientVersion = request.ClientVersion;
        device.ServiceVersion = request.ServiceVersion;
        if (!string.IsNullOrWhiteSpace(request.LastError))
        {
            db.DeviceEvents.Add(new DeviceEvent
            {
                DeviceId = device.Id,
                Type = DeviceEventType.SyncError,
                Details = request.LastError,
                DeviceTime = request.DeviceTime
            });
        }
        await db.SaveChangesAsync();
        return Results.Ok(new { serverTime = DateTimeOffset.UtcNow, device.ForceSync });
    }

    private static async Task<IResult> RecordEventAsync(DeviceEventRequest request, HttpContext context,
        DailyGateDbContext db)
    {
        if (request.Details?.Length > 4000)
            return Results.BadRequest(new { message = "Device event details must not exceed 4000 characters." });
        db.DeviceEvents.Add(new DeviceEvent
        {
            DeviceId = context.User.DeviceId(),
            Type = request.Type,
            Details = request.Details,
            DeviceTime = request.DeviceTime
        });
        await db.SaveChangesAsync();
        return Results.Accepted();
    }

    private static async Task<IResult> SyncAsync(HttpContext context, DailyGateDbContext db,
        DailyTestProvisioner provisioner, ServerSigningService signing,
        WorkdayService workday, IOptions<DailyGateOptions> options)
    {
        var device = await db.Devices.Include(x => x.Employee).SingleAsync(x => x.Id == context.User.DeviceId());
        var now = DateTimeOffset.UtcNow;
        device.LastSeenAt = now;
        device.LastSyncAt = now;
        device.OfflineLeaseExpiresAt = device.Employee.State == EmployeeState.Active
            ? now.AddDays(options.Value.OfflineLeaseDays)
            : now;
        var forceSync = device.ForceSync;
        device.ForceSync = false;
        await db.SaveChangesAsync();

        if (device.Employee.State == EmployeeState.Active)
            await provisioner.EnsureWindowAsync(device.EmployeeId, options.Value.OfflineLeaseDays);
        var current = workday.Current(now);
        var through = current.AddDays(options.Value.OfflineLeaseDays - 1);
        var tests = device.Employee.State == EmployeeState.Active
            ? await db.DailyTestInstances.AsNoTracking()
                .Where(x => x.EmployeeId == device.EmployeeId && x.Workday >= current && x.Workday <= through)
                .OrderBy(x => x.Workday)
                .Select(x => new SignedDailyTest(x.Id, x.Workday, x.PayloadJson, x.PayloadSignature, "ECDSA_P256_SHA256"))
                .ToListAsync()
            : [];
        return Results.Ok(new DeviceSyncResponse(
            device.EmployeeId, device.Employee.Login,
            now, workday.TimeZoneId, workday.StartHour, device.OfflineLeaseExpiresAt,
            forceSync, device.Employee.State == EmployeeState.Active, tests, signing.PublicKey));
    }

    private static async Task<IResult> SubmitAsync(SubmissionRequest request, HttpContext context,
        DailyGateDbContext db, ServerSigningService signing)
    {
        var device = await db.Devices.Include(x => x.Employee).SingleAsync(x => x.Id == context.User.DeviceId());
        var existing = await db.Submissions.AsNoTracking().Include(x => x.DailyTestInstance)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
            return Results.Ok(ToReceipt(existing));

        var instance = await db.DailyTestInstances.Include(x => x.Submission)
            .SingleOrDefaultAsync(x => x.Id == request.InstanceId);
        if (instance is null || instance.EmployeeId != device.EmployeeId)
            return Results.NotFound(new { message = "Daily test instance was not found." });
        if (instance.Submission is not null)
            return Results.Ok(ToReceipt(instance.Submission));
        if (request.Status is not (SubmissionKind.Completed or SubmissionKind.TimedOut))
            return Results.BadRequest(new { message = "Unsupported submission status." });

        var payload = JsonSerializer.Deserialize<DailyTestPayload>(instance.PayloadJson, JsonDefaults.Options)!;
        if (request.Answers is null)
            return Results.BadRequest(new { message = "Submission answers are required." });
        if (request.Answers.GroupBy(x => x.QuestionId).Any(group => group.Count() > 1))
            return Results.BadRequest(new { message = "A question cannot appear more than once." });
        var answers = request.Answers.ToDictionary(x => x.QuestionId);
        if (answers.Keys.Any(questionId => payload.Questions.All(x => x.Id != questionId)))
            return Results.BadRequest(new { message = "Answer contains a question outside the issued test." });
        foreach (var question in payload.Questions)
        {
            answers.TryGetValue(question.Id, out var answer);
            var selected = answer?.SelectedOptionIds ?? [];
            if (request.Status == SubmissionKind.Completed && question.Required && selected.Count == 0)
                return Results.BadRequest(new { message = "All required questions must be answered." });
            if (question.Kind == QuestionKind.SingleChoice && selected.Count > 1)
                return Results.BadRequest(new { message = "Single-choice question has multiple answers." });
            if (selected.Any(optionId => question.Options.All(x => x.Id != optionId)))
                return Results.BadRequest(new { message = "Answer contains an option outside the issued test." });
        }

        var now = DateTimeOffset.UtcNow;
        var status = request.Status == SubmissionKind.Completed ? TestInstanceState.Completed : TestInstanceState.TimedOut;
        var receiptPayload = JsonSerializer.Serialize(new
        {
            request.SubmissionId,
            instance.EmployeeId,
            instance.Workday,
            status = request.Status,
            acceptedAt = now
        }, JsonDefaults.Options);
        var submission = new Submission
        {
            Id = request.SubmissionId,
            DailyTestInstanceId = instance.Id,
            DailyTestInstance = instance,
            EmployeeId = instance.EmployeeId,
            DeviceId = device.Id,
            Device = device,
            DeviceName = device.Name,
            ClientVersion = device.ClientVersion,
            ServiceVersion = device.ServiceVersion,
            IdempotencyKey = request.IdempotencyKey,
            Status = status,
            StartedAt = request.StartedAt,
            SubmittedAt = request.SubmittedAt,
            AcceptedAt = now,
            WasOffline = request.WasOffline,
            ReceiptJson = receiptPayload,
            ReceiptSignature = signing.Sign(receiptPayload),
            Answers = payload.Questions.Select(question => new SubmissionAnswer
            {
                QuestionId = question.Id,
                SelectedOptionIdsJson = JsonSerializer.Serialize(
                    answers.GetValueOrDefault(question.Id)?.SelectedOptionIds ?? [], JsonDefaults.Options)
            }).ToList()
        };
        instance.State = status;
        instance.StartedAt = request.StartedAt;
        instance.CompletedAt = now;
        db.Submissions.Add(submission);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when
            (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two different retries can arrive together. The unique instance constraint selects one winner.
            db.ChangeTracker.Clear();
            var winner = await db.Submissions.AsNoTracking().Include(x => x.DailyTestInstance)
                .SingleOrDefaultAsync(x => x.DailyTestInstanceId == request.InstanceId);
            return winner is null
                ? Results.Conflict(new { message = "Submission identity already exists." })
                : Results.Ok(ToReceipt(winner));
        }
        return Results.Ok(ToReceipt(submission));
    }

    private static async Task<IResult> VerifyEmergencyAsync(EmergencyUnlockRequest request, HttpContext context,
        DailyGateDbContext db, ServerSigningService signing)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) return Results.Unauthorized();
        var deviceId = context.User.DeviceId();
        var hash = HashCode(NormalizeCode(request.Code));
        var now = DateTimeOffset.UtcNow;
        var accepted = await db.EmergencyUnlocks.Where(x => x.DeviceId == deviceId
                && x.Workday == request.Workday && x.CodeHash == hash && x.UsedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAt, now));
        if (accepted != 1)
            return Results.Unauthorized();

        var unlock = await db.EmergencyUnlocks.AsNoTracking().Include(x => x.Device)
            .SingleAsync(x => x.DeviceId == deviceId && x.Workday == request.Workday && x.CodeHash == hash);
        var instance = await db.DailyTestInstances.SingleOrDefaultAsync(x => x.EmployeeId == unlock.Device.EmployeeId && x.Workday == request.Workday);
        if (instance is not null && instance.State is TestInstanceState.Assigned or TestInstanceState.InProgress)
        {
            instance.State = TestInstanceState.EmergencyUnlocked;
            instance.CompletedAt = now;
        }
        db.DeviceEvents.Add(new DeviceEvent
        {
            DeviceId = deviceId,
            Type = DeviceEventType.EmergencyUnlock,
            Details = $"Emergency code ending {unlock.CodeLast4} was used.",
            DeviceTime = now
        });
        db.AuditEvents.Add(new AuditEvent
        {
            Actor = $"device:{deviceId}",
            Action = "emergency_code.used",
            EntityType = nameof(EmergencyUnlock),
            EntityId = unlock.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new { deviceId, request.Workday, unlock.CodeLast4 })
        });
        await db.SaveChangesAsync();

        var receipt = JsonSerializer.Serialize(new { deviceId, request.Workday, acceptedAt = now, status = "emergency_unlocked" });
        return Results.Ok(new EmergencyUnlockResponse(true, request.Workday, now, receipt, signing.Sign(receipt)));
    }

    private static CompletionReceipt ToReceipt(Submission submission)
        => new(submission.Id, submission.EmployeeId, submission.DailyTestInstance.Workday,
            submission.Status == TestInstanceState.Completed ? SubmissionKind.Completed : SubmissionKind.TimedOut,
            submission.AcceptedAt, submission.ReceiptJson, submission.ReceiptSignature);

    public static string GenerateCode(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(length, alphabet, static (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }

    public static string NormalizeCode(string code) => code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
    public static string HashCode(string code) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
