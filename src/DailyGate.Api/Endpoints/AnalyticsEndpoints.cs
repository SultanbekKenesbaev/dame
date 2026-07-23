using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using DailyGate.Api.Auth;
using DailyGate.Api.Contracts;
using DailyGate.Api.Data;
using DailyGate.Api.Domain;
using DailyGate.Api.Services;
using DailyGate.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DailyGate.Api.Infrastructure;

namespace DailyGate.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var analytics = app.MapGroup("/api/v1/admin/analytics").WithTags("Analytics")
            .RequireAuthorization("Viewer");
        analytics.MapGet("/dashboard", DashboardAsync);
        analytics.MapGet("/results", ResultsAsync);
        analytics.MapGet("/employees/{id:guid}", EmployeeDetailsAsync);
        analytics.MapGet("/export.csv", ExportCsvAsync);
        analytics.MapGet("/export.xlsx", ExportXlsxAsync);

        app.MapGet("/api/v1/admin/audit", async (DailyGateDbContext db, int? take) =>
            Results.Ok(await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.CreatedAt)
                .Take(Math.Clamp(take ?? 100, 1, 500)).ToListAsync()))
            .WithTags("Audit").RequireAuthorization("Viewer");

        var emergency = app.MapGroup("/api/v1/admin/emergency-unlocks").WithTags("Emergency")
            .RequireAuthorization("AdminOnly");
        emergency.MapPost("/", CreateEmergencyCodeAsync);
        emergency.MapGet("/", async (DailyGateDbContext db) => Results.Ok(await db.EmergencyUnlocks.AsNoTracking()
            .Include(x => x.Device).ThenInclude(x => x.Employee)
            .OrderByDescending(x => x.CreatedAt).Take(200).Select(x => new
            {
                x.Id, x.Workday, x.CodeLast4, x.CreatedAt, x.ExpiresAt, x.UsedAt,
                device = new { x.Device.Id, x.Device.Name },
                employee = new { x.Device.Employee.Id, x.Device.Employee.FullName }
            }).ToListAsync()));
        return app;
    }

    private static async Task<IResult> DashboardAsync(DailyGateDbContext db, WorkdayService clock,
        IOptions<DailyGateOptions> options)
    {
        var current = clock.Current();
        var counts = await db.DailyTestInstances.AsNoTracking().Where(x => x.Workday == current)
            .GroupBy(x => x.State).Select(x => new { status = x.Key.ToString(), count = x.Count() }).ToListAsync();
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-options.Value.HeartbeatOfflineMinutes);
        var offline = await db.Devices.AsNoTracking().CountAsync(x => !x.Revoked && (x.LastSeenAt == null || x.LastSeenAt < cutoff));
        var totalDevices = await db.Devices.AsNoTracking().CountAsync(x => !x.Revoked);
        var totalEmployees = await db.Employees.AsNoTracking().CountAsync(x => x.State == EmployeeState.Active);
        var events = await db.DeviceEvents.AsNoTracking().Include(x => x.Device).ThenInclude(x => x.Employee)
            .Where(x => x.ReceivedAt >= DateTimeOffset.UtcNow.AddDays(-1) && x.Type != DeviceEventType.Heartbeat)
            .OrderByDescending(x => x.ReceivedAt).Take(20).Select(x => new
            {
                x.Id, type = x.Type.ToString(), x.Details, x.ReceivedAt,
                device = x.Device.Name, employee = x.Device.Employee.FullName
            }).ToListAsync();
        return Results.Ok(new { workday = current, totalEmployees, totalDevices, offlineDevices = offline, counts, events });
    }

    private static async Task<IResult> EmployeeDetailsAsync(Guid id, DailyGateDbContext db)
    {
        var employee = await db.Employees.AsNoTracking().Include(x => x.Group).Include(x => x.Device)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (employee is null) return Results.NotFound();
        var history = await db.DailyTestInstances.AsNoTracking().Where(x => x.EmployeeId == id)
            .Include(x => x.Submission).ThenInclude(x => x!.Answers)
            .Include(x => x.Submission).ThenInclude(x => x!.Device)
            .OrderByDescending(x => x.Workday).Take(366).ToListAsync();
        return Results.Ok(new
        {
            employee = new
            {
                employee.Id, employee.FullName, employee.Login, employee.Position, state = employee.State.ToString(),
                group = employee.Group?.Name, device = employee.Device?.Name, employee.Device?.LastSeenAt
            },
            history = history.Select(HistoryItem)
        });
    }

    private static async Task<IResult> ResultsAsync(DailyGateDbContext db, DateOnly? from, DateOnly? to,
        Guid? employeeId, Guid? groupId, TestInstanceState? status, int? take)
    {
        var query = db.DailyTestInstances.AsNoTracking()
            .Include(x => x.Employee).ThenInclude(x => x.Group)
            .Include(x => x.Submission).ThenInclude(x => x!.Device)
            .AsQueryable();
        if (from is not null) query = query.Where(x => x.Workday >= from);
        if (to is not null) query = query.Where(x => x.Workday <= to);
        if (employeeId is not null) query = query.Where(x => x.EmployeeId == employeeId);
        if (groupId is not null) query = query.Where(x => x.Employee.GroupId == groupId);
        if (status is not null) query = query.Where(x => x.State == status);
        var rows = await query.OrderByDescending(x => x.Workday).ThenBy(x => x.Employee.FullName)
            .Take(Math.Clamp(take ?? 500, 1, 2000)).ToListAsync();
        return Results.Ok(rows.Select(x => new
        {
            x.Id,
            x.Workday,
            employee = new { x.Employee.Id, x.Employee.FullName, x.Employee.Login, x.Employee.Position },
            group = x.Employee.Group?.Name,
            status = x.State.ToString(),
            x.StartedAt,
            x.CompletedAt,
            durationSeconds = x.Submission == null ? null : (double?)(x.Submission.SubmittedAt - x.Submission.StartedAt).TotalSeconds,
            wasOffline = x.Submission?.WasOffline,
            device = x.Submission?.DeviceName,
            clientVersion = x.Submission?.ClientVersion,
            serviceVersion = x.Submission?.ServiceVersion
        }));
    }

    private static object HistoryItem(DailyTestInstance instance)
    {
        var payload = JsonSerializer.Deserialize<DailyTestPayload>(instance.PayloadJson, JsonDefaults.Options);
        var submitted = instance.Submission?.Answers.ToDictionary(x => x.QuestionId) ?? [];
        return new
        {
            instance.Id,
            instance.Workday,
            status = instance.State.ToString(),
            instance.StartedAt,
            instance.CompletedAt,
            durationSeconds = instance.Submission == null ? null : (double?)(instance.Submission.SubmittedAt - instance.Submission.StartedAt).TotalSeconds,
            wasOffline = instance.Submission?.WasOffline,
            device = instance.Submission?.DeviceName,
            clientVersion = instance.Submission?.ClientVersion,
            serviceVersion = instance.Submission?.ServiceVersion,
            answers = payload?.Questions.Select(question =>
            {
                var selectedIds = submitted.TryGetValue(question.Id, out var answer)
                    ? JsonSerializer.Deserialize<Guid[]>(answer.SelectedOptionIdsJson) ?? []
                    : [];
                return new
                {
                    questionId = question.Id,
                    question = question.Text,
                    selectedOptions = question.Options.Where(option => selectedIds.Contains(option.Id)).Select(option => option.Text),
                    skipped = selectedIds.Length == 0
                };
            })
        };
    }

    private static async Task<IResult> CreateEmergencyCodeAsync(CreateEmergencyCodeRequest request,
        HttpContext context, DailyGateDbContext db, WorkdayService clock, AuditService audit)
    {
        var device = await db.Devices.Include(x => x.Employee).SingleOrDefaultAsync(x => x.Id == request.DeviceId && !x.Revoked);
        if (device is null) return Results.NotFound();
        var code = DeviceEndpoints.GenerateCode(8);
        var workday = request.Workday ?? clock.Current();
        var unlock = new EmergencyUnlock
        {
            DeviceId = device.Id,
            CreatedByAdminId = context.User.AdminId(),
            CodeHash = DeviceEndpoints.HashCode(code),
            CodeLast4 = code[^4..],
            Workday = workday,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        db.EmergencyUnlocks.Add(unlock);
        await db.SaveChangesAsync();
        await audit.WriteAsync("emergency_code.created", nameof(EmergencyUnlock), unlock.Id,
            new { device.Id, device.EmployeeId, workday, unlock.ExpiresAt, unlock.CodeLast4 });
        return Results.Ok(new { code, unlock.ExpiresAt, workday, employee = device.Employee.FullName, device = device.Name });
    }

    private static async Task<IResult> ExportCsvAsync(DailyGateDbContext db, DateOnly? from, DateOnly? to,
        Guid? employeeId, Guid? groupId, TestInstanceState? status)
    {
        var rows = await ExportQuery(db, from, to, employeeId, groupId, status).ToListAsync();
        var csv = new StringBuilder("Workday,Employee,Login,Status,StartedAt,CompletedAt,WasOffline,Device,ClientVersion,ServiceVersion\r\n");
        foreach (var row in rows)
        {
            csv.Append(Escape(row.Workday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(row.Employee.FullName)).Append(',').Append(Escape(row.Employee.Login)).Append(',')
                .Append(Escape(row.State.ToString())).Append(',').Append(Escape(row.StartedAt?.ToString("O"))).Append(',')
                .Append(Escape(row.CompletedAt?.ToString("O"))).Append(',').Append(row.Submission?.WasOffline ?? false).Append(',')
                .Append(Escape(row.Submission?.DeviceName)).Append(',').Append(Escape(row.Submission?.ClientVersion)).Append(',')
                .Append(Escape(row.Submission?.ServiceVersion)).Append("\r\n");
        }
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", "dailygate-results.csv");
    }

    private static async Task<IResult> ExportXlsxAsync(DailyGateDbContext db, DateOnly? from, DateOnly? to,
        Guid? employeeId, Guid? groupId, TestInstanceState? status)
    {
        var rows = await ExportQuery(db, from, to, employeeId, groupId, status).ToListAsync();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Results");
        var headers = new[] { "Workday", "Employee", "Login", "Status", "Started at", "Completed at", "Offline", "Device", "Client version", "Service version" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var number = index + 2;
            sheet.Cell(number, 1).Value = row.Workday.ToString("yyyy-MM-dd");
            sheet.Cell(number, 2).Value = row.Employee.FullName;
            sheet.Cell(number, 3).Value = row.Employee.Login;
            sheet.Cell(number, 4).Value = row.State.ToString();
            sheet.Cell(number, 5).Value = row.StartedAt?.UtcDateTime;
            sheet.Cell(number, 6).Value = row.CompletedAt?.UtcDateTime;
            sheet.Cell(number, 7).Value = row.Submission?.WasOffline ?? false;
            sheet.Cell(number, 8).Value = row.Submission?.DeviceName;
            sheet.Cell(number, 9).Value = row.Submission?.ClientVersion;
            sheet.Cell(number, 10).Value = row.Submission?.ServiceVersion;
        }
        sheet.Columns().AdjustToContents();
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Results.File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "dailygate-results.xlsx");
    }

    private static IQueryable<DailyTestInstance> ExportQuery(DailyGateDbContext db, DateOnly? from, DateOnly? to,
        Guid? employeeId, Guid? groupId, TestInstanceState? status)
    {
        var query = db.DailyTestInstances.AsNoTracking().Include(x => x.Employee)
            .Include(x => x.Submission).ThenInclude(x => x!.Device).AsQueryable();
        if (from is not null) query = query.Where(x => x.Workday >= from);
        if (to is not null) query = query.Where(x => x.Workday <= to);
        if (employeeId is not null) query = query.Where(x => x.EmployeeId == employeeId);
        if (groupId is not null) query = query.Where(x => x.Employee.GroupId == groupId);
        if (status is not null) query = query.Where(x => x.State == status);
        return query.OrderByDescending(x => x.Workday).ThenBy(x => x.Employee.FullName);
    }

    private static string Escape(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
}
