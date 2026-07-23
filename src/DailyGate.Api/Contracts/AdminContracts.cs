using DailyGate.Api.Domain;

namespace DailyGate.Api.Contracts;

public sealed record AdminLoginRequest(string Login, string Password);
public sealed record CreateAdminUserRequest(string Login, string TemporaryPassword, AdminRole Role);
public sealed record ChangeAdminPasswordRequest(string CurrentPassword, string NewPassword);
public sealed record CreateGroupRequest(string Name);
public sealed record CreateEmployeeRequest(string FullName, string Login, string TemporaryPassword, Guid? GroupId, string? Position = null);
public sealed record UpdateEmployeeRequest(string FullName, Guid? GroupId, EmployeeState State, string? Position = null);
public sealed record ResetPasswordRequest(string TemporaryPassword);
public sealed record CreateEnrollmentCodeRequest(Guid EmployeeId);
public sealed record ReassignDeviceRequest(Guid EmployeeId);
public sealed record CreateBankRequest(string Name, string? Description);
public sealed record CreateQuestionRequest(string Text, QuestionType Type, IReadOnlyList<string> Options);
public sealed record CreateRuleRequest(
    string Name,
    Guid QuestionBankId,
    Guid? EmployeeGroupId,
    int QuestionCount,
    int TimeLimitMinutes,
    DateOnly EffectiveFrom);
public sealed record CreateEmergencyCodeRequest(Guid DeviceId, DateOnly? Workday);
public sealed record DeviceEventRequest(DeviceEventType Type, string? Details, DateTimeOffset DeviceTime);
