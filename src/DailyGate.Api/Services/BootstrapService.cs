using DailyGate.Api.Auth;
using DailyGate.Api.Data;
using DailyGate.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DailyGate.Api.Services;

public sealed class BootstrapService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DailyGateDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        if (await db.AdminUsers.AnyAsync(cancellationToken)) return;
        var passwordService = scope.ServiceProvider.GetRequiredService<PasswordService>();
        var section = configuration.GetSection("BootstrapAdmin");
        var login = section["Login"] ?? throw new InvalidOperationException("BootstrapAdmin:Login is required.");
        var password = section["Password"] ?? throw new InvalidOperationException("BootstrapAdmin:Password is required.");

        db.AdminUsers.Add(new AdminUser
        {
            Login = login.ToLowerInvariant(),
            PasswordHash = passwordService.HashAdmin(password),
            TotpSecret = string.Empty,
            Role = AdminRole.Admin
        });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Bootstrap administrator {Login} was created. Change its password immediately.", login);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
