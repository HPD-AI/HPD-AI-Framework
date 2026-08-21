using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Defines one closed durable schedule expression.</summary>
public abstract record BaseScheduleExpression
{
    private protected BaseScheduleExpression() { }
}

/// <summary>Runs exactly once at one UTC Unix-millisecond instant.</summary>
public sealed record BaseOnceSchedule(long At) : BaseScheduleExpression;

/// <summary>Runs on one checked fixed interval from a UTC anchor.</summary>
public sealed record BaseIntervalSchedule(long Anchor, long EveryMilliseconds) : BaseScheduleExpression;

/// <summary>Runs from one normalized six-field cron expression in an installed time zone.</summary>
public sealed record BaseCronSchedule(string Expression, string TimeZoneId) : BaseScheduleExpression;

/// <summary>Classifies calendar recurrence frequency.</summary>
public enum BaseCalendarFrequency
{
    /// <summary>Advances in seconds.</summary>
    Secondly,
    /// <summary>Advances in minutes.</summary>
    Minutely,
    /// <summary>Advances in hours.</summary>
    Hourly,
    /// <summary>Advances in calendar days.</summary>
    Daily,
    /// <summary>Advances in calendar weeks.</summary>
    Weekly,
    /// <summary>Advances in calendar months.</summary>
    Monthly,
    /// <summary>Advances in calendar years.</summary>
    Yearly,
}

/// <summary>Contains a local wall-clock time without a date.</summary>
public sealed record BaseLocalTime
{
    /// <summary>Gets hour 0..23.</summary>
    public required int Hour { get; init; }
    /// <summary>Gets minute 0..59.</summary>
    public required int Minute { get; init; }
    /// <summary>Gets second 0..59.</summary>
    public required int Second { get; init; }
    /// <summary>Gets millisecond 0..999.</summary>
    public required int Millisecond { get; init; }
}

/// <summary>Defines one closed calendar selector.</summary>
public abstract record BaseCalendarSelector
{
    private protected BaseCalendarSelector() { }
}

/// <summary>Selects every valid period.</summary>
public sealed record BaseEveryCalendarPeriod : BaseCalendarSelector;
/// <summary>Selects an exact sorted weekday set using Sunday zero.</summary>
public sealed record BaseWeekdayCalendarSelector(ImmutableArray<int> Weekdays) : BaseCalendarSelector;
/// <summary>Selects one day 1..31, skipping invalid dates.</summary>
public sealed record BaseMonthDayCalendarSelector(int Day) : BaseCalendarSelector;
/// <summary>Selects one month and day.</summary>
public sealed record BaseYearDayCalendarSelector(int Month, int Day) : BaseCalendarSelector;
/// <summary>Selects one ordinal weekday in a month; ordinal is 1..5 or -1 for last.</summary>
public sealed record BaseOrdinalWeekdayCalendarSelector(int Ordinal, int Weekday) : BaseCalendarSelector;

/// <summary>Runs from one closed calendar recurrence.</summary>
public sealed record BaseCalendarSchedule(
    BaseCalendarFrequency Frequency,
    int Interval,
    BaseLocalTime LocalTime,
    BaseCalendarSelector Selector,
    string TimeZoneId) : BaseScheduleExpression;

/// <summary>Classifies forward-gap handling.</summary>
public enum BaseTimeGapPolicy
{
    /// <summary>Omits a nonexistent local instant.</summary>
    Skip,
    /// <summary>Moves to the first valid instant after the gap.</summary>
    NextValid,
    /// <summary>Moves to the final valid instant before the gap.</summary>
    PreviousValid,
}
/// <summary>Classifies ambiguous-overlap handling.</summary>
public enum BaseTimeOverlapPolicy
{
    /// <summary>Selects the earlier UTC offset.</summary>
    EarlierOffset,
    /// <summary>Selects the later UTC offset.</summary>
    LaterOffset,
    /// <summary>Materializes both instants in stable ordinal order.</summary>
    Both,
}
/// <summary>Classifies missed-occurrence handling.</summary>
public enum BaseScheduleMisfirePolicy
{
    /// <summary>Records every missed occurrence without activation creation.</summary>
    Skip,
    /// <summary>Runs only the greatest currently missed occurrence.</summary>
    RunLatest,
    /// <summary>Runs every missed occurrence through bounded pages.</summary>
    RunAll,
}
/// <summary>Classifies active-occurrence overlap handling.</summary>
public enum BaseScheduleOverlapPolicy
{
    /// <summary>Allows concurrent active occurrences.</summary>
    Allow,
    /// <summary>Skips while another matching activation is active.</summary>
    SkipWhileActive,
    /// <summary>Materializes all occurrences and admits only the earliest active one.</summary>
    Queue,
    /// <summary>Fences predecessors through durable bounded cancellation maintenance.</summary>
    CancelPrevious,
}
/// <summary>Classifies the installed overlap-key authority.</summary>
public enum BaseScheduleOverlapKeyKind
{
    /// <summary>Uses schedule identity.</summary>
    Schedule,
    /// <summary>Uses activation definition plus protected scope.</summary>
    DefinitionScope,
    /// <summary>Uses an explicitly declared canonical key.</summary>
    CanonicalConcurrencyKey,
}

/// <summary>Defines one graph-installed durable schedule.</summary>
public sealed record BaseScheduleDefinition
{
    /// <summary>Gets stable schedule identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets positive schedule version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets target activation definition.</summary>
    public required BaseActivationDefinitionKey Activation { get; init; }
    /// <summary>Gets canonical activation input bytes.</summary>
    public required ImmutableArray<byte> CanonicalInput { get; init; }
    /// <summary>Gets input checksum.</summary>
    public required ImmutableArray<byte> InputChecksum { get; init; }
    /// <summary>Gets schedule expression.</summary>
    public required BaseScheduleExpression Expression { get; init; }
    /// <summary>Gets gap policy.</summary>
    public required BaseTimeGapPolicy GapPolicy { get; init; }
    /// <summary>Gets overlap policy for ambiguous local time.</summary>
    public required BaseTimeOverlapPolicy TimeOverlapPolicy { get; init; }
    /// <summary>Gets misfire policy.</summary>
    public required BaseScheduleMisfirePolicy MisfirePolicy { get; init; }
    /// <summary>Gets activation overlap policy.</summary>
    public required BaseScheduleOverlapPolicy ActivationOverlapPolicy { get; init; }
    /// <summary>Gets overlap-key kind.</summary>
    public required BaseScheduleOverlapKeyKind OverlapKeyKind { get; init; }
    /// <summary>Gets optional canonical concurrency key.</summary>
    public ImmutableArray<byte> ConcurrencyKey { get; init; }
    /// <summary>Gets declared priority -32..32.</summary>
    public required int Priority { get; init; }
    /// <summary>Gets deterministic maximum splay milliseconds.</summary>
    public required long MaximumSplayMilliseconds { get; init; }
    /// <summary>Gets exact schedule checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains current durable schedule authority.</summary>
public sealed record BaseScheduleAuthority
{
    /// <summary>Gets installed definition.</summary>
    public required BaseScheduleDefinition Definition { get; init; }
    /// <summary>Gets positive definition generation.</summary>
    public required long DefinitionGeneration { get; init; }
    /// <summary>Gets whether new occurrences are enabled.</summary>
    public required bool Enabled { get; init; }
    /// <summary>Gets positive semantic schedule epoch.</summary>
    public required long ScheduleEpoch { get; init; }
    /// <summary>Gets last considered nominal instant.</summary>
    public long? LastConsideredNominal { get; init; }
    /// <summary>Gets next nominal instant, or null when exhausted.</summary>
    public long? NextNominal { get; init; }
    /// <summary>Gets canonical authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Defines immutable occurrence disposition authority.</summary>
public abstract record BaseScheduleOccurrenceDisposition
{
    private protected BaseScheduleOccurrenceDisposition() { }
}

/// <summary>Records a materialized activation.</summary>
public sealed record BaseOccurrenceMaterialized(string ActivationId) : BaseScheduleOccurrenceDisposition;
/// <summary>Records a skipped misfire.</summary>
public sealed record BaseOccurrenceSkippedMisfire : BaseScheduleOccurrenceDisposition;
/// <summary>Records an overlap skip.</summary>
public sealed record BaseOccurrenceSkippedOverlap(string BlockingActivationId) : BaseScheduleOccurrenceDisposition;
/// <summary>Records explicit occurrence cancellation.</summary>
public sealed record BaseOccurrenceCancelled(string CancellationReceiptId) : BaseScheduleOccurrenceDisposition;
/// <summary>Records suppression by schedule replacement.</summary>
public sealed record BaseOccurrenceSuppressedByReplacement(long ReplacementGeneration) : BaseScheduleOccurrenceDisposition;
/// <summary>Records suppression by authenticated restore floor.</summary>
public sealed record BaseOccurrenceSuppressedByRestoreFloor(ImmutableArray<byte> FloorChecksum) : BaseScheduleOccurrenceDisposition;

/// <summary>Contains one immutable nominal-occurrence fact.</summary>
public sealed record BaseScheduleOccurrenceFact
{
    /// <summary>Gets deterministic occurrence identity.</summary>
    public required string OccurrenceId { get; init; }
    /// <summary>Gets schedule identity and epoch.</summary>
    public required string ScheduleId { get; init; }
    /// <summary>Gets positive schedule epoch.</summary>
    public required long ScheduleEpoch { get; init; }
    /// <summary>Gets nominal UTC instant.</summary>
    public required long NominalAt { get; init; }
    /// <summary>Gets effective UTC instant after deterministic splay.</summary>
    public required long EffectiveAt { get; init; }
    /// <summary>Gets overlap ordinal for ambiguous local instants.</summary>
    public required int OverlapOrdinal { get; init; }
    /// <summary>Gets immutable disposition.</summary>
    public required BaseScheduleOccurrenceDisposition Disposition { get; init; }
    /// <summary>Gets canonical fact checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}
