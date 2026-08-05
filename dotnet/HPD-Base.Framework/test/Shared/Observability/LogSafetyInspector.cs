using System.Collections;
using System.Globalization;
using System.Text;

namespace HPD.Base.Tests.Observability;

internal static class LogSafetyInspector
{
    private static readonly HashSet<Type> AllowedScalarTypes =
    [
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(char),
        typeof(string)
    ];

    public static void AssertSafe(
        IEnumerable<CapturedLogRecord> records,
        params string[] forbiddenMarkers)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(forbiddenMarkers);

        foreach (var record in records)
        {
            AssertNoDuplicateStateKeys(record);
            AssertStateIsScalar(record);
            AssertNoForbiddenMarkers(record, forbiddenMarkers);
        }
    }

    public static void AssertNoExceptions(IEnumerable<CapturedLogRecord> records)
    {
        foreach (var record in records)
        {
            if (record.Exception is not null)
            {
                throw new InvalidOperationException(
                    $"Log event {record.EventId.Id} supplied exception type '{record.Exception.GetType().FullName}'.");
            }
        }
    }

    public static void AssertNoScopes(IEnumerable<CapturedLogRecord> records)
    {
        foreach (var record in records)
        {
            if (record.Scopes.Count != 0)
            {
                throw new InvalidOperationException(
                    $"Log event {record.EventId.Id} captured {record.Scopes.Count} scope(s).");
            }
        }
    }

    private static void AssertNoDuplicateStateKeys(CapturedLogRecord record)
    {
        var duplicate = record.State
            .GroupBy(property => property.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Log event {record.EventId.Id} contains duplicate state key '{duplicate.Key}'.");
        }
    }

    private static void AssertStateIsScalar(CapturedLogRecord record)
    {
        foreach (var property in record.State)
        {
            if (property.Key == "{OriginalFormat}" || property.Value is null)
            {
                continue;
            }

            var type = property.Value.GetType();
            if (!AllowedScalarTypes.Contains(type) && !type.IsEnum)
            {
                throw new InvalidOperationException(
                    $"Log event {record.EventId.Id} property '{property.Key}' has disallowed complex type '{type.FullName}'.");
            }
        }
    }

    private static void AssertNoForbiddenMarkers(
        CapturedLogRecord record,
        IReadOnlyList<string> forbiddenMarkers)
    {
        var surfaces = EnumerateSurfaces(record).ToArray();
        foreach (var marker in forbiddenMarkers.Where(marker => !string.IsNullOrEmpty(marker)))
        {
            foreach (var variant in MarkerVariants(marker))
            {
                var match = surfaces.FirstOrDefault(surface =>
                    surface.Value.Contains(variant, StringComparison.OrdinalIgnoreCase));
                if (match != default)
                {
                    throw new InvalidOperationException(
                        $"Forbidden marker was found in log event {record.EventId.Id} {match.Channel}.");
                }
            }
        }
    }

    private static IEnumerable<(string Channel, string Value)> EnumerateSurfaces(CapturedLogRecord record)
    {
        yield return ("category", record.Category);
        yield return ("event name", record.EventId.Name ?? string.Empty);
        yield return ("template", record.OriginalFormat ?? string.Empty);
        yield return ("rendered message", record.RenderedMessage);

        foreach (var property in record.State)
        {
            yield return ($"state key '{property.Key}'", property.Key);
            yield return (
                $"state value '{property.Key}'",
                Convert.ToString(property.Value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        foreach (var scope in record.Scopes)
        {
            yield return ("scope", Convert.ToString(scope, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        if (record.Exception is not null)
        {
            yield return ("exception type", record.Exception.GetType().FullName ?? record.Exception.GetType().Name);
            yield return ("exception message", record.Exception.Message);
            yield return ("exception rendering", record.Exception.ToString());
        }
    }

    private static IEnumerable<string> MarkerVariants(string marker)
    {
        yield return marker;
        yield return marker.ToLowerInvariant();
        yield return marker.ToUpperInvariant();
        yield return Uri.EscapeDataString(marker);
        yield return Convert.ToBase64String(Encoding.UTF8.GetBytes(marker));
        yield return System.Text.Json.JsonSerializer.Serialize(marker).Trim('"');
    }
}
