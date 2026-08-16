namespace Merfit.Application.Common.Interfaces;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
    DateOnly TodayUtc { get; }

    /// <summary>Converts UtcNow into the given IANA timezone's local calendar date — used for streak/day-boundary math, never a raw UTC date.</summary>
    DateOnly TodayInTimeZone(string ianaTimeZoneId);
}
