namespace DailyGate.Api.Domain;

public enum EmployeeState { Active, Disabled, Archived }
public enum AdminRole { Admin, Viewer }
public enum QuestionType { SingleChoice, MultipleChoice }
public enum TestInstanceState { Assigned, InProgress, Completed, TimedOut, Missed, EmergencyUnlocked }
public enum DeviceEventType { Heartbeat, SyncError, CacheCorrupted, ClockTamper, ClientCrash, OfflineLeaseExpired, EmergencyUnlock }

public sealed class EmployeeGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Employee> Employees { get; set; } = [];
}

public sealed class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FullName { get; set; }
    public required string Login { get; set; }
    public required string PasswordHash { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public EmployeeState State { get; set; } = EmployeeState.Active;
    public string? Position { get; set; }
    public Guid? GroupId { get; set; }
    public EmployeeGroup? Group { get; set; }
    public Device? Device { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string HardwareFingerprint { get; set; }
    public required string PublicKey { get; set; }
    public string ClientVersion { get; set; } = "unknown";
    public string ServiceVersion { get; set; } = "unknown";
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset OfflineLeaseExpiresAt { get; set; }
    public bool ForceSync { get; set; }
    public bool Revoked { get; set; }
}

public sealed class DeviceEnrollmentCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string CodeHash { get; set; }
    public string CodeLast4 { get; set; } = "";
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid CreatedByAdminId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public sealed class AdminUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Login { get; set; }
    public required string PasswordHash { get; set; }
    public required string TotpSecret { get; set; }
    public AdminRole Role { get; set; } = AdminRole.Admin;
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class QuestionBank
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public bool Published { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Question> Questions { get; set; } = [];
}

public sealed class Question
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionBankId { get; set; }
    public QuestionBank QuestionBank { get; set; } = null!;
    public required string Text { get; set; }
    public QuestionType Type { get; set; }
    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
    public List<QuestionOption> Options { get; set; } = [];
}

public sealed class QuestionOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public required string Text { get; set; }
    public int SortOrder { get; set; }
}

public sealed class TestRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public Guid QuestionBankId { get; set; }
    public QuestionBank QuestionBank { get; set; } = null!;
    public Guid? EmployeeGroupId { get; set; }
    public EmployeeGroup? EmployeeGroup { get; set; }
    public int QuestionCount { get; set; } = 10;
    public int TimeLimitMinutes { get; set; } = 15;
    public DateOnly EffectiveFrom { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DailyTestInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid TestRuleId { get; set; }
    public TestRule TestRule { get; set; } = null!;
    public DateOnly Workday { get; set; }
    public required string PayloadJson { get; set; }
    public required string PayloadSignature { get; set; }
    public TestInstanceState State { get; set; } = TestInstanceState.Assigned;
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Submission? Submission { get; set; }
}

public sealed class Submission
{
    public Guid Id { get; set; }
    public Guid DailyTestInstanceId { get; set; }
    public DailyTestInstance DailyTestInstance { get; set; } = null!;
    public Guid EmployeeId { get; set; }
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public string DeviceName { get; set; } = "unknown";
    public string ClientVersion { get; set; } = "unknown";
    public string ServiceVersion { get; set; } = "unknown";
    public required string IdempotencyKey { get; set; }
    public TestInstanceState Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset AcceptedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool WasOffline { get; set; }
    public string ReceiptJson { get; set; } = "";
    public string ReceiptSignature { get; set; } = "";
    public List<SubmissionAnswer> Answers { get; set; } = [];
}

public sealed class SubmissionAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    public Submission Submission { get; set; } = null!;
    public Guid QuestionId { get; set; }
    public string SelectedOptionIdsJson { get; set; } = "[]";
}

public sealed class DeviceEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public DeviceEventType Type { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset DeviceTime { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class EmergencyUnlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public Guid CreatedByAdminId { get; set; }
    public required string CodeHash { get; set; }
    public string CodeLast4 { get; set; } = "";
    public DateOnly Workday { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AdminUserId { get; set; }
    public string Actor { get; set; } = "system";
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
