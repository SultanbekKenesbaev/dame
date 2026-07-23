using System.Security.Claims;
using DailyGate.Api.Auth;
using DailyGate.Api.Contracts;
using DailyGate.Api.Data;
using DailyGate.Api.Domain;
using DailyGate.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DailyGate.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/auth").WithTags("Admin authentication");

        group.MapPost("/login", async (AdminLoginRequest request, DailyGateDbContext db,
            PasswordService passwords, TokenService tokens, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
                return Results.Unauthorized();
            var login = request.Login.Trim().ToLowerInvariant();
            var user = await db.AdminUsers.SingleOrDefaultAsync(x => x.Login == login && x.Active);
            if (user is null || !passwords.Verify(request.Password, user.PasswordHash))
                return Results.Unauthorized();

            user.LastLoginAt = DateTimeOffset.UtcNow;
            db.AuditEvents.Add(new AuditEvent
            {
                AdminUserId = user.Id,
                Actor = user.Login,
                Action = "admin.login",
                EntityType = nameof(AdminUser),
                EntityId = user.Id.ToString()
            });
            await db.SaveChangesAsync();

            var token = tokens.CreateAdminToken(user);
            context.Response.Cookies.Append("dailygate_admin", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                MaxAge = TimeSpan.FromHours(8),
                Path = "/"
            });
            return Results.Ok(new { user.Id, user.Login, role = user.Role.ToString() });
        }).AllowAnonymous().RequireRateLimiting("auth");

        group.MapPost("/logout", (HttpContext context) =>
        {
            context.Response.Cookies.Delete("dailygate_admin", new CookieOptions { Path = "/" });
            return Results.NoContent();
        }).RequireAuthorization("Viewer");

        group.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            id = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            login = user.Identity?.Name,
            role = user.FindFirstValue(ClaimTypes.Role)
        })).RequireAuthorization("Viewer");

        group.MapPost("/change-password", async (ChangeAdminPasswordRequest request, ClaimsPrincipal principal,
            DailyGateDbContext db, PasswordService passwords) =>
        {
            if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword)
                || request.NewPassword.Length < 8)
                return Results.BadRequest(new { message = "New password must contain at least 8 characters." });
            var id = principal.AdminId();
            var user = await db.AdminUsers.FindAsync(id);
            if (user is null || !passwords.Verify(request.CurrentPassword, user.PasswordHash)) return Results.Unauthorized();
            user.PasswordHash = passwords.HashAdmin(request.NewPassword);
            db.AuditEvents.Add(new AuditEvent { AdminUserId = id, Actor = user.Login, Action = "admin.password_changed", EntityType = nameof(AdminUser), EntityId = id.ToString() });
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization("Viewer");

        var users = app.MapGroup("/api/v1/admin/users").WithTags("Admin users").RequireAuthorization("AdminOnly");
        users.MapGet("/", async (DailyGateDbContext db) => Results.Ok(await db.AdminUsers.AsNoTracking().OrderBy(x => x.Login)
            .Select(x => new { x.Id, x.Login, role = x.Role.ToString(), x.Active, x.CreatedAt, x.LastLoginAt }).ToListAsync()));
        users.MapPost("/", async (CreateAdminUserRequest request, DailyGateDbContext db,
            PasswordService passwords, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(request.Login) || request.Login.Length > 100
                || string.IsNullOrEmpty(request.TemporaryPassword) || request.TemporaryPassword.Length < 8)
                return Results.BadRequest(new { message = "Login is required and temporary password must contain at least 8 characters." });
            var entity = new AdminUser
            {
                Login = request.Login.Trim().ToLowerInvariant(), PasswordHash = passwords.HashAdmin(request.TemporaryPassword),
                TotpSecret = string.Empty, Role = request.Role
            };
            db.AdminUsers.Add(entity); await db.SaveChangesAsync();
            await audit.WriteAsync("admin.created", nameof(AdminUser), entity.Id, new { entity.Login, entity.Role });
            return Results.Created($"/api/v1/admin/users/{entity.Id}", new
                { entity.Id, entity.Login, role = entity.Role.ToString() });
        });

        return app;
    }
}
