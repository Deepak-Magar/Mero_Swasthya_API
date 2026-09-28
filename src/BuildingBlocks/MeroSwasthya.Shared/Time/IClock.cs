namespace MeroSwasthya.Shared.Time;

/// <summary>
/// The server clock. All timestamps are UTC and truncated to whole milliseconds, so what is
/// stored (PostgreSQL keeps microseconds) is exactly what goes over the wire (A.1 uses ms).
/// A cursor echoed back by a client therefore compares equal to the stored value.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }

    DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => Truncate(DateTime.UtcNow);

    public static DateTime Truncate(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
}
