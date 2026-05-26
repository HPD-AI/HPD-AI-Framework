using System.Globalization;
using System.Reflection;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Built-in clearing-calendar catalog for common replay venues.
/// Caller-supplied holidays remain supported for exchange special closures and broker-specific overrides.
/// </summary>
public static class ClearingCalendarCatalog
{
    private const string UsMarketDatasetId = "us-market";

    private static readonly HashSet<string> CryptoVenues = new(StringComparer.OrdinalIgnoreCase)
    {
        "BINANCE",
        "COINBASE",
        "KRAKEN"
    };

    private static readonly HashSet<string> USEquityVenues = new(StringComparer.OrdinalIgnoreCase)
    {
        "NYSE",
        "NASDAQ"
    };

    private static readonly HashSet<string> USFuturesVenues = new(StringComparer.OrdinalIgnoreCase)
    {
        "CME"
    };

    private static readonly Dictionary<string, string> VenueDatasets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NYSE"] = UsMarketDatasetId,
        ["NASDAQ"] = UsMarketDatasetId,
        ["CME"] = UsMarketDatasetId
    };

    public static IReadOnlyCollection<string> BundledDatasetIds => GetBundledDatasetIds();

    public static ClearingCalendar ForVenue(
        Venue venue,
        int year,
        IEnumerable<DateOnly>? additionalHolidays = null)
        => ForVenue(venue, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), additionalHolidays);

    public static ClearingCalendar ForVenue(
        Venue venue,
        DateOnly start,
        DateOnly end,
        IEnumerable<DateOnly>? additionalHolidays = null)
    {
        if (end < start)
            throw new ArgumentException("Clearing calendar end date cannot be before start date.", nameof(end));

        var venueName = venue.Name;
        if (CryptoVenues.Contains(venueName))
            return ClearingCalendar.Crypto(additionalHolidays);

        var holidays = new HashSet<DateOnly>();
        if (USEquityVenues.Contains(venueName) || USFuturesVenues.Contains(venueName))
            AddUSMarketHolidays(holidays, start, end);

        if (VenueDatasets.TryGetValue(venueName, out var datasetId))
            AddBundledHolidays(holidays, datasetId, start, end);

        AddAdditionalHolidays(holidays, additionalHolidays);

        if (USEquityVenues.Contains(venueName))
            return ClearingCalendar.USEquities(holidays);

        if (USFuturesVenues.Contains(venueName))
            return ClearingCalendar.USFutures(holidays);

        return ClearingCalendar.Weekdays(holidays);
    }

    public static IReadOnlySet<DateOnly> USMarketHolidays(int year)
    {
        var holidays = new HashSet<DateOnly>();
        AddUSMarketHolidays(holidays, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        AddBundledHolidays(holidays, UsMarketDatasetId, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        return holidays;
    }

    public static IReadOnlySet<DateOnly> BundledHolidayDataset(
        string datasetId,
        DateOnly? start = null,
        DateOnly? end = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        return ParseHolidayFeed(ReadBundledHolidayDataset(datasetId), start, end);
    }

    public static ClearingCalendar FromBundledHolidayDataset(
        string name,
        IEnumerable<DayOfWeek> businessDays,
        string datasetId,
        DateOnly? start = null,
        DateOnly? end = null,
        IEnumerable<DateOnly>? additionalHolidays = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        var holidays = new HashSet<DateOnly>(BundledHolidayDataset(datasetId, start, end));
        AddAdditionalHolidays(holidays, additionalHolidays);
        return new ClearingCalendar(name, businessDays, holidays);
    }

    public static ClearingCalendar FromHolidayFeed(
        string name,
        IEnumerable<DayOfWeek> businessDays,
        string feedText,
        DateOnly? start = null,
        DateOnly? end = null,
        IEnumerable<DateOnly>? additionalHolidays = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedText);
        var holidays = new HashSet<DateOnly>(ParseHolidayFeed(feedText, start, end));
        AddAdditionalHolidays(holidays, additionalHolidays);
        return new ClearingCalendar(name, businessDays, holidays);
    }

    public static ClearingCalendar FromHolidayFeedFile(
        string name,
        IEnumerable<DayOfWeek> businessDays,
        string path,
        DateOnly? start = null,
        DateOnly? end = null,
        IEnumerable<DateOnly>? additionalHolidays = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromHolidayFeed(
            name,
            businessDays,
            File.ReadAllText(path),
            start,
            end,
            additionalHolidays);
    }

    public static ClearingCalendar ForVenueWithHolidayFeed(
        Venue venue,
        string feedText,
        DateOnly? start = null,
        DateOnly? end = null,
        IEnumerable<DateOnly>? additionalHolidays = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedText);
        var businessDays = CryptoVenues.Contains(venue.Name)
            ? ClearingCalendar.Crypto().BusinessDays
            : ClearingCalendar.ForVenue(venue).BusinessDays;
        var holidays = new HashSet<DateOnly>(ParseHolidayFeed(feedText, start, end));
        AddAdditionalHolidays(holidays, additionalHolidays);
        return new ClearingCalendar($"{venue.Name} Feed", businessDays, holidays);
    }

    public static ClearingCalendar ForVenueWithHolidayFeedFile(
        Venue venue,
        string path,
        DateOnly? start = null,
        DateOnly? end = null,
        IEnumerable<DateOnly>? additionalHolidays = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ForVenueWithHolidayFeed(
            venue,
            File.ReadAllText(path),
            start,
            end,
            additionalHolidays);
    }

    public static IReadOnlySet<DateOnly> ParseHolidayFeed(
        string feedText,
        DateOnly? start = null,
        DateOnly? end = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedText);
        if (start.HasValue && end.HasValue && end.Value < start.Value)
            throw new ArgumentException("Holiday feed end date cannot be before start date.", nameof(end));

        var holidays = new HashSet<DateOnly>();
        using var reader = new StringReader(feedText);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var candidate = ExtractHolidayDateCandidate(line);
            if (candidate is null)
                continue;

            if (!TryParseHolidayDate(candidate, out var holiday))
            {
                if (lineNumber == 1 && LooksLikeHeader(candidate))
                    continue;

                throw new FormatException($"Holiday feed line {lineNumber} does not contain a supported date: '{line}'.");
            }

            if (start.HasValue && holiday < start.Value)
                continue;

            if (end.HasValue && holiday > end.Value)
                continue;

            holidays.Add(holiday);
        }

        return holidays;
    }

    private static void AddUSMarketHolidays(HashSet<DateOnly> holidays, DateOnly start, DateOnly end)
    {
        for (var year = Math.Max(1, start.Year - 1); year <= end.Year + 1; year++)
        {
            AddIfInRange(holidays, ObservedFixedHoliday(year, 1, 1), start, end);
            AddIfInRange(holidays, NthWeekdayOfMonth(year, 1, DayOfWeek.Monday, 3), start, end);
            AddIfInRange(holidays, NthWeekdayOfMonth(year, 2, DayOfWeek.Monday, 3), start, end);
            AddIfInRange(holidays, EasterSunday(year).AddDays(-2), start, end);
            AddIfInRange(holidays, LastWeekdayOfMonth(year, 5, DayOfWeek.Monday), start, end);
            AddIfInRange(holidays, ObservedFixedHoliday(year, 6, 19), start, end);
            AddIfInRange(holidays, ObservedFixedHoliday(year, 7, 4), start, end);
            AddIfInRange(holidays, NthWeekdayOfMonth(year, 9, DayOfWeek.Monday, 1), start, end);
            AddIfInRange(holidays, NthWeekdayOfMonth(year, 11, DayOfWeek.Thursday, 4), start, end);
            AddIfInRange(holidays, ObservedFixedHoliday(year, 12, 25), start, end);
        }
    }

    private static void AddBundledHolidays(
        HashSet<DateOnly> holidays,
        string datasetId,
        DateOnly start,
        DateOnly end)
    {
        foreach (var holiday in BundledHolidayDataset(datasetId, start, end))
            holidays.Add(holiday);
    }

    private static void AddAdditionalHolidays(HashSet<DateOnly> holidays, IEnumerable<DateOnly>? additionalHolidays)
    {
        if (additionalHolidays is null)
            return;

        foreach (var holiday in additionalHolidays)
            holidays.Add(holiday);
    }

    private static void AddIfInRange(HashSet<DateOnly> holidays, DateOnly holiday, DateOnly start, DateOnly end)
    {
        if (holiday >= start && holiday <= end)
            holidays.Add(holiday);
    }

    private static string? ExtractHolidayDateCandidate(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            return null;

        var remaining = trimmed.AsSpan();
        while (true)
        {
            var comma = remaining.IndexOf(',');
            ReadOnlySpan<char> rawField;
            if (comma < 0)
            {
                rawField = remaining;
                remaining = [];
            }
            else
            {
                rawField = remaining[..comma];
                remaining = remaining[(comma + 1)..];
            }

            var field = rawField.Trim().Trim('"');
            if (field.Length > 0)
                return field.ToString();

            if (remaining.Length == 0)
                break;
        }

        return null;
    }

    private static bool TryParseHolidayDate(string value, out DateOnly date)
    {
        string[] formats =
        [
            "yyyy-MM-dd",
            "yyyyMMdd",
            "MM/dd/yyyy",
            "M/d/yyyy"
        ];

        return DateOnly.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static bool LooksLikeHeader(string value)
        => value.Equals("date", StringComparison.OrdinalIgnoreCase)
            || value.Equals("holiday_date", StringComparison.OrdinalIgnoreCase)
            || value.Equals("calendar_date", StringComparison.OrdinalIgnoreCase);

    private static string ReadBundledHolidayDataset(string datasetId)
    {
        var resourceName = $"Rhodium.Simulation.Data.ClearingCalendars.{datasetId}.csv";
        var assembly = typeof(ClearingCalendarCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new ArgumentException($"Bundled clearing-calendar dataset '{datasetId}' was not found.", nameof(datasetId));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string[] GetBundledDatasetIds()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in VenueDatasets.Values)
            seen.Add(id);

        var ids = new string[seen.Count];
        var index = 0;
        foreach (var id in seen)
            ids[index++] = id;

        Array.Sort(ids, StringComparer.OrdinalIgnoreCase);
        return ids;
    }

    private static DateOnly ObservedFixedHoliday(int year, int month, int day)
    {
        var holiday = new DateOnly(year, month, day);
        return holiday.DayOfWeek switch
        {
            DayOfWeek.Saturday => holiday.AddDays(-1),
            DayOfWeek.Sunday => holiday.AddDays(1),
            _ => holiday
        };
    }

    private static DateOnly NthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int occurrence)
    {
        var date = new DateOnly(year, month, 1);
        while (date.DayOfWeek != dayOfWeek)
            date = date.AddDays(1);

        return date.AddDays(7 * (occurrence - 1));
    }

    private static DateOnly LastWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek)
    {
        var date = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        while (date.DayOfWeek != dayOfWeek)
            date = date.AddDays(-1);

        return date;
    }

    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
