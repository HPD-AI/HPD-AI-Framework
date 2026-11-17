using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Identifies one closed Studio route parameter codec.</summary>
public enum StudioRouteCodec : byte
{
    /// <summary>A bounded NFC identity.</summary>
    Identifier = 1,
    /// <summary>A positive 64-bit integer.</summary>
    PositiveLong,
    /// <summary>A nonnegative 64-bit integer.</summary>
    NonnegativeLong,
    /// <summary>A lowercase hexadecimal SHA-256 value.</summary>
    Sha256,
    /// <summary>A typed graph-owned Studio resource identity.</summary>
    StudioResourceIdentity,
    /// <summary>An opaque protected cursor.</summary>
    Cursor,
    /// <summary>A registered enum value.</summary>
    RegisteredEnum,
    /// <summary>A registered selected-tab identity.</summary>
    SelectedTab,
}

/// <summary>Identifies whether a route segment is literal or typed.</summary>
public enum StudioRouteSegmentKind : byte { Literal = 1, Parameter }

/// <summary>Defines one immutable route segment.</summary>
public sealed class StudioRouteSegment
{
    private StudioRouteSegment(StudioRouteSegmentKind kind, string value, StudioRouteCodec? codec, StudioSha256 checksum)
    { Kind = kind; Value = value; Codec = codec; Checksum = checksum; }
    /// <summary>Gets the segment kind.</summary>
    public StudioRouteSegmentKind Kind { get; }
    /// <summary>Gets the literal value or parameter name.</summary>
    public string Value { get; }
    /// <summary>Gets the parameter codec.</summary>
    public StudioRouteCodec? Codec { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }

    /// <summary>Creates a literal segment.</summary>
    public static StudioRouteSegment Literal(string value) => Create(StudioRouteSegmentKind.Literal, value, null);
    /// <summary>Creates a typed parameter segment.</summary>
    public static StudioRouteSegment Parameter(string name, StudioRouteCodec codec) => Create(StudioRouteSegmentKind.Parameter, name, codec);

    private static StudioRouteSegment Create(StudioRouteSegmentKind kind, string value, StudioRouteCodec? codec)
    {
        StudioContractValidation.Enum(kind); StudioContractValidation.Id(value);
        if ((kind == StudioRouteSegmentKind.Parameter) != codec.HasValue) throw new ArgumentException("Studio route codec correspondence is invalid.");
        if (codec.HasValue) StudioContractValidation.Enum(codec.Value);
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.route-segment.v1", writer =>
        { writer.Enum(kind); writer.String(value); writer.Boolean(codec.HasValue); if (codec.HasValue) writer.Enum(codec.Value); });
        return new(kind, value, codec, checksum);
    }
}

/// <summary>Defines one registered route query member.</summary>
public sealed class StudioQueryParameter
{
    private StudioQueryParameter(string name, StudioRouteCodec codec, bool required, ImmutableArray<string> values, StudioSha256 checksum)
    { Name = name; Codec = codec; Required = required; RegisteredValues = values; Checksum = checksum; }
    /// <summary>Gets the canonical query name.</summary>
    public string Name { get; }
    /// <summary>Gets the value codec.</summary>
    public StudioRouteCodec Codec { get; }
    /// <summary>Gets whether the member is required.</summary>
    public bool Required { get; }
    /// <summary>Gets registered values for enum and tab codecs.</summary>
    public ImmutableArray<string> RegisteredValues { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }

    /// <summary>Creates a route query member.</summary>
    public static StudioQueryParameter Create(string name, StudioRouteCodec codec, bool required, IEnumerable<string>? registeredValues = null)
    {
        StudioContractValidation.Id(name); StudioContractValidation.Enum(codec);
        bool requiresValues = codec is StudioRouteCodec.RegisteredEnum or StudioRouteCodec.SelectedTab;
        ImmutableArray<string> values = StudioContractValidation.Ids(registeredValues ?? [], 64, !requiresValues, nameof(registeredValues));
        if (!requiresValues && values.Length != 0) throw new ArgumentException("This Studio query codec does not admit registered values.", nameof(registeredValues));
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.route-query.v1", writer =>
        { writer.String(name); writer.Enum(codec); writer.Boolean(required); writer.Count(values.Length); foreach (string value in values) writer.String(value); });
        return new(name, codec, required, values, checksum);
    }
}

/// <summary>Defines one bounded canonical Studio route template.</summary>
public sealed class StudioRouteTemplate
{
    private StudioRouteTemplate(string id, ImmutableArray<StudioRouteSegment> segments,
        ImmutableArray<StudioQueryParameter> query, string pattern, StudioSha256 checksum)
    { TemplateId = id; Segments = segments; Query = query; PatternKey = pattern; Checksum = checksum; }
    /// <summary>Gets the route-template identity.</summary>
    public string TemplateId { get; }
    /// <summary>Gets segments in path order.</summary>
    public ImmutableArray<StudioRouteSegment> Segments { get; }
    /// <summary>Gets query members in ordinal name order.</summary>
    public ImmutableArray<StudioQueryParameter> Query { get; }
    internal string PatternKey { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }

    /// <summary>Creates and checksums a route template.</summary>
    public static StudioRouteTemplate Create(string id, IEnumerable<StudioRouteSegment> segments,
        IEnumerable<StudioQueryParameter>? query = null)
    {
        StudioContractValidation.Id(id);
        // An empty segment sequence is the one canonical application-root route (`/`).
        // All non-root routes remain bounded by the same closed segment grammar.
        ImmutableArray<StudioRouteSegment> ownedSegments = StudioContractValidation.Materialize(segments, 8, true, nameof(segments));
        ImmutableArray<StudioQueryParameter> ownedQuery = StudioContractValidation.Materialize(query ?? [], 16, true, nameof(query));
        if (ownedSegments.Select(static value => value.Kind == StudioRouteSegmentKind.Parameter ? value.Value : null)
                .Where(static value => value is not null).Distinct(StringComparer.Ordinal).Count() !=
            ownedSegments.Count(static value => value.Kind == StudioRouteSegmentKind.Parameter))
            throw new ArgumentException("Studio route parameters must be unique.", nameof(segments));
        if (!ownedQuery.Select(static value => value.Name).SequenceEqual(ownedQuery.Select(static value => value.Name).Order(StringComparer.Ordinal)) ||
            ownedQuery.Select(static value => value.Name).Distinct(StringComparer.Ordinal).Count() != ownedQuery.Length)
            throw new ArgumentException("Studio query members are not canonical.", nameof(query));
        foreach (StudioRouteSegment segment in ownedSegments)
        {
            if (segment.Kind == StudioRouteSegmentKind.Literal &&
                !segment.Value.All(static value => char.IsAsciiLetterOrDigit(value) || value is '-' or '.' or '_' or '~'))
                throw new ArgumentException("Studio route literals must use canonical RFC 3986 unreserved characters.", nameof(segments));
        }
        string pattern = string.Join('/', ownedSegments.Select(static value => value.Kind == StudioRouteSegmentKind.Literal
            ? "l:" + value.Value : "p:" + (byte)value.Codec!.Value)) + "?" +
            string.Join('&', ownedQuery.Select(static value => $"{value.Name}:{(byte)value.Codec}:{(value.Required ? 1 : 0)}"));
        if (System.Text.Encoding.UTF8.GetByteCount(pattern) > 512)
            throw new ArgumentException("A Studio route exceeds the canonical URL bound.", nameof(segments));
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.route.v1", writer =>
        {
            writer.String(id); writer.Count(ownedSegments.Length); foreach (StudioRouteSegment value in ownedSegments) writer.Checksum(value.Checksum);
            writer.Count(ownedQuery.Length); foreach (StudioQueryParameter value in ownedQuery) writer.Checksum(value.Checksum);
        });
        return new(id, ownedSegments, ownedQuery, pattern, checksum);
    }

    internal bool Overlaps(StudioRouteTemplate other)
    {
        if (Segments.Length != other.Segments.Length) return false;
        for (int index = 0; index < Segments.Length; index++)
        {
            StudioRouteSegment left = Segments[index];
            StudioRouteSegment right = other.Segments[index];
            if (left.Kind == StudioRouteSegmentKind.Literal && right.Kind == StudioRouteSegmentKind.Literal &&
                !StringComparer.Ordinal.Equals(left.Value, right.Value)) return false;
            // The graph does not rely on codec- or literal-specific routing precedence.
            // Any position involving a parameter is conservatively overlapping unless a
            // preceding literal pair already proved the templates disjoint.
        }
        return true;
    }
}
