using DailyGate.Api.Infrastructure;
using DailyGate.Shared;
using Microsoft.Extensions.Options;

namespace DailyGate.Api.Services;

public sealed class WorkdayService
{
    private readonly DailyGateOptions _options;
    public TimeZoneInfo TimeZone { get; }

    public WorkdayService(IOptions<DailyGateOptions> options)
    {
        _options = options.Value;
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZoneId);
    }

    public DateOnly Current(DateTimeOffset? now = null)
        => WorkdayCalculator.GetWorkday(now ?? DateTimeOffset.UtcNow, TimeZone, _options.WorkdayStartHour);

    public int StartHour => _options.WorkdayStartHour;
    public string TimeZoneId => _options.TimeZoneId;
}
