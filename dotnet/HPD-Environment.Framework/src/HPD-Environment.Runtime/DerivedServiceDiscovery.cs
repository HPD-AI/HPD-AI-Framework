#nullable enable

namespace HPD.Environment.Runtime;

using HPD.Environment.Contracts;

internal static class DerivedServiceDiscovery
{
    internal const int MaxRecords = 128;
    internal static readonly TimeSpan DefaultTtl =
        TimeSpan.FromSeconds(30);

    public static IReadOnlyList<DiscoveryRecord> Build(
        ServiceDiscoverySpec spec,
        IEnumerable<DiscoveryRecord> membershipRecords)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(membershipRecords);
        TimeSpan ttl = BoundTtl(spec.DefaultTtl);
        IEnumerable<DiscoveryRecord> explicitRecords =
            spec.Records.Select(record => new DiscoveryRecord(
                record.Name,
                record.Kind,
                record.Target,
                BoundTtl(record.Ttl ?? ttl)));
        return explicitRecords
            .Concat(membershipRecords.Select(record =>
                record with { Ttl = BoundTtl(record.Ttl) }))
            .GroupBy(
                static record => Key(record),
                StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(
                static record => record.Name.Value,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(static record => record.Kind)
            .Take(MaxRecords)
            .ToArray();
    }

    public static IReadOnlyList<DiscoveryRecord> Resolve(
        IReadOnlyList<DiscoveryRecord> records,
        ServiceDiscoveryQuery query) =>
        records
            .Where(record =>
                string.Equals(
                    record.Name.Value,
                    query.Name.Value,
                    StringComparison.OrdinalIgnoreCase) &&
                (query.Kind is null || query.Kind == record.Kind))
            .ToArray();

    public static TimeSpan BoundTtl(TimeSpan? ttl)
    {
        TimeSpan value = ttl ?? DefaultTtl;
        if (value < TimeSpan.FromSeconds(1))
            return TimeSpan.FromSeconds(1);
        if (value > TimeSpan.FromHours(1))
            return TimeSpan.FromHours(1);
        return value;
    }

    private static string Key(DiscoveryRecord record) =>
        string.Join(
            "\u001f",
            record.Name.Value.ToLowerInvariant(),
            ((int)record.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            record.Target.ToString());
}
