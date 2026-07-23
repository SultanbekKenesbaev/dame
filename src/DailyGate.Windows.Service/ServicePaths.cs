namespace DailyGate.Windows.Service;

public static class ServicePaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DailyGate");
    public static string Database => Path.Combine(Root, "dailygate.db");
    public static string Credentials => Path.Combine(Root, "device.dat");
    public static string Settings => Path.Combine(Root, "settings.json");
    public const string PipeName = "DailyGate.Client";

    public static void Ensure() => Directory.CreateDirectory(Root);
}

public sealed record ServiceSettings(
    string ApiBaseUrl,
    Guid DeviceId,
    Guid EmployeeId,
    string EmployeeLogin,
    string ServerPublicKey,
    string TimeZoneId,
    int WorkdayStartHour,
    string? AllowedClientSid = null,
    bool DemoMode = false);
