using DailyGate.Api.Auth;
using DailyGate.Api.Contracts;
using DailyGate.Api.Data;
using DailyGate.Api.Domain;
using DailyGate.Api.Infrastructure;
using DailyGate.Api.Services;
using DailyGate.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DailyGate.Api.Endpoints;

public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var groups = app.MapGroup("/api/v1/admin/groups").WithTags("Employees");
        groups.MapGet("/", async (DailyGateDbContext db) =>
            Results.Ok(await db.EmployeeGroups.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name, employeeCount = x.Employees.Count }).ToListAsync())).RequireAuthorization("Viewer");
        groups.MapPost("/", async (CreateGroupRequest request, DailyGateDbContext db, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 160)
                return Results.BadRequest(new { message = "Group name is required and must not exceed 160 characters." });
            var entity = new EmployeeGroup { Name = request.Name.Trim() };
            db.EmployeeGroups.Add(entity);
            await db.SaveChangesAsync();
            await audit.WriteAsync("group.created", nameof(EmployeeGroup), entity.Id, new { entity.Name });
            return Results.Created($"/api/v1/admin/groups/{entity.Id}", new { entity.Id, entity.Name });
        }).RequireAuthorization("AdminOnly");

        var employees = app.MapGroup("/api/v1/admin/employees").WithTags("Employees");
        employees.MapGet("/", async (DailyGateDbContext db, string? search, EmployeeState? state) =>
        {
            var query = db.Employees.AsNoTracking().Include(x => x.Group).Include(x => x.Device).AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(x => x.FullName.ToLower().Contains(search.ToLower()) || x.Login.ToLower().Contains(search.ToLower()));
            if (state is not null) query = query.Where(x => x.State == state);
            return Results.Ok(await query.OrderBy(x => x.FullName).Select(x => new
            {
                x.Id, x.FullName, x.Login, x.Position, state = x.State.ToString(), x.MustChangePassword,
                group = x.Group == null ? null : new { x.Group.Id, x.Group.Name },
                device = x.Device == null ? null : new { x.Device.Id, x.Device.Name, x.Device.LastSeenAt }
            }).ToListAsync());
        }).RequireAuthorization("Viewer");
        employees.MapPost("/", async (CreateEmployeeRequest request, DailyGateDbContext db,
            PasswordService passwords, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Length > 240
                || string.IsNullOrWhiteSpace(request.Login) || request.Login.Length > 100
                || string.IsNullOrEmpty(request.TemporaryPassword) || request.TemporaryPassword.Length < 12
                || request.Position?.Length > 160)
                return Results.BadRequest(new { message = "Name and login are required; temporary password must contain at least 12 characters." });
            var entity = new Employee
            {
                FullName = request.FullName.Trim(),
                Login = request.Login.Trim().ToLowerInvariant(),
                PasswordHash = passwords.Hash(request.TemporaryPassword),
                Position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim(),
                GroupId = request.GroupId
            };
            db.Employees.Add(entity);
            await db.SaveChangesAsync();
            await audit.WriteAsync("employee.created", nameof(Employee), entity.Id, new { entity.FullName, entity.Login });
            return Results.Created($"/api/v1/admin/employees/{entity.Id}", new { entity.Id });
        }).RequireAuthorization("AdminOnly");
        employees.MapPut("/{id:guid}", async (Guid id, UpdateEmployeeRequest request, DailyGateDbContext db, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Length > 240 || request.Position?.Length > 160)
                return Results.BadRequest(new { message = "Employee name is required and must not exceed 240 characters." });
            var employee = await db.Employees.FindAsync(id);
            if (employee is null) return Results.NotFound();
            employee.FullName = request.FullName.Trim();
            employee.Position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim();
            employee.GroupId = request.GroupId;
            employee.State = request.State;
            employee.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await audit.WriteAsync("employee.updated", nameof(Employee), id, request);
            return Results.NoContent();
        }).RequireAuthorization("AdminOnly");
        employees.MapPost("/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest request,
            DailyGateDbContext db, PasswordService passwords, AuditService audit) =>
        {
            if (string.IsNullOrEmpty(request.TemporaryPassword) || request.TemporaryPassword.Length < 12)
                return Results.BadRequest(new { message = "Temporary password must contain at least 12 characters." });
            var employee = await db.Employees.FindAsync(id);
            if (employee is null) return Results.NotFound();
            employee.PasswordHash = passwords.Hash(request.TemporaryPassword);
            employee.MustChangePassword = true;
            await db.SaveChangesAsync();
            await audit.WriteAsync("employee.password_reset", nameof(Employee), id);
            return Results.NoContent();
        }).RequireAuthorization("AdminOnly");

        var deviceLogin = app.MapGroup("/api/v1/device").WithTags("Device").RequireAuthorization(DeviceSignatureAuthenticationHandler.Scheme);
        deviceLogin.MapPost("/employee/login", async (EmployeeLoginRequest request, HttpContext context,
            DailyGateDbContext db, PasswordService passwords, TokenService tokens, WorkdayService workday,
            IOptions<DailyGateOptions> options) =>
        {
            if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
                return Results.Unauthorized();
            var deviceId = context.User.DeviceId();
            var device = await db.Devices.Include(x => x.Employee).SingleAsync(x => x.Id == deviceId);
            var employee = device.Employee;
            if (employee.State != EmployeeState.Active
                || !employee.Login.Equals(request.Login.Trim(), StringComparison.OrdinalIgnoreCase)
                || !passwords.Verify(request.Password, employee.PasswordHash)) return Results.Unauthorized();

            device.LastSeenAt = DateTimeOffset.UtcNow;
            device.LastSyncAt = DateTimeOffset.UtcNow;
            device.OfflineLeaseExpiresAt = DateTimeOffset.UtcNow.AddDays(options.Value.OfflineLeaseDays);
            var currentTest = await db.DailyTestInstances.SingleOrDefaultAsync(x =>
                x.EmployeeId == employee.Id && x.Workday == workday.Current());
            if (currentTest is { State: TestInstanceState.Assigned })
            {
                currentTest.State = TestInstanceState.InProgress;
                currentTest.StartedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new EmployeeLoginResponse(
                employee.Id, employee.FullName, employee.MustChangePassword,
                tokens.CreateEmployeeToken(employee, device), passwords.CreateOfflineVerifier(request.Password),
                device.OfflineLeaseExpiresAt));
        });
        deviceLogin.MapPost("/employee/change-password", async (PasswordChangeRequest request, HttpContext context,
            DailyGateDbContext db, PasswordService passwords) =>
        {
            if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword)
                || request.NewPassword.Length < 12)
                return Results.BadRequest(new { message = "New password must contain at least 12 characters." });
            var device = await db.Devices.Include(x => x.Employee).SingleAsync(x => x.Id == context.User.DeviceId());
            if (!passwords.Verify(request.CurrentPassword, device.Employee.PasswordHash)) return Results.Unauthorized();
            device.Employee.PasswordHash = passwords.Hash(request.NewPassword);
            device.Employee.MustChangePassword = false;
            device.Employee.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

}
