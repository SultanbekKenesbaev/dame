using System.Text.Json;
using System.Text.Json.Serialization;

namespace DailyGate.Shared;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuestionKind
{
    SingleChoice,
    MultipleChoice
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubmissionKind
{
    Completed,
    TimedOut,
    EmergencyUnlocked
}

public sealed record DeviceEnrollmentRequest(
    string EnrollmentCode,
    string Name,
    string HardwareFingerprint,
    string PublicKey,
    string ClientVersion);

public sealed record DeviceEnrollmentResponse(
    Guid DeviceId,
    Guid EmployeeId,
    string EmployeeLogin,
    string ServerPublicKey,
    DateTimeOffset ServerTime,
    string TimeZoneId,
    int WorkdayStartHour);

public sealed record EmployeeLoginRequest(string Login, string Password);

public sealed record EmployeeLoginResponse(
    Guid EmployeeId,
    string FullName,
    bool MustChangePassword,
    string AccessToken,
    string OfflineVerifier,
    DateTimeOffset OfflineLeaseExpiresAt);

public sealed record PasswordChangeRequest(string CurrentPassword, string NewPassword);

public sealed record DeviceHeartbeatRequest(
    string ClientVersion,
    string ServiceVersion,
    string State,
    DateTimeOffset DeviceTime,
    string? LastError);

public sealed record DeviceSyncResponse(
    Guid EmployeeId,
    string EmployeeLogin,
    DateTimeOffset ServerTime,
    string TimeZoneId,
    int WorkdayStartHour,
    DateTimeOffset OfflineLeaseExpiresAt,
    bool ForceSync,
    bool EmployeeActive,
    IReadOnlyList<SignedDailyTest> Tests,
    string ServerPublicKey);

public sealed record SignedDailyTest(
    Guid InstanceId,
    DateOnly Workday,
    string PayloadJson,
    string Signature,
    string SignatureAlgorithm = "ECDSA_P256_SHA256")
{
    public DailyTestPayload Payload() =>
        JsonSerializer.Deserialize<DailyTestPayload>(PayloadJson, JsonDefaults.Options)
        ?? throw new InvalidOperationException("Invalid signed test payload.");
}

public sealed record DailyTestPayload(
    Guid InstanceId,
    Guid EmployeeId,
    DateOnly Workday,
    string Title,
    int TimeLimitMinutes,
    DateTimeOffset IssuedAt,
    IReadOnlyList<DailyQuestion> Questions);

public sealed record DailyQuestion(
    Guid Id,
    string Text,
    QuestionKind Kind,
    bool Required,
    IReadOnlyList<DailyOption> Options);

public sealed record DailyOption(Guid Id, string Text);

public sealed record SubmissionRequest(
    Guid SubmissionId,
    Guid InstanceId,
    string IdempotencyKey,
    SubmissionKind Status,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    bool WasOffline,
    IReadOnlyList<SubmissionAnswerRequest> Answers);

public sealed record SubmissionAnswerRequest(Guid QuestionId, IReadOnlyList<Guid> SelectedOptionIds);

public sealed record CompletionReceipt(
    Guid SubmissionId,
    Guid EmployeeId,
    DateOnly Workday,
    SubmissionKind Status,
    DateTimeOffset AcceptedAt,
    string ReceiptJson,
    string Signature);

public sealed record EmergencyUnlockRequest(string Code, DateOnly Workday);

public sealed record EmergencyUnlockResponse(
    bool Accepted,
    DateOnly Workday,
    DateTimeOffset AcceptedAt,
    string ReceiptJson,
    string Signature);

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
