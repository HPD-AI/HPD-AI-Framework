using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

/// <summary>Contains the exact portable evaluation of one authoritative record.</summary>
public sealed record BaseTextEvaluatedCandidate(BaseTextScore Score, BaseTextCandidateScoreProof Proof);

/// <summary>Provides the portable semantic oracle used for provider certification and Runtime verification.</summary>
public static class BaseTextSemanticEvaluator
{
    internal static long ValidateIndexedPayload(RecordPayload payload, BaseTextIndexDefinition index)
    {
        long bytes = 0;
        foreach (BaseTextIndexFieldDefinition field in index.Fields) if (TryField(payload, field, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            ImmutableArray<string> tokens = BaseTextAnalyzer.Analyze(value.GetString()!); long fieldBytes = tokens.Sum(static token => (long)Encoding.UTF8.GetByteCount(token));
            if (tokens.Length > index.Limits.MaximumTokensPerField || fieldBytes > index.Limits.MaximumNormalizedBytesPerField) throw new ArgumentException(BaseTextErrorCodes.BudgetExceeded);
            bytes = checked(bytes + fieldBytes);
        }
        if (bytes > index.Limits.MaximumNormalizedBytesPerRecord) throw new ArgumentException(BaseTextErrorCodes.BudgetExceeded);
        return bytes;
    }
    internal static string NormalizedCarrierText(RecordPayload payload, BaseTextIndexDefinition index)
    {
        var tokens = new List<string>(); foreach (BaseTextIndexFieldDefinition field in index.Fields) if (TryField(payload, field, out JsonElement value) && value.ValueKind == JsonValueKind.String) tokens.AddRange(BaseTextAnalyzer.Analyze(value.GetString()!));
        _ = ValidateIndexedPayload(payload, index); return string.Join(' ', tokens);
    }
    /// <summary>Encodes the mandatory score-descending and record-ID-ascending continuation boundary.</summary>
    public static ImmutableArray<byte> OrderingBoundary(BaseTextScore score, RecordId id)
    {
        byte[] text = Encoding.UTF8.GetBytes(id.Value); byte[] result = new byte[12 + text.Length];
        BinaryPrimitives.WriteUInt64BigEndian(result, ulong.MaxValue - score.Units); BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), checked((uint)text.Length)); text.CopyTo(result, 12);
        return ImmutableArray.Create(result);
    }
    /// <summary>Evaluates matching, feature evidence, and score over one canonical payload.</summary>
    public static BaseTextEvaluatedCandidate? Evaluate(RecordPayload payload, BaseTextIndexDefinition index, BaseTextQuery query, ImmutableArray<byte> queryDigest, ImmutableArray<BaseTextFieldInfluenceConstraint> influences = default)
    {
        Dictionary<string, BaseTextCandidateConstraint> influenceByField = influences.IsDefaultOrEmpty ? new(StringComparer.Ordinal) : influences.ToDictionary(static value => value.StableFieldId, static value => value.Constraint, StringComparer.Ordinal);
        var fields = new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal);
        foreach (BaseTextIndexFieldDefinition field in index.Fields)
        {
            if (influenceByField.TryGetValue(field.StableFieldId, out BaseTextCandidateConstraint? influence) && !ConstraintMatches(payload, index, influence)) fields[field.StableFieldId] = [];
            else if (!TryField(payload, field, out JsonElement value) || value.ValueKind != JsonValueKind.String) fields[field.StableFieldId] = [];
            else fields[field.StableFieldId] = BaseTextAnalyzer.Analyze(value.GetString()!);
        }
        Eval result = Match(query, fields, null);
        if (!result.Matched) return null;
        BaseTextFeatureEvidence[] features = result.Features.Values.OrderBy(static value => FeatureKey(value), ByteComparer.Instance).ToArray();
        var contributions = new List<ulong>(features.Length);
        foreach (BaseTextFeatureEvidence feature in features)
        {
            BaseTextIndexFieldDefinition definition = index.Fields.Single(value => value.StableFieldId == feature.StableFieldId);
            contributions.Add(BaseTextScoring.Feature(definition.Weight, feature.CandidateTermFrequency, fields[feature.StableFieldId].Length));
        }
        BaseTextFieldStatistics[] statistics = index.Fields.OrderBy(static value => value.StableFieldId, StringComparer.Ordinal)
            .Select(field => new BaseTextFieldStatistics { StableFieldId = field.StableFieldId, CandidateTokenCount = fields[field.StableFieldId].Length }).ToArray();
        byte[] proofBytes = ProofBytes(statistics, features);
        byte[] digestInput = new byte[queryDigest.Length + proofBytes.Length]; queryDigest.CopyTo(digestInput); proofBytes.CopyTo(digestInput, queryDigest.Length);
        return new BaseTextEvaluatedCandidate(BaseTextScoring.Sum(contributions), new BaseTextCandidateScoreProof
        {
            Fields = [.. statistics], Features = [.. features], ProofDigest = ImmutableArray.Create(SHA256.HashData(digestInput)),
        });
    }

    /// <summary>Evaluates one exact pre-ranking candidate constraint.</summary>
    public static bool ConstraintMatches(RecordPayload payload, BaseTextIndexDefinition index, BaseTextCandidateConstraint constraint) => constraint switch
    {
        BaseTextCandidateConstraint.True => true,
        BaseTextCandidateConstraint.False => false,
        BaseTextCandidateConstraint.And value => value.Children.All(child => ConstraintMatches(payload, index, child)),
        BaseTextCandidateConstraint.Or value => value.Children.Any(child => ConstraintMatches(payload, index, child)),
        BaseTextCandidateConstraint.IsMissing value => !TryFilter(payload, index, value.Field, out _, out _),
        BaseTextCandidateConstraint.IsNull value => TryFilter(payload, index, value.Field, out bool isNull, out _) && isNull,
        BaseTextCandidateConstraint.Equal value => TryFilter(payload, index, value.Field, out bool isNull, out BaseTextFilterValue? actual) && !isNull && actual == value.Value,
        BaseTextCandidateConstraint.In value => TryFilter(payload, index, value.Field, out bool isNull, out BaseTextFilterValue? actual) && !isNull && value.Values.Contains(actual!),
        _ => false,
    };

    /// <summary>Computes the canonical constraint digest.</summary>
    public static ImmutableArray<byte> ConstraintDigest(BaseTextCandidateConstraint value)
    {
        return ImmutableArray.Create(SHA256.HashData(ConstraintEncoding(value).AsSpan()));
    }
    /// <summary>Returns the canonical provider-neutral candidate-constraint bytes.</summary>
    public static ImmutableArray<byte> ConstraintEncoding(BaseTextCandidateConstraint value) { using var stream = new MemoryStream(); stream.Write("HPDB-TEXT-CONSTRAINT-1\0"u8); WriteConstraint(stream, value); return ImmutableArray.Create(stream.ToArray()); }
    internal static ImmutableArray<byte> ConstraintNodeEncoding(BaseTextCandidateConstraint value) { using var stream = new MemoryStream(); WriteConstraint(stream, value); return ImmutableArray.Create(stream.ToArray()); }
    internal static ImmutableArray<byte> FilterValueEncoding(BaseTextFilterValue value) { using var stream = new MemoryStream(); WriteValue(stream, value); return ImmutableArray.Create(stream.ToArray()); }

    private static Eval Match(BaseTextQuery query, Dictionary<string, ImmutableArray<string>> fields, string? selected) => query switch
    {
        BaseTextQuery.Term value => Atomic(BaseTextFeatureKind.Term, [value.Value], fields, selected),
        BaseTextQuery.Prefix value => Atomic(BaseTextFeatureKind.Prefix, [value.Value], fields, selected),
        BaseTextQuery.Phrase value => Atomic(BaseTextFeatureKind.Phrase, value.Terms, fields, selected),
        BaseTextQuery.Field value => Match(value.Child, fields, value.StableFieldId),
        BaseTextQuery.Or value => Or(value.Children.Select(child => Match(child, fields, selected))),
        BaseTextQuery.And value => And(value.Children, fields, selected),
        BaseTextQuery.Not value => new Eval(!Match(value.Child, fields, selected).Matched, []),
        _ => new(false, []),
    };

    private static Eval Atomic(BaseTextFeatureKind kind, ImmutableArray<string> terms, Dictionary<string, ImmutableArray<string>> fields, string? selected)
    {
        var features = new Dictionary<string, BaseTextFeatureEvidence>(StringComparer.Ordinal);
        IEnumerable<KeyValuePair<string, ImmutableArray<string>>> candidates = selected is null ? fields : fields.Where(pair => pair.Key == selected);
        foreach ((string field, ImmutableArray<string> tokens) in candidates)
        {
            int tf; ImmutableArray<ImmutableArray<byte>> expansions = [];
            if (kind == BaseTextFeatureKind.Term) tf = tokens.Count(token => token == terms[0]);
            else if (kind == BaseTextFeatureKind.Prefix)
            {
                string[] matching = tokens.Where(token => token.StartsWith(terms[0], StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(ByteStringComparer.Instance).ToArray();
                if (matching.Length > 256 || matching.Sum(static token => Encoding.UTF8.GetByteCount(token)) > 16 * 1024) throw new InvalidOperationException(BaseTextErrorCodes.BudgetExceeded);
                tf = Math.Min(65_535, tokens.Count(token => token.StartsWith(terms[0], StringComparison.Ordinal)));
                expansions = matching.Select(static token => ImmutableArray.Create(Encoding.UTF8.GetBytes(token))).ToImmutableArray();
            }
            else tf = PhraseFrequency(tokens, terms);
            if (tf == 0) continue;
            BaseTextFeatureEvidence evidence = new() { Kind = kind, StableFieldId = field, NormalizedTokens = terms.Select(static token => ImmutableArray.Create(Encoding.UTF8.GetBytes(token))).ToImmutableArray(), CandidateTermFrequency = Math.Min(65_535, tf), PrefixExpansions = expansions };
            features[Convert.ToHexString(FeatureKey(evidence))] = evidence;
        }
        return new(features.Count != 0, features);
    }

    private static Eval And(ImmutableArray<BaseTextQuery> children, Dictionary<string, ImmutableArray<string>> fields, string? selected)
    {
        var merged = new Dictionary<string, BaseTextFeatureEvidence>(StringComparer.Ordinal);
        foreach (BaseTextQuery child in children)
        {
            Eval result = Match(child, fields, selected); if (!result.Matched) return new(false, []);
            foreach ((string key, BaseTextFeatureEvidence value) in result.Features) merged[key] = value;
        }
        return new(true, merged);
    }
    private static Eval Or(IEnumerable<Eval> values)
    {
        var merged = new Dictionary<string, BaseTextFeatureEvidence>(StringComparer.Ordinal); bool any = false;
        foreach (Eval value in values) { if (!value.Matched) continue; any = true; foreach ((string key, BaseTextFeatureEvidence feature) in value.Features) merged[key] = feature; }
        return new(any, merged);
    }
    private static int PhraseFrequency(ImmutableArray<string> tokens, ImmutableArray<string> phrase)
    { int count = 0; for (int i = 0; i + phrase.Length <= tokens.Length; i++) { bool match = true; for (int j = 0; j < phrase.Length; j++) if (tokens[i + j] != phrase[j]) { match = false; break; } if (match) count++; } return Math.Min(65_535, count); }
    private static bool TryField(RecordPayload payload, BaseTextIndexFieldDefinition field, out JsonElement value)
    {
        value = default;
        return payload.Fields is not null && (payload.Fields.TryGetValue(field.StableFieldId, out value) || payload.Fields.TryGetValue(field.WireName, out value));
    }
    private static bool TryFilter(RecordPayload payload, BaseTextIndexDefinition index, BaseTextFilterField field, out bool isNull, out BaseTextFilterValue? value)
    {
        BaseTextIndexFilterFieldDefinition? definition = index.FilterFields.SingleOrDefault(item => item.StableFieldId == field.StableFieldId && item.ValueKind == field.ValueKind);
        if (definition is null || payload.Fields is null || !payload.Fields.TryGetValue(definition.StableFieldId, out JsonElement json) && !payload.Fields.TryGetValue(definition.WireName, out json)) { isNull = false; value = null; return false; }
        isNull = json.ValueKind == JsonValueKind.Null; value = isNull ? null : field.ValueKind switch
        {
            BaseTextFilterValueKind.String when json.ValueKind == JsonValueKind.String => BaseTextFilterValue.FromString(json.GetString()!),
            BaseTextFilterValueKind.Id when json.ValueKind == JsonValueKind.String => BaseTextFilterValue.FromId(json.GetString()!),
            BaseTextFilterValueKind.Boolean when json.ValueKind is JsonValueKind.True or JsonValueKind.False => BaseTextFilterValue.FromBoolean(json.GetBoolean()),
            BaseTextFilterValueKind.Integer when json.TryGetInt64(out long integer) => BaseTextFilterValue.FromInteger(integer),
            _ => null,
        }; return isNull || value is not null;
    }
    private static byte[] FeatureKey(BaseTextFeatureEvidence value)
    { using var stream = new MemoryStream(); stream.WriteByte((byte)value.Kind); WriteString(stream, value.StableFieldId); foreach (ImmutableArray<byte> token in value.NormalizedTokens) { WriteCount(stream, token.Length); stream.Write(token.AsSpan()); } return stream.ToArray(); }
    private static byte[] ProofBytes(IEnumerable<BaseTextFieldStatistics> fields, IEnumerable<BaseTextFeatureEvidence> features)
    { using var stream = new MemoryStream(); foreach (BaseTextFieldStatistics field in fields) { WriteString(stream, field.StableFieldId); WriteLong(stream, field.CandidateTokenCount); } foreach (BaseTextFeatureEvidence feature in features) { byte[] key = FeatureKey(feature); WriteCount(stream, key.Length); stream.Write(key); WriteLong(stream, feature.CandidateTermFrequency); foreach (ImmutableArray<byte> expansion in feature.PrefixExpansions) { WriteCount(stream, expansion.Length); stream.Write(expansion.AsSpan()); } } return stream.ToArray(); }
    internal static long ProofRetainedBytes(BaseTextCandidateScoreProof proof) => ProofBytes(proof.Fields, proof.Features).LongLength + proof.ProofDigest.Length;
    internal static long PrefixExpansionCount(BaseTextCandidateScoreProof proof) => proof.Features.Sum(static feature => (long)feature.PrefixExpansions.Length);
    internal static long PrefixExpansionBytes(BaseTextCandidateScoreProof proof) => proof.Features.Sum(static feature => feature.PrefixExpansions.Sum(static expansion => (long)expansion.Length));
    private static void WriteConstraint(Stream stream, BaseTextCandidateConstraint value)
    {
        switch (value)
        {
            case BaseTextCandidateConstraint.True: stream.WriteByte(1); break; case BaseTextCandidateConstraint.False: stream.WriteByte(2); break;
            case BaseTextCandidateConstraint.And and: stream.WriteByte(3); WriteCount(stream, and.Children.Length); foreach (var child in and.Children) WriteConstraint(stream, child); break;
            case BaseTextCandidateConstraint.Or or: stream.WriteByte(4); WriteCount(stream, or.Children.Length); foreach (var child in or.Children) WriteConstraint(stream, child); break;
            case BaseTextCandidateConstraint.IsMissing missing: stream.WriteByte(5); WriteField(stream, missing.Field); break;
            case BaseTextCandidateConstraint.IsNull nil: stream.WriteByte(6); WriteField(stream, nil.Field); break;
            case BaseTextCandidateConstraint.Equal equal: stream.WriteByte(7); WriteField(stream, equal.Field); WriteValue(stream, equal.Value); break;
            case BaseTextCandidateConstraint.In inside: stream.WriteByte(8); WriteField(stream, inside.Field); WriteCount(stream, inside.Values.Length); foreach (var item in inside.Values) WriteValue(stream, item); break;
            default: throw new InvalidOperationException(BaseTextErrorCodes.QueryInvalid);
        }
    }
    private static void WriteField(Stream stream, BaseTextFilterField field) { WriteString(stream, field.StableFieldId); stream.WriteByte(checked((byte)((int)field.ValueKind + 1))); }
    private static void WriteValue(Stream stream, BaseTextFilterValue value) { stream.WriteByte(checked((byte)((int)value.Kind + 1))); switch (value.Kind) { case BaseTextFilterValueKind.String: case BaseTextFilterValueKind.Id: WriteString(stream, value.StringValue!); break; case BaseTextFilterValueKind.Boolean: stream.WriteByte(value.BooleanValue == true ? (byte)1 : (byte)0); break; case BaseTextFilterValueKind.Integer: WriteLong(stream, value.IntegerValue!.Value); break; } }
    private static void WriteString(Stream stream, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); WriteCount(stream, bytes.Length); stream.Write(bytes); }
    private static void WriteCount(Stream stream, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value)); stream.Write(bytes); }
    private static void WriteLong(Stream stream, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
    private sealed record Eval(bool Matched, Dictionary<string, BaseTextFeatureEvidence> Features);
    private sealed class ByteComparer : IComparer<byte[]> { internal static readonly ByteComparer Instance = new(); public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y); }
    private sealed class ByteStringComparer : IComparer<string> { internal static readonly ByteStringComparer Instance = new(); public int Compare(string? x, string? y) => Encoding.UTF8.GetBytes(x!).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(y!)); }
}
