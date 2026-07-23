namespace DailyGate.Api.Infrastructure;

public sealed class DailyGateOptions
{
    public const string Section = "DailyGate";
    public string TimeZoneId { get; init; } = "Asia/Samarkand";
    public int WorkdayStartHour { get; init; } = 4;
    public int OfflineLeaseDays { get; init; } = 7;
    public int HeartbeatOfflineMinutes { get; init; } = 15;
    public string SigningKeyPath { get; init; } = "data/server-signing-key.pem";
    public bool RunWorker { get; init; } = true;
    public bool RunBootstrap { get; init; } = true;
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; init; } = "DailyGate";
    public string Audience { get; init; } = "DailyGate.Admin";
    public required string SigningKey { get; init; }
}
