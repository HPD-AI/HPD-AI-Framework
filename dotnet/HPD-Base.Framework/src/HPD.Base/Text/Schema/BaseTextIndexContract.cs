using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Owns validation, defensive copying, and identity for text-index definitions.</summary>
public static class BaseTextIndexContract
{
    /// <summary>Validates, deeply owns, and checksums one definition.</summary>
    public static BaseTextIndexDefinition Seal(BaseTextIndexDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Validate(value);
        BaseTextIndexDefinition owned = value with
        {
            Id = Copy(value.Id), CollectionId = Copy(value.CollectionId),
            AnalyzerContractId = Copy(value.AnalyzerContractId), ScoringContractId = Copy(value.ScoringContractId),
            AnalyzerReceipt = Copy(value.AnalyzerReceipt), ScoringReceipt = Copy(value.ScoringReceipt),
            SerializerGraphChecksum = Copy(value.SerializerGraphChecksum),
            Fields = value.Fields.Select(Clone).ToImmutableArray(),
            FilterFields = value.FilterFields.Select(Clone).ToImmutableArray(),
            Limits = value.Limits with { },
            DefinitionChecksum = [],
        };
        ImmutableArray<byte> checksum = ImmutableArray.Create(SHA256.HashData(Encode(owned)));
        if (!value.DefinitionChecksum.IsDefaultOrEmpty && !CryptographicOperations.FixedTimeEquals(value.DefinitionChecksum.AsSpan(), checksum.AsSpan()))
            throw new InvalidOperationException(BaseTextErrorCodes.ContractInvalid);
        return owned with { DefinitionChecksum = checksum };
    }

    /// <summary>Returns the canonical definition bytes excluding its resulting checksum.</summary>
    public static byte[] Encode(BaseTextIndexDefinition value)
    {
        using var stream = new MemoryStream();
        stream.Write("HPDB-TEXT-INDEX-1\0"u8);
        String(stream, value.Id); Integer(stream, value.Version); String(stream, value.CollectionId); Integer(stream, (int)value.Audience);
        Count(stream, value.Fields.Length);
        foreach (BaseTextIndexFieldDefinition field in value.Fields)
        {
            String(stream, field.StableFieldId); String(stream, field.ApplicationName); String(stream, field.WireName); Integer(stream, field.Weight);
            Integer(stream, (int)field.Confidentiality); Count(stream, field.StaticInfluenceAudiences.Length);
            foreach (HPDBaseEndpointAudience audience in field.StaticInfluenceAudiences) Integer(stream, (int)audience);
            stream.WriteByte(field.RequiresDynamicInfluenceConstraint ? (byte)1 : (byte)0);
        }
        Count(stream, value.FilterFields.Length);
        foreach (BaseTextIndexFilterFieldDefinition field in value.FilterFields)
        { String(stream, field.StableFieldId); String(stream, field.ApplicationName); String(stream, field.WireName); Integer(stream, (int)field.ValueKind); }
        String(stream, value.AnalyzerContractId); Bytes(stream, value.AnalyzerReceipt); String(stream, value.ScoringContractId); Bytes(stream, value.ScoringReceipt);
        Limits(stream, value.Limits); Bytes(stream, value.SerializerGraphChecksum);
        return stream.ToArray();
    }

    private static void Validate(BaseTextIndexDefinition value)
    {
        Id(value.Id); Id(value.CollectionId);
        if (value.Version <= 0 || !Enum.IsDefined(value.Audience) || value.Fields.Length is < 1 or > 8 || value.FilterFields.Length > 16) throw Invalid();
        if (!string.Equals(value.AnalyzerContractId, BaseTextAnalyzers.UnicodeCaseFoldedV1, StringComparison.Ordinal)
            || !string.Equals(value.ScoringContractId, BaseTextScoring.ContractId, StringComparison.Ordinal)) throw Invalid();
        if (!value.AnalyzerReceipt.AsSpan().SequenceEqual(BaseTextContractReceipts.AnalyzerReceipt.AsSpan())
            || !value.ScoringReceipt.AsSpan().SequenceEqual(BaseTextContractReceipts.ScoringReceipt.AsSpan())
            || value.SerializerGraphChecksum.Length != 32) throw Invalid();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (BaseTextIndexFieldDefinition field in value.Fields)
        {
            Id(field.StableFieldId);
            if (!identities.Add(field.StableFieldId) || string.IsNullOrEmpty(field.ApplicationName) || string.IsNullOrEmpty(field.WireName)
                || field.Weight is < 1 or > 16 || field.Confidentiality is BaseFieldConfidentiality.Confidential or BaseFieldConfidentiality.Secret
                || field.StaticInfluenceAudiences.IsDefaultOrEmpty || field.StaticInfluenceAudiences.Distinct().Count() != field.StaticInfluenceAudiences.Length
                || !field.StaticInfluenceAudiences.Contains(value.Audience)
                || !field.StaticInfluenceAudiences.SequenceEqual(field.StaticInfluenceAudiences.Order())) throw Invalid();
        }
        string? prior = null;
        foreach (BaseTextIndexFilterFieldDefinition field in value.FilterFields)
        {
            Id(field.StableFieldId);
            if (!identities.Add(field.StableFieldId) || string.IsNullOrEmpty(field.ApplicationName) || string.IsNullOrEmpty(field.WireName)
                || !Enum.IsDefined(field.ValueKind) || (prior is not null && StringComparer.Ordinal.Compare(prior, field.StableFieldId) >= 0)) throw Invalid();
            prior = field.StableFieldId;
        }
        ValidateLimits(value.Limits);
    }

    private static void ValidateLimits(BaseTextExecutionLimits value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BaseTextExecutionLimits maximum = BaseTextPlatform.DefaultLimits;
        if (value.MaximumQueryNodes is < 1 || value.MaximumQueryNodes > maximum.MaximumQueryNodes
            || value.MaximumQueryDepth is < 1 || value.MaximumQueryDepth > maximum.MaximumQueryDepth
            || value.MaximumPhraseTerms is < 2 || value.MaximumPhraseTerms > maximum.MaximumPhraseTerms
            || value.MaximumQueryBytes is < 1 || value.MaximumQueryBytes > maximum.MaximumQueryBytes
            || value.MaximumFilterNodes is < 1 || value.MaximumFilterNodes > maximum.MaximumFilterNodes
            || value.MaximumFilterDepth is < 1 || value.MaximumFilterDepth > maximum.MaximumFilterDepth
            || value.MaximumFilterLiterals is < 1 || value.MaximumFilterLiterals > maximum.MaximumFilterLiterals
            || value.MaximumInValues is < 1 || value.MaximumInValues > maximum.MaximumInValues
            || value.MaximumPrefixExpansions is < 1 || value.MaximumPrefixExpansions > maximum.MaximumPrefixExpansions
            || value.MaximumPrefixExpansionBytes is < 1 || value.MaximumPrefixExpansionBytes > maximum.MaximumPrefixExpansionBytes
            || value.MaximumSecondaryOrderFields is < 1 || value.MaximumSecondaryOrderFields > maximum.MaximumSecondaryOrderFields
            || value.MaximumOrderingBytes is < 1 || value.MaximumOrderingBytes > maximum.MaximumOrderingBytes
            || value.MaximumCandidates is < 2 || value.MaximumCandidates > maximum.MaximumCandidates
            || value.MaximumScoreProofBytes is < 1 || value.MaximumScoreProofBytes > maximum.MaximumScoreProofBytes
            || value.MaximumTokensPerField is < 1 || value.MaximumTokensPerField > maximum.MaximumTokensPerField
            || value.MaximumNormalizedBytesPerField is < 1 || value.MaximumNormalizedBytesPerField > maximum.MaximumNormalizedBytesPerField
            || value.MaximumNormalizedBytesPerRecord is < 1 || value.MaximumNormalizedBytesPerRecord > maximum.MaximumNormalizedBytesPerRecord
            || value.MaximumResults is < 1 || value.MaximumResults > maximum.MaximumResults
            || value.MaximumResultBytes is < 1 || value.MaximumResultBytes > maximum.MaximumResultBytes
            || value.MaximumCursorBytes is < 1 || value.MaximumCursorBytes > maximum.MaximumCursorBytes
            || value.MaximumStatementParameters is < 1 || value.MaximumStatementParameters > maximum.MaximumStatementParameters
            || value.MaximumCandidates < checked(value.MaximumResults + 1)
            || value.MaximumTokensPerField > BaseTextAnalyzers.MaximumTokensPerField
            || value.MaximumNormalizedBytesPerField > BaseTextAnalyzers.MaximumNormalizedBytesPerField
            || value.MaximumTransientBytes is < 1 || value.MaximumTransientBytes > maximum.MaximumTransientBytes
            || value.QueryTimeout <= TimeSpan.Zero || value.QueryTimeout > maximum.QueryTimeout
            || value.ConsistencyWaitTimeout <= TimeSpan.Zero || value.ConsistencyWaitTimeout > maximum.ConsistencyWaitTimeout) throw Invalid();
    }

    private static BaseTextIndexFieldDefinition Clone(BaseTextIndexFieldDefinition value) => value with
    { StableFieldId = Copy(value.StableFieldId), ApplicationName = Copy(value.ApplicationName), WireName = Copy(value.WireName), StaticInfluenceAudiences = value.StaticInfluenceAudiences.ToImmutableArray() };
    private static BaseTextIndexFilterFieldDefinition Clone(BaseTextIndexFilterFieldDefinition value) => value with
    { StableFieldId = Copy(value.StableFieldId), ApplicationName = Copy(value.ApplicationName), WireName = Copy(value.WireName) };
    private static ImmutableArray<byte> Copy(ImmutableArray<byte> value) => ImmutableArray.Create(value.ToArray());
    private static string Copy(string value) => new(value.AsSpan());
    private static void Id(string value) { if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 128 || value.Any(static c => c is < '!' or > '~')) throw Invalid(); }
    private static InvalidOperationException Invalid() => new(BaseTextErrorCodes.ContractInvalid);
    private static void String(Stream stream, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); Count(stream, bytes.Length); stream.Write(bytes); }
    private static void Bytes(Stream stream, ImmutableArray<byte> value) { Count(stream, value.Length); stream.Write(value.AsSpan()); }
    private static void Count(Stream stream, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value)); stream.Write(bytes); }
    private static void Integer(Stream stream, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
    private static void Limits(Stream stream, BaseTextExecutionLimits value)
    {
        Integer(stream, value.MaximumQueryNodes); Integer(stream, value.MaximumQueryDepth); Integer(stream, value.MaximumPhraseTerms); Integer(stream, value.MaximumQueryBytes);
        Integer(stream, value.MaximumFilterNodes); Integer(stream, value.MaximumFilterDepth); Integer(stream, value.MaximumFilterLiterals); Integer(stream, value.MaximumInValues);
        Integer(stream, value.MaximumPrefixExpansions); Integer(stream, value.MaximumPrefixExpansionBytes); Integer(stream, value.MaximumSecondaryOrderFields); Integer(stream, value.MaximumOrderingBytes);
        Integer(stream, value.MaximumCandidates); Integer(stream, value.MaximumScoreProofBytes); Integer(stream, value.MaximumTokensPerField); Integer(stream, value.MaximumNormalizedBytesPerField);
        Integer(stream, value.MaximumNormalizedBytesPerRecord); Integer(stream, value.MaximumResults); Integer(stream, value.MaximumResultBytes); Integer(stream, value.MaximumCursorBytes);
        Integer(stream, value.MaximumStatementParameters); Integer(stream, value.MaximumTransientBytes); Integer(stream, value.QueryTimeout.Ticks); Integer(stream, value.ConsistencyWaitTimeout.Ticks);
    }
}
