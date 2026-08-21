using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Represents one closed provider-neutral lexical query.</summary>
public abstract record BaseTextQuery
{
    private BaseTextQuery() { }

    /// <summary>Matches one normalized token.</summary>
    public sealed record Term : BaseTextQuery
    {
        internal Term(string value) => Value = value;
        /// <summary>Gets the caller text normalized by the BASE analyzer.</summary>
        public string Value { get; }
    }

    /// <summary>Matches normalized tokens beginning with one prefix.</summary>
    public sealed record Prefix : BaseTextQuery
    {
        internal Prefix(string value) => Value = value;
        /// <summary>Gets the caller prefix normalized by the BASE analyzer.</summary>
        public string Value { get; }
    }

    /// <summary>Matches consecutive normalized tokens inside one field.</summary>
    public sealed record Phrase : BaseTextQuery
    {
        internal Phrase(ImmutableArray<string> terms) => Terms = terms;
        /// <summary>Gets phrase tokens in source order.</summary>
        public ImmutableArray<string> Terms { get; }
    }

    /// <summary>Restricts a child query to one stable searchable field.</summary>
    public sealed record Field : BaseTextQuery
    {
        internal Field(string stableFieldId, BaseTextQuery child) { StableFieldId = stableFieldId; Child = child; }
        /// <summary>Gets the stable field identity.</summary>
        public string StableFieldId { get; }
        /// <summary>Gets the restricted child query.</summary>
        public BaseTextQuery Child { get; }
    }

    /// <summary>Requires every positive child and excludes its direct negative children.</summary>
    public sealed record And : BaseTextQuery
    {
        internal And(ImmutableArray<BaseTextQuery> children) => Children = children;
        /// <summary>Gets canonical distinct children.</summary>
        public ImmutableArray<BaseTextQuery> Children { get; }
    }

    /// <summary>Requires at least one child.</summary>
    public sealed record Or : BaseTextQuery
    {
        internal Or(ImmutableArray<BaseTextQuery> children) => Children = children;
        /// <summary>Gets canonical distinct children.</summary>
        public ImmutableArray<BaseTextQuery> Children { get; }
    }

    /// <summary>Excludes matches of one child under an anchored conjunction.</summary>
    public sealed record Not : BaseTextQuery
    {
        internal Not(BaseTextQuery child) => Child = child;
        /// <summary>Gets the excluded child.</summary>
        public BaseTextQuery Child { get; }
    }

    /// <summary>Creates one validated token query.</summary>
    public static BaseTextQuery Token(string value) => BaseTextQueryContract.Term(value);
    /// <summary>Creates one validated prefix query.</summary>
    public static BaseTextQuery StartsWith(string value) => BaseTextQueryContract.Prefix(value);
    /// <summary>Creates one validated phrase query.</summary>
    public static BaseTextQuery ExactPhrase(params string[] terms) => BaseTextQueryContract.Phrase(terms);
    /// <summary>Restricts a query to one stable field identity.</summary>
    public static BaseTextQuery InField(string stableFieldId, BaseTextQuery child) => BaseTextQueryContract.Field(stableFieldId, child);
    /// <summary>Creates a canonical conjunction.</summary>
    public static BaseTextQuery All(params BaseTextQuery[] children) => BaseTextQueryContract.And(children);
    /// <summary>Creates a canonical disjunction.</summary>
    public static BaseTextQuery Any(params BaseTextQuery[] children) => BaseTextQueryContract.Or(children);
    /// <summary>Creates an exclusion node that is valid only under an anchored conjunction.</summary>
    public static BaseTextQuery Exclude(BaseTextQuery child) => BaseTextQueryContract.Not(child);
}

/// <summary>Owns validation, normalization, and canonical bytes for lexical queries.</summary>
public static class BaseTextQueryContract
{
    private const int MaximumNodes = 64;
    private const int MaximumDepth = 12;

    /// <summary>Creates one validated term.</summary>
    public static BaseTextQuery Term(string value)
    {
        ImmutableArray<string> tokens = BaseTextAnalyzer.Analyze(value);
        if (tokens.Length != 1) throw Invalid("A term must normalize to exactly one token.");
        return new BaseTextQuery.Term(tokens[0]);
    }

    /// <summary>Creates one validated prefix.</summary>
    public static BaseTextQuery Prefix(string value)
    {
        ImmutableArray<string> tokens = BaseTextAnalyzer.Analyze(value);
        if (tokens.Length != 1 || Encoding.UTF8.GetByteCount(tokens[0]) is < 2 or > 64)
            throw Invalid("A prefix must normalize to one token containing 2 through 64 UTF-8 bytes.");
        return new BaseTextQuery.Prefix(tokens[0]);
    }

    /// <summary>Creates one validated phrase.</summary>
    public static BaseTextQuery Phrase(IEnumerable<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var result = ImmutableArray.CreateBuilder<string>();
        foreach (string term in terms)
        {
            ImmutableArray<string> tokens = BaseTextAnalyzer.Analyze(term);
            if (tokens.Length != 1) throw Invalid("Each phrase member must normalize to exactly one token.");
            result.Add(tokens[0]);
        }
        if (result.Count is < 2 or > 16) throw Invalid("A phrase must contain 2 through 16 terms.");
        return new BaseTextQuery.Phrase(result.ToImmutable());
    }

    /// <summary>Creates one validated field restriction.</summary>
    public static BaseTextQuery Field(string stableFieldId, BaseTextQuery child)
    {
        ValidateId(stableFieldId);
        ArgumentNullException.ThrowIfNull(child);
        return Validate(new BaseTextQuery.Field(stableFieldId, child));
    }

    /// <summary>Creates one canonical conjunction.</summary>
    public static BaseTextQuery And(IEnumerable<BaseTextQuery> children) => Logical(children, true);
    /// <summary>Creates one canonical disjunction.</summary>
    public static BaseTextQuery Or(IEnumerable<BaseTextQuery> children) => Logical(children, false);

    /// <summary>Creates one exclusion node.</summary>
    public static BaseTextQuery Not(BaseTextQuery child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (ContainsNot(child)) throw Invalid("A negative child cannot contain another negative node.");
        return new BaseTextQuery.Not(child);
    }

    /// <summary>Validates a complete query and returns the same immutable graph.</summary>
    public static BaseTextQuery Validate(BaseTextQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        int nodes = 0;
        Visit(query, 1, false, ref nodes);
        return query;
    }

    /// <summary>Returns the canonical query bytes.</summary>
    public static ImmutableArray<byte> Encode(BaseTextQuery query)
    {
        Validate(query);
        using var stream = new MemoryStream();
        stream.Write("HPDB-TEXT-QUERY-1\0"u8);
        WriteNode(stream, query);
        return ImmutableArray.Create(stream.ToArray());
    }

    /// <summary>Returns the canonical SHA-256 structural digest.</summary>
    public static ImmutableArray<byte> Digest(BaseTextQuery query) => ImmutableArray.Create(SHA256.HashData(Encode(query).AsSpan()));

    private static BaseTextQuery Logical(IEnumerable<BaseTextQuery> values, bool and)
    {
        ArgumentNullException.ThrowIfNull(values);
        var flattened = new List<BaseTextQuery>();
        foreach (BaseTextQuery child in values)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (and && child is BaseTextQuery.And nestedAnd) flattened.AddRange(nestedAnd.Children);
            else if (!and && child is BaseTextQuery.Or nestedOr) flattened.AddRange(nestedOr.Children);
            else flattened.Add(child);
        }
        var distinct = flattened
            .Select(static child => (Child: child, Bytes: EncodeNode(child)))
            .OrderBy(static item => item.Bytes, ByteArrayComparer.Instance)
            .GroupBy(static item => Convert.ToHexString(item.Bytes), StringComparer.Ordinal)
            .Select(static group => group.First().Child)
            .ToImmutableArray();
        if (distinct.Length is < 2 or > 16) throw Invalid("A logical node must contain 2 through 16 distinct children.");
        BaseTextQuery result = and ? new BaseTextQuery.And(distinct) : new BaseTextQuery.Or(distinct);
        return Validate(result);
    }

    private static void Visit(BaseTextQuery query, int depth, bool directAndChild, ref int nodes)
    {
        if (depth > MaximumDepth || ++nodes > MaximumNodes) throw Invalid("The query exceeds its structural bounds.");
        switch (query)
        {
            case BaseTextQuery.Term term when string.IsNullOrEmpty(term.Value): throw Invalid("A term is empty.");
            case BaseTextQuery.Prefix prefix when Encoding.UTF8.GetByteCount(prefix.Value) is < 2 or > 64: throw Invalid("A prefix is outside its byte bounds.");
            case BaseTextQuery.Phrase phrase when phrase.Terms.Length is < 2 or > 16: throw Invalid("A phrase is outside its term bounds.");
            case BaseTextQuery.Field field:
                ValidateId(field.StableFieldId); Visit(field.Child, depth + 1, false, ref nodes); break;
            case BaseTextQuery.And and:
                if (and.Children.Length is < 2 or > 16 || and.Children.All(static child => child is BaseTextQuery.Not)) throw Invalid("An AND requires a positive anchor.");
                foreach (BaseTextQuery child in and.Children) Visit(child, depth + 1, true, ref nodes);
                break;
            case BaseTextQuery.Or or:
                if (or.Children.Length is < 2 or > 16 || or.Children.All(static child => child is BaseTextQuery.Not)) throw Invalid("An OR requires a positive branch.");
                foreach (BaseTextQuery child in or.Children) Visit(child, depth + 1, false, ref nodes);
                break;
            case BaseTextQuery.Not not:
                if (!directAndChild || ContainsNot(not.Child)) throw Invalid("NOT is valid only as a direct child of an anchored AND.");
                Visit(not.Child, depth + 1, false, ref nodes); break;
            case BaseTextQuery.Term or BaseTextQuery.Prefix or BaseTextQuery.Phrase: break;
            default: throw Invalid("The query contains an unknown node.");
        }
    }

    private static bool ContainsNot(BaseTextQuery query) => query switch
    {
        BaseTextQuery.Not => true,
        BaseTextQuery.Field field => ContainsNot(field.Child),
        BaseTextQuery.And and => and.Children.Any(ContainsNot),
        BaseTextQuery.Or or => or.Children.Any(ContainsNot),
        _ => false,
    };

    private static byte[] EncodeNode(BaseTextQuery query)
    {
        using var stream = new MemoryStream();
        WriteNode(stream, query);
        return stream.ToArray();
    }

    private static void WriteNode(Stream stream, BaseTextQuery query)
    {
        switch (query)
        {
            case BaseTextQuery.Term term: stream.WriteByte(1); WriteString(stream, term.Value); break;
            case BaseTextQuery.Prefix prefix: stream.WriteByte(2); WriteString(stream, prefix.Value); break;
            case BaseTextQuery.Phrase phrase: stream.WriteByte(3); WriteSequence(stream, phrase.Terms, WriteString); break;
            case BaseTextQuery.Field field: stream.WriteByte(4); WriteString(stream, field.StableFieldId); WriteNode(stream, field.Child); break;
            case BaseTextQuery.And and: stream.WriteByte(5); WriteSequence(stream, and.Children, WriteNode); break;
            case BaseTextQuery.Or or: stream.WriteByte(6); WriteSequence(stream, or.Children, WriteNode); break;
            case BaseTextQuery.Not not: stream.WriteByte(7); WriteNode(stream, not.Child); break;
            default: throw Invalid("The query contains an unknown node.");
        }
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(count, checked((uint)bytes.Length));
        stream.Write(count); stream.Write(bytes);
    }

    private static void WriteSequence<T>(Stream stream, ImmutableArray<T> values, Action<Stream, T> writer)
    {
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(count, checked((uint)values.Length));
        stream.Write(count);
        foreach (T value in values) writer(stream, value);
    }

    private static void ValidateId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 128 || value.Any(static c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw Invalid("A stable field identity is invalid.");
    }

    private static ArgumentException Invalid(string message) => new(message, BaseTextErrorCodes.QueryInvalid);

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        internal static readonly ByteArrayComparer Instance = new();
        public int Compare(byte[]? x, byte[]? y) => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            _ => x.AsSpan().SequenceCompareTo(y),
        };
    }
}
