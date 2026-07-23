using DailyGate.Shared;

namespace DailyGate.Api.Tests;

public sealed class WorkdayCalculatorTests
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Samarkand");

    [Fact]
    public void BeforeFourBelongsToPreviousWorkday()
    {
        var instant = new DateTimeOffset(2026, 7, 20, 3, 59, 59, TimeSpan.FromHours(5));
        Assert.Equal(new DateOnly(2026, 7, 19), WorkdayCalculator.GetWorkday(instant, _zone));
    }

    [Fact]
    public void FourStartsNewWorkday()
    {
        var instant = new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.FromHours(5));
        Assert.Equal(new DateOnly(2026, 7, 20), WorkdayCalculator.GetWorkday(instant, _zone));
    }

    [Fact]
    public void RebootDoesNotChangeWorkdayIdentity()
    {
        var morning = new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.FromHours(5));
        var evening = new DateTimeOffset(2026, 7, 20, 23, 30, 0, TimeSpan.FromHours(5));
        Assert.Equal(WorkdayCalculator.GetWorkday(morning, _zone), WorkdayCalculator.GetWorkday(evening, _zone));
    }
}
