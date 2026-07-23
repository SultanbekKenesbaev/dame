namespace DailyGate.Shared;

public static class WorkdayCalculator
{
    public static DateOnly GetWorkday(DateTimeOffset instant, TimeZoneInfo timeZone, int startHour = 4)
    {
        if (startHour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(startHour));
        }

        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        var date = DateOnly.FromDateTime(local.DateTime);
        return local.Hour < startHour ? date.AddDays(-1) : date;
    }

    public static DateTimeOffset StartOfWorkday(DateOnly workday, TimeZoneInfo timeZone, int startHour = 4)
    {
        var local = workday.ToDateTime(new TimeOnly(startHour, 0), DateTimeKind.Unspecified);
        var offset = timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
