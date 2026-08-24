using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Identifies one closed Studio route parameter codec.</summary>
public enum BaseStudioRouteCodec : byte
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
public enum BaseStudioRouteSegmentKind : byte { Literal = 1, Parameter }

/// <summary>Defines one immutable route segment.</summary>
public sealed class BaseStudioRouteSegment
{
    private BaseStudioRouteSegment(BaseStudioRouteSegmentKind kind, string value, BaseStudioRouteCodec? codec, BaseStudioSha256 checksum)
    { Kind = kind; Value = value; Codec = codec; Checksum = checksum; }
    /// <summary>Gets the segment kind.</summary>
    public BaseStudioRouteSegmentKind Kind { get; }
    /// <summary>Gets the literal value or parameter name.</summary>
    public string Value { get; }
    /// <summary>Gets the parameter codec.</summary>
    public BaseStudioRouteCodec? Codec { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates a literal segment.</summary>
    public static BaseStudioRouteSegment Literal(string value) => Create(BaseStudioRouteSegmentKind.Literal, value, null);
    /// <summary>Creates a typed parameter segment.</summary>
    public static BaseStudioRouteSegment Parameter(string name, BaseStudioRouteCodec codec) => Create(BaseStudioRouteSegmentKind.Parameter, name, codec);

    private static BaseStudioRouteSegment Create(BaseStudioRouteSegmentKind kind, string value, BaseStudioRouteCodec? codec)
    {
        StudioContractValidation.Enum(kind); StudioContractValidation.Id(value);
        if ((kind == BaseStudioRouteSegmentKind.Parameter) != codec.HasValue) throw new ArgumentException("Studio route codec correspondence is invalid.");
        if (codec.HasValue) StudioContractValidation.Enum(codec.Value);
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.route-segment.v1", writer =>
        { writer.Enum(kind); writer.String(value); writer.Boolean(codec.HasValue); if (codec.HasValue) writer.Enum(codec.Value); });
        return new(kind, value, codec, checksum);
    }
}

/// <summary>Defines one registered route query member.</summary>
public sealed class BaseStudioQueryParameter
{
    private BaseStudioQueryParameter(string name, BaseStudioRouteCodec codec, bool required, ImmutableArray<string> values, BaseStudioSha256 checksum)
    { Name = name; Codec = codec; Required = required; RegisteredValues = values; Checksum = checksum; }
    /// <summary>Gets the canonical query name.</summary>
    public string Name { get; }
    /// <summary>Gets the value codec.</summary>
    public BaseStudioRouteCodec Codec { get; }
    /// <summary>Gets whether the member is required.</summary>
    public bool Required { get; }
    /// <summary>Gets registered values for enum and tab codecs.</summary>
    public ImmutableArray<string> RegisteredValues { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates a route query member.</summary>
    public static BaseStudioQueryParameter Create(string name, BaseStudioRouteCodec codec, bool required, IEnumerable<string>? registeredValues = null)
    {
        StudioContractValidation.Id(name); StudioContractValidation.Enum(codec);
        bool requiresValues = codec is BaseStudioRouteCodec.RegisteredEnum or BaseStudioRouteCodec.SelectedTab;
        ImmutableArray<string> values = StudioContractValidation.Ids(registeredValues ?? [], 64, !requiresValues, nameof(registeredValues));
        if (!requiresValues && values.Length != 0) throw new ArgumentException("This Studio query codec does not admit registered values.", nameof(registeredValues));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.route-query.v1", writer =>
        { writer.String(name); writer.Enum(codec); writer.Boolean(required); writer.Count(values.Length); foreach (string value in values) writer.String(value); });
        return new(name, codec, required, values, checksum);
    }
}

/// <summary>Defines one bounded canonical Studio route template.</summary>
public sealed class BaseStudioRouteTemplate
{
    private BaseStudioRouteTemplate(string id, ImmutableArray<BaseStudioRouteSegment> segments,
        ImmutableArray<BaseStudioQueryParameter> query, string pattern, BaseStudioSha256 checksum)
    { TemplateId = id; Segments = segments; Query = query; PatternKey = pattern; Checksum = checksum; }
    /// <summary>Gets the route-template identity.</summary>
    public string TemplateId { get; }
    /// <summary>Gets segments in path order.</summary>
    public ImmutableArray<BaseStudioRouteSegment> Segments { get; }
    /// <summary>Gets query members in ordinal name order.</summary>
    public ImmutableArray<BaseStudioQueryParameter> Query { get; }
    internal string PatternKey { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums a route template.</summary>
    public static BaseStudioRouteTemplate Create(string id, IEnumerable<BaseStudioRouteSegment> segments,
        IEnumerable<BaseStudioQueryParameter>? query = null)
    {
        StudioContractValidation.Id(id);
        // An empty segment sequence is the one canonical application-root route (`/`).
        // All non-root routes remain bounded by the same closed segment grammar.
        ImmutableArray<BaseStudioRouteSegment> ownedSegments = StudioContractValidation.Materialize(segments, 8, true, nameof(segments));
        ImmutableArray<BaseStudioQueryParameter> ownedQuery = StudioContractValidation.Materialize(query ?? [], 16, true, nameof(query));
        if (ownedSegments.Select(static value => value.Kind == BaseStudioRouteSegmentKind.Parameter ? value.Value : null)
                .Where(static value => value is not null).Distinct(StringComparer.Ordinal).Count() !=
            ownedSegments.Count(static value => value.Kind == BaseStudioRouteSegmentKind.Parameter))
            throw new ArgumentException("Studio route parameters must be unique.", nameof(segments));
        if (!ownedQuery.Select(static value => value.Name).SequenceEqual(ownedQuery.Select(static value => value.Name).Order(StringComparer.Ordinal)) ||
            ownedQuery.Select(static value => value.Name).Distinct(StringComparer.Ordinal).Count() != ownedQuery.Length)
            throw new ArgumentException("Studio query members are not canonical.", nameof(query));
        foreach (BaseStudioRouteSegment segment in ownedSegments)
        {
            if (segment.Kind == BaseStudioRouteSegmentKind.Literal &&
                !segment.Value.All(static value => char.IsAsciiLetterOrDigit(value) || value is '-' or '.' or '_' or '~'))
                throw new ArgumentException("Studio route literals must use canonical RFC 3986 unreserved characters.", nameof(segments));
        }
        string pattern = string.Join('/', ownedSegments.Select(static value => value.Kind == BaseStudioRouteSegmentKind.Literal
            ? "l:" + value.Value : "p:" + (byte)value.Codec!.Value)) + "?" +
            string.Join('&', ownedQuery.Select(static value => $"{value.Name}:{(byte)value.Codec}:{(value.Required ? 1 : 0)}"));
        if (System.Text.Encoding.UTF8.GetByteCount(pattern) > 512)
            throw new ArgumentException("A Studio route exceeds the canonical URL bound.", nameof(segments));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.route.v1", writer =>
        {
            writer.String(id); writer.Count(ownedSegments.Length); foreach (BaseStudioRouteSegment value in ownedSegments) writer.Checksum(value.Checksum);
            writer.Count(ownedQuery.Length); foreach (BaseStudioQueryParameter value in ownedQuery) writer.Checksum(value.Checksum);
        });
        return new(id, ownedSegments, ownedQuery, pattern, checksum);
    }

    internal bool Overlaps(BaseStudioRouteTemplate other)
    {
        if (Segments.Length != other.Segments.Length) return false;
        for (int index = 0; index < Segments.Length; index++)
        {
            BaseStudioRouteSegment left = Segments[index];
            BaseStudioRouteSegment right = other.Segments[index];
            if (left.Kind == BaseStudioRouteSegmentKind.Literal && right.Kind == BaseStudioRouteSegmentKind.Literal &&
                !StringComparer.Ordinal.Equals(left.Value, right.Value)) return false;
            // The graph does not rely on codec- or literal-specific routing precedence.
            // Any position involving a parameter is conservatively overlapping unless a
            // preceding literal pair already proved the templates disjoint.
        }
        return true;
    }
}
