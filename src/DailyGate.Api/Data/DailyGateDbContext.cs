using DailyGate.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DailyGate.Api.Data;

public sealed class DailyGateDbContext(DbContextOptions<DailyGateDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeGroup> EmployeeGroups => Set<EmployeeGroup>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceEnrollmentCode> DeviceEnrollmentCodes => Set<DeviceEnrollmentCode>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<QuestionBank> QuestionBanks => Set<QuestionBank>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<TestRule> TestRules => Set<TestRule>();
    public DbSet<DailyTestInstance> DailyTestInstances => Set<DailyTestInstance>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionAnswer> SubmissionAnswers => Set<SubmissionAnswer>();
    public DbSet<DeviceEvent> DeviceEvents => Set<DeviceEvent>();
    public DbSet<EmergencyUnlock> EmergencyUnlocks => Set<EmergencyUnlock>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasIndex(x => x.Login).IsUnique();
            entity.Property(x => x.State).HasConversion<string>();
            entity.Property(x => x.Login).HasMaxLength(100);
            entity.Property(x => x.FullName).HasMaxLength(240);
            entity.Property(x => x.Position).HasMaxLength(160);
        });

        modelBuilder.Entity<EmployeeGroup>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Device>().HasIndex(x => x.HardwareFingerprint).IsUnique();
        modelBuilder.Entity<Device>().HasIndex(x => x.EmployeeId).IsUnique();
        modelBuilder.Entity<AdminUser>().HasIndex(x => x.Login).IsUnique();
        modelBuilder.Entity<AdminUser>().Property(x => x.Role).HasConversion<string>();
        modelBuilder.Entity<Question>().Property(x => x.Type).HasConversion<string>();
        modelBuilder.Entity<DailyTestInstance>().Property(x => x.State).HasConversion<string>();
        modelBuilder.Entity<Submission>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<DeviceEvent>().Property(x => x.Type).HasConversion<string>();

        modelBuilder.Entity<DailyTestInstance>()
            .HasIndex(x => new { x.EmployeeId, x.Workday }).IsUnique();
        modelBuilder.Entity<Submission>()
            .HasIndex(x => x.DailyTestInstanceId).IsUnique();
        modelBuilder.Entity<Submission>()
            .HasIndex(x => x.IdempotencyKey).IsUnique();
        modelBuilder.Entity<Submission>()
            .HasOne(x => x.Device).WithMany()
            .HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Device>()
            .HasOne(x => x.Employee).WithOne(x => x.Device)
            .HasForeignKey<Device>(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DailyTestInstance>()
            .HasOne(x => x.Submission).WithOne(x => x.DailyTestInstance)
            .HasForeignKey<Submission>(x => x.DailyTestInstanceId);
    }
}
