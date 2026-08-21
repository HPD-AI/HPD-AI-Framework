using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Builds and canonically seals graph-owned durable schedule definitions.</summary>
public static class BaseScheduleDefinitionBuilder
{
    /// <summary>Validates, normalizes, and checksums one schedule definition.</summary>
    public static BaseScheduleDefinition Create(BaseScheduleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        BaseApplicationId.Validate(definition.Id, nameof(definition.Id));
        BaseApplicationId.Validate(definition.OwningModuleId, nameof(definition.OwningModuleId));
        BaseApplicationId.Validate(definition.ManageGrantId, nameof(definition.ManageGrantId));
        BaseApplicationId.Validate(definition.MaterializeGrantId, nameof(definition.MaterializeGrantId));
        if (definition.Version <= 0 || definition.Activation.Version <= 0 || definition.Activation.Checksum.Length != 32 ||
            definition.InputChecksum.Length != 32 || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(definition.CanonicalInput.AsSpan()), definition.InputChecksum.AsSpan()) ||
            definition.Priority is < -32 or > 32 || definition.MaximumSplayMilliseconds is < 0 or > 86_400_000)
            throw new InvalidOperationException("base.activation.scheduleInvalid");
        if (definition.OverlapKeyKind == BaseScheduleOverlapKeyKind.CanonicalConcurrencyKey != !definition.ConcurrencyKey.IsDefaultOrEmpty)
            throw new InvalidOperationException("base.activation.scheduleInvalid");

        BaseScheduleExpression expression = Normalize(definition.Expression);
        var normalized = definition with
        {
            Id = new string(definition.Id.AsSpan()),
            OwningModuleId = new string(definition.OwningModuleId.AsSpan()),
            ManageGrantId = new string(definition.ManageGrantId.AsSpan()),
            MaterializeGrantId = new string(definition.MaterializeGrantId.AsSpan()),
            Activation = definition.Activation with { Checksum = definition.Activation.Checksum.ToArray().ToImmutableArray() },
            CanonicalInput = definition.CanonicalInput.ToArray().ToImmutableArray(),
            InputChecksum = definition.InputChecksum.ToArray().ToImmutableArray(),
            ConcurrencyKey = definition.ConcurrencyKey.IsDefault
                ? ImmutableArray<byte>.Empty
                : definition.ConcurrencyKey.ToArray().ToImmutableArray(),
            Expression = expression,
            Checksum = ImmutableArray<byte>.Empty,
        };
        return normalized with { Checksum = Checksum(normalized).ToImmutableArray() };
    }

    /// <summary>Returns the next nominal UTC instant strictly after the supplied boundary.</summary>
    public static long? NextNominal(BaseScheduleExpression expression, long? after)
    {
        ArgumentNullException.ThrowIfNull(expression);
        long boundary = after ?? -1;
        return expression switch
        {
            BaseOnceSchedule once => once.At > boundary ? once.At : null,
            BaseIntervalSchedule interval => NextInterval(interval, boundary),
            BaseCronSchedule cron when cron.TimeZoneId == "UTC" => NextCron(cron.Expression, boundary),
            BaseCalendarSchedule calendar when calendar.TimeZoneId == "UTC" => NextCalendar(calendar, boundary),
            _ => throw new InvalidOperationException("base.activation.timeZoneUnavailable"),
        };
    }

    private static BaseScheduleExpression Normalize(BaseScheduleExpression expression) => expression switch
    {
        BaseOnceSchedule once when once.At >= 0 => once,
        BaseIntervalSchedule interval when interval.Anchor >= 0 && interval.EveryMilliseconds is >= 1 and <= 3_155_760_000_000L => interval,
        BaseCronSchedule cron => cron with { Expression = Cron.Parse(cron.Expression).Canonical, TimeZoneId = NormalizeZone(cron.TimeZoneId) },
        BaseCalendarSchedule calendar => NormalizeCalendar(calendar),
        _ => throw new InvalidOperationException("base.activation.scheduleInvalid"),
    };

    private static BaseCalendarSchedule NormalizeCalendar(BaseCalendarSchedule calendar)
    {
        if (calendar.Interval is < 1 or > 1_000_000 || calendar.LocalTime.Hour is < 0 or > 23 ||
            calendar.LocalTime.Minute is < 0 or > 59 || calendar.LocalTime.Second is < 0 or > 59 ||
            calendar.LocalTime.Millisecond is < 0 or > 999)
            throw new InvalidOperationException("base.activation.scheduleInvalid");
        BaseCalendarSelector selector = calendar.Selector switch
        {
            BaseEveryCalendarPeriod => calendar.Selector,
            BaseWeekdayCalendarSelector weekdays when !weekdays.Weekdays.IsDefaultOrEmpty &&
                weekdays.Weekdays.All(static value => value is >= 0 and <= 6) =>
                new BaseWeekdayCalendarSelector(weekdays.Weekdays.Distinct().Order().ToImmutableArray()),
            BaseMonthDayCalendarSelector day when day.Day is >= 1 and <= 31 => day,
            BaseYearDayCalendarSelector day when day.Month is >= 1 and <= 12 && day.Day is >= 1 and <= 31 => day,
            BaseOrdinalWeekdayCalendarSelector ordinal when ordinal.Ordinal is >= 1 and <= 5 or -1 && ordinal.Weekday is >= 0 and <= 6 => ordinal,
            _ => throw new InvalidOperationException("base.activation.scheduleInvalid"),
        };
        return calendar with { Selector = selector, TimeZoneId = NormalizeZone(calendar.TimeZoneId) };
    }

    private static string NormalizeZone(string zone)
    {
        if (string.IsNullOrWhiteSpace(zone) || !string.Equals(zone, zone.Normalize(NormalizationForm.FormC), StringComparison.Ordinal))
            throw new InvalidOperationException("base.activation.scheduleInvalid");
        return new string(zone.AsSpan());
    }

    private static long? NextInterval(BaseIntervalSchedule value, long after)
    {
        if (value.Anchor > after) return value.Anchor;
        long delta = checked(after - value.Anchor);
        long n = checked(delta / value.EveryMilliseconds + 1);
        try { return checked(value.Anchor + n * value.EveryMilliseconds); }
        catch (OverflowException) { return null; }
    }

    private static long? NextCron(string expression, long after)
    {
        Cron cron = Cron.Parse(expression);
        DateTimeOffset start = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, after)).ToUniversalTime();
        for (int year = start.Year; year <= 9999; year++)
        foreach (int month in cron.Month)
        {
            if (year == start.Year && month < start.Month) continue;
            int days = DateTime.DaysInMonth(year, month);
            for (int day = 1; day <= days; day++)
            {
                DateTime date = new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
                bool dom = cron.DayOfMonth.Contains(day);
                bool dow = cron.DayOfWeek.Contains((int)date.DayOfWeek);
                bool dayMatches = cron.DayOfMonthWildcard ? dow : cron.DayOfWeekWildcard ? dom : dom || dow;
                if (!dayMatches) continue;
                foreach (int hour in cron.Hour)
                foreach (int minute in cron.Minute)
                foreach (int second in cron.Second)
                {
                    long candidate = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero).ToUnixTimeMilliseconds();
                    if (candidate > after) return candidate;
                }
            }
        }
        return null;
    }

    private static long? NextCalendar(BaseCalendarSchedule value, long after)
    {
        DateTimeOffset cursor = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, after)).ToUniversalTime();
        DateTimeOffset anchor = DateTimeOffset.UnixEpoch;
        for (DateTime date = cursor.UtcDateTime.Date; date.Year <= 9999; date = date.AddDays(1))
        {
            if (!CalendarPeriodMatches(value, anchor.UtcDateTime, date) || !CalendarSelectorMatches(value.Selector, date)) continue;
            long candidate = new DateTimeOffset(date.Year, date.Month, date.Day, value.LocalTime.Hour, value.LocalTime.Minute,
                value.LocalTime.Second, value.LocalTime.Millisecond, TimeSpan.Zero).ToUnixTimeMilliseconds();
            if (candidate > after) return candidate;
            if (date == DateTime.MaxValue.Date) break;
        }
        return null;
    }

    private static bool CalendarPeriodMatches(BaseCalendarSchedule value, DateTime anchor, DateTime date)
    {
        long units = value.Frequency switch
        {
            BaseCalendarFrequency.Secondly or BaseCalendarFrequency.Minutely or BaseCalendarFrequency.Hourly or BaseCalendarFrequency.Daily => (date - anchor.Date).Days,
            BaseCalendarFrequency.Weekly => (date - anchor.Date).Days / 7,
            BaseCalendarFrequency.Monthly => (date.Year - anchor.Year) * 12L + date.Month - anchor.Month,
            BaseCalendarFrequency.Yearly => date.Year - anchor.Year,
            _ => 0,
        };
        return units % value.Interval == 0;
    }

    private static bool CalendarSelectorMatches(BaseCalendarSelector selector, DateTime date) => selector switch
    {
        BaseEveryCalendarPeriod => true,
        BaseWeekdayCalendarSelector weekdays => weekdays.Weekdays.Contains((int)date.DayOfWeek),
        BaseMonthDayCalendarSelector day => date.Day == day.Day,
        BaseYearDayCalendarSelector day => date.Month == day.Month && date.Day == day.Day,
        BaseOrdinalWeekdayCalendarSelector ordinal => date.DayOfWeek == (DayOfWeek)ordinal.Weekday &&
            (ordinal.Ordinal == -1 ? date.AddDays(7).Month != date.Month : (date.Day - 1) / 7 + 1 == ordinal.Ordinal),
        _ => false,
    };

    private static byte[] Checksum(BaseScheduleDefinition value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, "base.activation.schedule.definition.v2\0"); Add(hash, value.Id); Add(hash, value.Version); Add(hash, value.OwningModuleId);
        Add(hash, value.ManageGrantId); Add(hash, value.MaterializeGrantId);
        Add(hash, value.Activation.Id); Add(hash, value.Activation.Version); Add(hash, value.Activation.Checksum.AsSpan());
        Add(hash, value.InputChecksum.AsSpan()); Add(hash, ExpressionText(value.Expression));
        Add(hash, (int)value.GapPolicy); Add(hash, (int)value.TimeOverlapPolicy); Add(hash, (int)value.MisfirePolicy);
        Add(hash, (int)value.ActivationOverlapPolicy); Add(hash, (int)value.OverlapKeyKind); Add(hash, value.ConcurrencyKey.AsSpan());
        Add(hash, value.Priority); Add(hash, value.MaximumSplayMilliseconds); return hash.GetHashAndReset();
    }

    private static string ExpressionText(BaseScheduleExpression value) => value switch
    {
        BaseOnceSchedule once => $"once:{once.At}",
        BaseIntervalSchedule interval => $"interval:{interval.Anchor}:{interval.EveryMilliseconds}",
        BaseCronSchedule cron => $"cron:{cron.Expression}:{cron.TimeZoneId}",
        BaseCalendarSchedule calendar => $"calendar:{(int)calendar.Frequency}:{calendar.Interval}:{calendar.LocalTime.Hour}:{calendar.LocalTime.Minute}:{calendar.LocalTime.Second}:{calendar.LocalTime.Millisecond}:{SelectorText(calendar.Selector)}:{calendar.TimeZoneId}",
        _ => throw new InvalidOperationException("base.activation.scheduleInvalid"),
    };

    private static string SelectorText(BaseCalendarSelector value) => value switch
    {
        BaseEveryCalendarPeriod => "every",
        BaseWeekdayCalendarSelector days => "weekdays:" + string.Join(',', days.Weekdays),
        BaseMonthDayCalendarSelector day => $"month-day:{day.Day}",
        BaseYearDayCalendarSelector day => $"year-day:{day.Month}:{day.Day}",
        BaseOrdinalWeekdayCalendarSelector day => $"ordinal:{day.Ordinal}:{day.Weekday}",
        _ => throw new InvalidOperationException("base.activation.scheduleInvalid"),
    };

    private static void Add(IncrementalHash hash, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length)); hash.AppendData(length); hash.AppendData(bytes); }
    private static void Add(IncrementalHash hash, int value) => Add(hash, (long)value);
    private static void Add(IncrementalHash hash, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes); }
    private static void Add(IncrementalHash hash, ReadOnlySpan<byte> value) { Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)value.Length)); hash.AppendData(length); hash.AppendData(value); }

    private sealed record Cron(int[] Second, int[] Minute, int[] Hour, int[] DayOfMonth, int[] Month, int[] DayOfWeek,
        bool DayOfMonthWildcard, bool DayOfWeekWildcard, string Canonical)
    {
        internal static Cron Parse(string expression)
        {
            string[] fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length != 6) throw new InvalidOperationException("base.activation.scheduleInvalid");
            int[] second = Field(fields[0], 0, 59, out _); int[] minute = Field(fields[1], 0, 59, out _);
            int[] hour = Field(fields[2], 0, 23, out _); int[] dom = Field(fields[3], 1, 31, out bool domAny);
            int[] month = Field(fields[4], 1, 12, out _); int[] dow = Field(fields[5], 0, 6, out bool dowAny);
            string canonical = string.Join(' ', second.Join(), minute.Join(), hour.Join(), domAny ? "*" : dom.Join(), month.Join(), dowAny ? "*" : dow.Join());
            return new Cron(second, minute, hour, dom, month, dow, domAny, dowAny, canonical);
        }

        private static int[] Field(string source, int minimum, int maximum, out bool wildcard)
        {
            wildcard = source == "*";
            var values = new SortedSet<int>();
            foreach (string part in source.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string rangeText = part; int step = 1;
                int slash = part.IndexOf('/');
                if (slash >= 0) { rangeText = part[..slash]; if (!int.TryParse(part[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step <= 0) throw new InvalidOperationException("base.activation.scheduleInvalid"); }
                int start; int end = 0;
                if (rangeText == "*") { start = minimum; end = maximum; }
                else
                {
                    string[] range = rangeText.Split('-');
                    if (range.Length > 2 || !int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out start) ||
                        (range.Length == 2 && !int.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out end)))
                        throw new InvalidOperationException("base.activation.scheduleInvalid");
                    if (range.Length == 1) end = start;
                }
                if (start < minimum || end > maximum || start > end) throw new InvalidOperationException("base.activation.scheduleInvalid");
                for (int value = start; value <= end; value = checked(value + step)) { values.Add(value); if (value > end - step) break; }
            }
            if (values.Count == 0) throw new InvalidOperationException("base.activation.scheduleInvalid");
            return values.ToArray();
        }
    }

    private static string Join(this int[] values) => string.Join(',', values);
}
