using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Receipts one exact source artifact used to compile time-zone authority.</summary>
public sealed record BaseTimeZoneSourceReceipt
{
    /// <summary>Gets the UTF-8 ordinal artifact name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the exact artifact byte length.</summary>
    public required long ByteLength { get; init; }
    /// <summary>Gets the SHA-256 artifact checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Defines one compiled UTC transition.</summary>
public sealed record BaseTimeZoneTransition
{
    /// <summary>Gets the inclusive UTC Unix-second boundary.</summary>
    public required long UtcSecond { get; init; }
    /// <summary>Gets offset seconds after the boundary.</summary>
    public required int OffsetSeconds { get; init; }
    /// <summary>Gets whether the resulting period is daylight-saving time.</summary>
    public required bool DaylightSaving { get; init; }
    /// <summary>Gets the normalized abbreviation.</summary>
    public required string Abbreviation { get; init; }
}

/// <summary>Defines one canonical compiled time zone.</summary>
public sealed record BaseTimeZoneDefinition
{
    /// <summary>Gets canonical IANA zone identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets offset seconds before the first transition.</summary>
    public required int InitialOffsetSeconds { get; init; }
    /// <summary>Gets strictly ordered explicit transitions through the supported horizon.</summary>
    public required ImmutableArray<BaseTimeZoneTransition> Transitions { get; init; }
}

/// <summary>Maps one IANA link identity to a canonical zone.</summary>
public sealed record BaseTimeZoneAlias(string Id, string CanonicalId);

/// <summary>Contains one immutable graph-installed compiled tzdb authority.</summary>
public sealed record BaseTimeZoneAuthority
{
    /// <summary>Gets positive authority generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets exact IANA release identity.</summary>
    public required string ReleaseId { get; init; }
    /// <summary>Gets exact source receipts.</summary>
    public required ImmutableArray<BaseTimeZoneSourceReceipt> Sources { get; init; }
    /// <summary>Gets canonical zones.</summary>
    public required ImmutableArray<BaseTimeZoneDefinition> Zones { get; init; }
    /// <summary>Gets canonical aliases.</summary>
    public required ImmutableArray<BaseTimeZoneAlias> Aliases { get; init; }
    /// <summary>Gets the inclusive minimum supported UTC Unix second.</summary>
    public required long MinimumUtcSecond { get; init; }
    /// <summary>Gets the inclusive maximum supported UTC Unix second.</summary>
    public required long MaximumUtcSecond { get; init; }
    /// <summary>Gets exact canonical compiled bytes.</summary>
    public required ImmutableArray<byte> CompiledBytes { get; init; }
    /// <summary>Gets SHA-256 over canonical compiled bytes.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Validates and canonically compiles graph-owned time-zone authority.</summary>
public static class BaseTimeZoneAuthorityBuilder
{
    private static readonly string[] RequiredSources = ["backward", "tzdata.zi", "zone.tab", "zone1970.tab"];

    /// <summary>Returns one deeply owned canonical authority.</summary>
    public static BaseTimeZoneAuthority Create(BaseTimeZoneAuthority source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Generation <= 0 || string.IsNullOrWhiteSpace(source.ReleaseId) ||
            source.MinimumUtcSecond >= source.MaximumUtcSecond || source.Sources.Length is < 5 or > 16 ||
            source.Zones.IsDefaultOrEmpty || source.Zones.Length > 4096 || source.Aliases.Length > 8192)
            throw new InvalidOperationException("base.activation.timeZoneInvalid");
        BaseTimeZoneSourceReceipt[] receipts = source.Sources.OrderBy(static value => value.Name, StringComparer.Ordinal).ToArray();
        if (receipts.Select(static value => value.Name).Distinct(StringComparer.Ordinal).Count() != receipts.Length ||
            RequiredSources.Any(required => receipts.All(value => !string.Equals(value.Name, required, StringComparison.Ordinal))) ||
            receipts.Any(static value => string.IsNullOrWhiteSpace(value.Name) || value.ByteLength <= 0 || value.Checksum.Length != 32))
            throw new InvalidOperationException("base.activation.timeZoneInvalid");
        BaseTimeZoneDefinition[] zones = source.Zones.OrderBy(static value => value.Id, StringComparer.Ordinal).Select(CloneZone).ToArray();
        if (zones.Select(static value => value.Id).Distinct(StringComparer.Ordinal).Count() != zones.Length) throw new InvalidOperationException("base.activation.timeZoneInvalid");
        BaseTimeZoneAlias[] aliases = source.Aliases.OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(static value => new BaseTimeZoneAlias(new string(value.Id.AsSpan()), new string(value.CanonicalId.AsSpan()))).ToArray();
        if (aliases.Select(static value => value.Id).Distinct(StringComparer.Ordinal).Count() != aliases.Length ||
            aliases.Any(alias => zones.All(zone => !string.Equals(zone.Id, alias.CanonicalId, StringComparison.Ordinal))))
            throw new InvalidOperationException("base.activation.timeZoneInvalid");
        var normalized = source with
        {
            ReleaseId = new string(source.ReleaseId.AsSpan()),
            Sources = receipts.Select(static value => value with { Name = new string(value.Name.AsSpan()), Checksum = value.Checksum.ToArray().ToImmutableArray() }).ToImmutableArray(),
            Zones = zones.ToImmutableArray(), Aliases = aliases.ToImmutableArray(), CompiledBytes = [], Checksum = [],
        };
        byte[] bytes = Encode(normalized);
        if (bytes.LongLength > 64L * 1024 * 1024) throw new InvalidOperationException("base.activation.timeZoneInvalid");
        return normalized with { CompiledBytes = bytes.ToImmutableArray(), Checksum = SHA256.HashData(bytes).ToImmutableArray() };
    }

    private static BaseTimeZoneDefinition CloneZone(BaseTimeZoneDefinition value)
    {
        if (string.IsNullOrWhiteSpace(value.Id) || value.Id.Length > 255 ||
            !string.Equals(value.Id, value.Id.Normalize(NormalizationForm.FormC), StringComparison.Ordinal))
            throw new InvalidOperationException("base.activation.timeZoneInvalid");
        if (value.InitialOffsetSeconds is < -93_600 or > 93_600 || value.Transitions.Length > 100_000) throw new InvalidOperationException("base.activation.timeZoneInvalid");
        BaseTimeZoneTransition[] transitions = value.Transitions.Select(static transition => transition with { Abbreviation = new string(transition.Abbreviation.AsSpan()) }).ToArray();
        if (!transitions.Select(static transition => transition.UtcSecond).SequenceEqual(transitions.Select(static transition => transition.UtcSecond).Order()) ||
            transitions.Select(static transition => transition.UtcSecond).Distinct().Count() != transitions.Length ||
            transitions.Any(static transition => transition.OffsetSeconds is < -93_600 or > 93_600 || transition.Abbreviation.Length > 32))
            throw new InvalidOperationException("base.activation.timeZoneInvalid");
        return value with { Id = new string(value.Id.AsSpan()), Transitions = transitions.ToImmutableArray() };
    }

    private static byte[] Encode(BaseTimeZoneAuthority value)
    {
        using var stream = new MemoryStream();
        stream.Write("base.activation.tzdb.v1\0"u8); Write(stream, value.ReleaseId); Write(stream, value.Generation);
        Write(stream, value.MinimumUtcSecond); Write(stream, value.MaximumUtcSecond); Write(stream, value.Sources.Length);
        foreach (BaseTimeZoneSourceReceipt receipt in value.Sources) { Write(stream, receipt.Name); Write(stream, receipt.ByteLength); Write(stream, receipt.Checksum.AsSpan()); }
        Write(stream, value.Zones.Length);
        foreach (BaseTimeZoneDefinition zone in value.Zones)
        {
            Write(stream, zone.Id); Write(stream, zone.InitialOffsetSeconds); Write(stream, zone.Transitions.Length);
            foreach (BaseTimeZoneTransition transition in zone.Transitions)
            { Write(stream, transition.UtcSecond); Write(stream, transition.OffsetSeconds); stream.WriteByte(transition.DaylightSaving ? (byte)1 : (byte)0); Write(stream, transition.Abbreviation); }
        }
        Write(stream, value.Aliases.Length); foreach (BaseTimeZoneAlias alias in value.Aliases) { Write(stream, alias.Id); Write(stream, alias.CanonicalId); }
        return stream.ToArray();
    }
    private static void Write(Stream stream, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); Write(stream, bytes.Length); stream.Write(bytes); }
    private static void Write(Stream stream, ReadOnlySpan<byte> value) { Write(stream, value.Length); stream.Write(value); }
    private static void Write(Stream stream, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); stream.Write(bytes); }
    private static void Write(Stream stream, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
}

/// <summary>Resolves installed named-zone transition authority for schedule evaluation.</summary>
public sealed class BaseTimeZoneRegistry
{
    private readonly BaseTimeZoneAuthority? _authority;
    private readonly Dictionary<string, BaseTimeZoneDefinition> _zones = new(StringComparer.Ordinal);

    internal BaseTimeZoneRegistry(BaseTimeZoneAuthority? authority)
    {
        if (authority is null) return;
        _authority = BaseTimeZoneAuthorityBuilder.Create(authority);
        foreach (BaseTimeZoneDefinition zone in _authority.Zones) _zones.Add(zone.Id, zone);
        foreach (BaseTimeZoneAlias alias in _authority.Aliases) _zones.Add(alias.Id, _zones[alias.CanonicalId]);
    }

    internal bool Contains(string id) => string.Equals(id, "UTC", StringComparison.Ordinal) || _zones.ContainsKey(id);

    internal DateTime LocalAt(string id, long utcMilliseconds)
    {
        if (string.Equals(id, "UTC", StringComparison.Ordinal)) return DateTimeOffset.FromUnixTimeMilliseconds(utcMilliseconds).UtcDateTime;
        BaseTimeZoneDefinition zone = Resolve(id); long seconds = Math.DivRem(utcMilliseconds, 1000, out long remainder);
        int offset = OffsetAt(zone, seconds); return DateTimeOffset.FromUnixTimeMilliseconds(checked(seconds * 1000 + remainder)).UtcDateTime.AddSeconds(offset);
    }

    internal ImmutableArray<long> ResolveLocal(string id, DateTime local, BaseTimeGapPolicy gap, BaseTimeOverlapPolicy overlap)
    {
        if (string.Equals(id, "UTC", StringComparison.Ordinal)) return [new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc)).ToUnixTimeMilliseconds()];
        BaseTimeZoneDefinition zone = Resolve(id); long localMs = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        int[] offsets = [zone.InitialOffsetSeconds, .. zone.Transitions.Select(static value => value.OffsetSeconds).Distinct()];
        long[] matches = offsets.Distinct().Select(offset => checked(localMs - offset * 1000L))
            .Where(utc => utc >= checked(_authority!.MinimumUtcSecond * 1000) && utc <= checked(_authority.MaximumUtcSecond * 1000) && OffsetAt(zone, Math.DivRem(utc, 1000, out _)) * 1000L == localMs - utc)
            .Distinct().Order().ToArray();
        if (matches.Length == 1) return [matches[0]];
        if (matches.Length > 1) return overlap switch
        { BaseTimeOverlapPolicy.EarlierOffset => [matches[0]], BaseTimeOverlapPolicy.LaterOffset => [matches[^1]], _ => matches.ToImmutableArray() };
        int previous = zone.InitialOffsetSeconds;
        foreach (BaseTimeZoneTransition transition in zone.Transitions)
        {
            if (transition.OffsetSeconds > previous)
            {
                long before = checked(transition.UtcSecond * 1000 + previous * 1000L);
                long after = checked(transition.UtcSecond * 1000 + transition.OffsetSeconds * 1000L);
                if (localMs >= before && localMs < after)
                    return gap switch { BaseTimeGapPolicy.Skip => [], BaseTimeGapPolicy.NextValid => [checked(transition.UtcSecond * 1000)], _ => [checked(transition.UtcSecond * 1000 - 1)] };
            }
            previous = transition.OffsetSeconds;
        }
        return [];
    }

    private BaseTimeZoneDefinition Resolve(string id) => _zones.TryGetValue(id, out BaseTimeZoneDefinition? zone)
        ? zone : throw new InvalidOperationException("base.activation.timeZoneUnavailable");
    private static int OffsetAt(BaseTimeZoneDefinition zone, long utcSecond)
    {
        int offset = zone.InitialOffsetSeconds;
        foreach (BaseTimeZoneTransition transition in zone.Transitions) { if (transition.UtcSecond > utcSecond) break; offset = transition.OffsetSeconds; }
        return offset;
    }
}
