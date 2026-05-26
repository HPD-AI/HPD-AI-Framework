using System.Text.Json.Serialization;

namespace Rhodium.Primitives;

/// <summary>
/// A point in time. Nanosecond precision.
/// </summary>
public readonly record struct Instant(long Nanos) : IComparable<Instant>
{
    public static readonly Instant MinValue = new(long.MinValue);
    public static readonly Instant MaxValue = new(long.MaxValue);
    public static readonly Instant Epoch = new(0);

    [JsonIgnore] public static Instant Now => FromDateTimeOffset(DateTimeOffset.UtcNow);

    public static Instant FromDateTimeOffset(DateTimeOffset dto)
    {
        var unixTicks = dto.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks;
        return new Instant(checked(unixTicks * 100));
    }

    public static Instant FromUnixMillis(long millis) => new(millis * 1_000_000);
    public static Instant FromUnixSeconds(long seconds) => new(seconds * 1_000_000_000);

    public DateTimeOffset ToDateTimeOffset() =>
        new(checked(DateTimeOffset.UnixEpoch.Ticks + (Nanos / 100)), TimeSpan.Zero);

    public DateTime ToUtcDateTime() => ToDateTimeOffset().UtcDateTime;

    public int CompareTo(Instant other) => Nanos.CompareTo(other.Nanos);

    public static Instant Max(Instant a, Instant b) => a.Nanos >= b.Nanos ? a : b;
    public static Instant Min(Instant a, Instant b) => a.Nanos <= b.Nanos ? a : b;

    public static Duration operator -(Instant a, Instant b) => new(a.Nanos - b.Nanos);
    public static Instant operator +(Instant a, Duration b) => new(a.Nanos + b.Nanos);
    public static Instant operator -(Instant a, Duration b) => new(a.Nanos - b.Nanos);

    public static bool operator >(Instant a, Instant b) => a.Nanos > b.Nanos;
    public static bool operator <(Instant a, Instant b) => a.Nanos < b.Nanos;
    public static bool operator >=(Instant a, Instant b) => a.Nanos >= b.Nanos;
    public static bool operator <=(Instant a, Instant b) => a.Nanos <= b.Nanos;

    public override string ToString() => ToDateTimeOffset().ToString("O");
}

/// <summary>
/// A length of time. Nanosecond precision.
/// </summary>
public readonly record struct Duration(long Nanos) : IComparable<Duration>
{
    public static readonly Duration Zero = new(0);

    public static Duration FromNanos(long nanos) => new(nanos);
    public static Duration FromMicros(long micros) => new(micros * 1_000);
    public static Duration FromMillis(long millis) => new(millis * 1_000_000);
    public static Duration FromSeconds(long seconds) => new(seconds * 1_000_000_000);
    public static Duration FromMinutes(long minutes) => FromSeconds(minutes * 60);
    public static Duration FromHours(long hours) => FromMinutes(hours * 60);
    public static Duration FromDays(long days) => FromHours(days * 24);

    public static Duration Seconds(double s) => new((long)(s * 1_000_000_000));
    public static Duration Minutes(double m) => Seconds(m * 60);
    public static Duration Hours(double h) => Minutes(h * 60);
    public static Duration Days(double d) => Hours(d * 24);

    public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(Nanos / 100);

    [JsonIgnore] public double TotalSeconds => Nanos / 1_000_000_000.0;
    [JsonIgnore] public double TotalMinutes => TotalSeconds / 60.0;
    [JsonIgnore] public double TotalHours => TotalMinutes / 60.0;
    [JsonIgnore] public double TotalDays => TotalHours / 24.0;

    public int CompareTo(Duration other) => Nanos.CompareTo(other.Nanos);

    public static Duration operator +(Duration a, Duration b) => new(a.Nanos + b.Nanos);
    public static Duration operator -(Duration a, Duration b) => new(a.Nanos - b.Nanos);
    public static Duration operator *(Duration a, long b) => new(a.Nanos * b);
    public static Duration operator /(Duration a, long b) => new(a.Nanos / b);

    public static bool operator >(Duration a, Duration b) => a.Nanos > b.Nanos;
    public static bool operator <(Duration a, Duration b) => a.Nanos < b.Nanos;
    public static bool operator >=(Duration a, Duration b) => a.Nanos >= b.Nanos;
    public static bool operator <=(Duration a, Duration b) => a.Nanos <= b.Nanos;

    public static implicit operator Duration(TimeSpan ts) => new(ts.Ticks * 100);

    public override string ToString() => ToTimeSpan().ToString();
}

/// <summary>
/// Dual timestamp for HFT events. In HFT, there's always a gap between
/// when something happens at the exchange and when you see it.
/// The gap IS the latency you're competing against.
/// </summary>
public readonly record struct DualTimestamp(
    Instant ExchangeTime,  // When exchange processed it
    Instant LocalTime      // When we received it
)
{
    /// <summary>
    /// Feed latency (how stale your data is).
    /// </summary>
    [JsonIgnore] public Duration FeedLatency => LocalTime - ExchangeTime;

    public static DualTimestamp Now(Instant exchTime) => new(exchTime, Instant.Now);
    public static DualTimestamp Synchronized(Instant time) => new(time, time);

    public override string ToString() => $"exch={ExchangeTime}, local={LocalTime}, lag={FeedLatency}";
}
