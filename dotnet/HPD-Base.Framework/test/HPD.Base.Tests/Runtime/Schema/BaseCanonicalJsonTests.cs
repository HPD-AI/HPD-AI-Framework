using System.Text;
using HPD.Base;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Immutable;

namespace HPD.Base.Tests.Schema;

public sealed class BaseCanonicalJsonTests
{
    private static readonly BaseCanonicalJsonLimits Limits = new()
    {
        MaximumCanonicalBytes = 1024, MaximumDepth = 8, MaximumTotalNodes = 64,
        MaximumTotalStringUtf8Bytes = 256, MaximumTotalNameUtf8Bytes = 256,
        MaximumArrayItemsPerContainer = 16, MaximumObjectPropertiesPerContainer = 16
    };

    [Theory]
    [InlineData("null")]
    [InlineData("{\"a\":1,\"b\":[true,\"é\"]}")]
    [InlineData("[0,-1,0.01,1.2]")]
    [InlineData("-170141183460469231731687303715884105728")]
    [InlineData("\"\\n\\u0000/é\"")]
    public void AdmitsExactCanonicalVectors(string json)
    {
        BaseCanonicalJson value = BaseCanonicalJson.ParseAndValidate(Encoding.UTF8.GetBytes(json), Limits);
        Assert.Equal(json, Encoding.UTF8.GetString(value.Utf8.Span));
        Assert.True(value.Checksum.IsValid);
    }

    [Theory]
    [InlineData("{\"b\":1,\"a\":2}")]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("1.20")]
    [InlineData("1e0")]
    [InlineData("-0")]
    [InlineData("\"\\u00e9\"")]
    [InlineData("-170141183460469231731687303715884105729")]
    public void RejectsNoncanonicalOrAmbiguousVectors(string json) =>
        Assert.ThrowsAny<FormatException>(() => BaseCanonicalJson.ParseAndValidate(Encoding.UTF8.GetBytes(json), Limits));

    [Fact]
    public void OwnsInputAndReturnedBytes()
    {
        byte[] input = "{\"a\":1}"u8.ToArray();
        BaseCanonicalJson value = BaseCanonicalJson.ParseAndValidate(input, Limits);
        input[0] = (byte)'[';
        byte[] copy = value.Utf8.ToArray(); copy[0] = (byte)'[';
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(value.Utf8.Span));
    }

    [Fact]
    public void Canonicalizer_enforces_every_independent_limit_at_maximum_plus_one()
    {
        BaseCanonicalJsonLimits limits = new()
        {
            MaximumCanonicalBytes = 32, MaximumDepth = 2, MaximumTotalNodes = 5,
            MaximumTotalStringUtf8Bytes = 2, MaximumTotalNameUtf8Bytes = 2,
            MaximumArrayItemsPerContainer = 2, MaximumObjectPropertiesPerContainer = 2,
        };

        Assert.Equal("{\"a\":1,\"b\":2}", Encoding.UTF8.GetString(
            BaseCanonicalJson.Canonicalize("{\"b\":2,\"a\":1}"u8, limits)));
        Assert.Throws<FormatException>(() => BaseCanonicalJson.Canonicalize("[[0]]"u8, limits));
        Assert.Throws<FormatException>(() => BaseCanonicalJson.Canonicalize("[0,1,2]"u8, limits));
        Assert.Throws<FormatException>(() => BaseCanonicalJson.Canonicalize("{\"a\":1,\"b\":2,\"c\":3}"u8, limits));
        Assert.Throws<FormatException>(() => BaseCanonicalJson.Canonicalize("{\"a\":[0,1],\"b\":2}"u8, limits));
        Assert.Throws<FormatException>(() => BaseCanonicalJson.Canonicalize("\"abc\""u8, limits));
        Assert.Throws<FormatException>(() => BaseCanonicalJson.Canonicalize("{\"abc\":1}"u8, limits));
        Assert.Throws<FormatException>(() => BaseCanonicalJson.Canonicalize(new byte[33], limits));
    }

    [Fact]
    public void SourceGeneratedConverterEmbedsCanonicalValueWithoutAnEnvelope()
    {
        BaseCanonicalJson value = BaseCanonicalJson.ParseAndValidate("{\"a\":1}"u8, Limits);
        byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(value, CanonicalJsonTestContext.Default.BaseCanonicalJson);
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(encoded));
        Assert.Equal(value, JsonSerializer.Deserialize(encoded, CanonicalJsonTestContext.Default.BaseCanonicalJson));
    }

    [Fact]
    public void QueryValueProtocolUsesOneCanonicalPaddedBase64Spelling()
    {
        QueryValue value = new()
        {
            Kind = QueryValueKind.CanonicalJson,
            CanonicalJsonUtf8 = ImmutableArray.Create("{\"a\":1}"u8.ToArray()),
        };
        byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(value, HPDBaseJsonSerializerContext.Default.QueryValue);
        string json = Encoding.UTF8.GetString(encoded);
        Assert.Contains("\"canonicalJsonUtf8\":\"eyJhIjoxfQ==\"", json, StringComparison.Ordinal);
        QueryValue decoded = JsonSerializer.Deserialize(encoded, HPDBaseJsonSerializerContext.Default.QueryValue)!;
        Assert.True(decoded.CanonicalJsonUtf8.AsSpan().SequenceEqual("{\"a\":1}"u8));

        byte[] missingPadding = Encoding.UTF8.GetBytes(json.Replace("eyJhIjoxfQ==", "eyJhIjoxfQ", StringComparison.Ordinal));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(missingPadding, HPDBaseJsonSerializerContext.Default.QueryValue));
    }

    [Theory]
    [InlineData(null, "76fdc0cb498fb680ba9697f90d1ff72270a776730beb689f1b2b8a5c89b419df")]
    [InlineData("null", "4443abd8553872a114eec8682742e5dcf20a1e78b2363ed0a6bcad631c8b8f62")]
    [InlineData("\"A\"", "fac380cd7081f1999ee68664f509a19896ad4739f5cc19bce665d8f8b149871b")]
    public void LogicalEqualityKeysMatchTheNormativeL54SeedVectors(string? json, string checksum)
    {
        BaseScalarCodecAuthority codec = new()
        {
            Id = BaseScalarCodecId.Create("s"), Version = 1, Kind = BaseScalarKind.String, AllowedConstraints = [],
            CodecChecksum = BaseSchemaAuthorityChecksum.Create(new byte[32]), EqualityVersion = 1,
            EqualityChecksum = BaseSchemaAuthorityChecksum.Create(new byte[32]), OrderingVersion = 1,
            OrderingChecksum = BaseSchemaAuthorityChecksum.Create(new byte[32]),
        };
        FieldDefinition field = new() { Id = "f", ApplicationName = "Value", WireName = "value", Type = "string", ScalarKind = BaseScalarKind.String, ScalarCodec = codec };
        CollectionDefinition collection = new() { Id = "c", Name = "c", Kind = "record", SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, Fields = [field] };
        BaseLogicalIndexDefinition index = new()
        {
            Id = BaseLogicalIndexId.Create("i"), Version = 1, CollectionId = "c", Unique = true, StoreRequired = true,
            Parts = [new BaseLogicalIndexPart { FieldOrdinal = 0, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue }],
            MembershipPredicate = new BaseIndexPredicateRegistry { Root = BaseIndexPredicateId.Create("root"), Nodes = [new BaseIndexPredicateNode { Id = BaseIndexPredicateId.Create("root"), Kind = BaseIndexPredicateNodeKind.True }], Checksum = default },
            Checksum = default,
        };
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (json is not null) { using JsonDocument document = JsonDocument.Parse(json); fields["value"] = document.RootElement.Clone(); }
        byte[] key = BaseLogicalIndexEvaluator.Key(collection, index, new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields });
        Assert.Equal(checksum, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(key)));
    }

    [Fact]
    public void LogicalComparatorUsesExactDecimalStateDirectionAndRecordIdOrder()
    {
        BaseScalarCodecAuthority codec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.Decimal);
        FieldDefinition field = new() { Id = "f", ApplicationName = "Value", WireName = "value", Type = "number", Format = "decimal", ScalarKind = BaseScalarKind.Decimal, ScalarCodec = codec, ScalarConstraints = new() };
        CollectionDefinition collection = new() { Id = "c", Name = "c", Kind = "record", SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, Fields = [field] };
        BaseLogicalIndexDefinition index = new()
        {
            Id = BaseLogicalIndexId.Create("i"), Version = 1, CollectionId = "c", Unique = false, StoreRequired = true,
            Parts = [new BaseLogicalIndexPart { FieldOrdinal = 0, Direction = BaseIndexSortDirection.Ascending, Collation = BaseIndexCollation.OrdinalBinary, NullOrder = BaseIndexNullOrder.MissingThenNullThenValue }],
            MembershipPredicate = new BaseIndexPredicateRegistry { Root = BaseIndexPredicateId.Create("root"), Nodes = [new BaseIndexPredicateNode { Id = BaseIndexPredicateId.Create("root"), Kind = BaseIndexPredicateNodeKind.True }], Checksum = default }, Checksum = default,
        };
        var values = new List<(RecordId Id, RecordPayload Payload)>
        {
            (RecordId.Create("same-b"), Payload("1")), (RecordId.Create("null"), Payload("null")), (RecordId.Create("negative"), Payload("-170141183460469231731687303715884105728")),
            (RecordId.Create("missing"), Payload(null)), (RecordId.Create("same-a"), Payload("1")), (RecordId.Create("positive"), Payload("170141183460469231731687303715884105727")),
        };

        values.Sort((left, right) => BaseLogicalIndexEvaluator.Compare(collection, index, left.Payload, left.Id, right.Payload, right.Id));

        Assert.Equal(["missing", "null", "negative", "same-a", "same-b", "positive"], values.Select(static value => value.Id.Value));
    }

    [Theory]
    [InlineData("é", true)]
    [InlineData("e\u0301", false)]
    [InlineData("Å\u0327", true)]
    [InlineData("A\u0327\u030A", false)]
    [InlineData("각", true)]
    [InlineData("각", false)]
    public void PinnedUnicode17NfcDoesNotUseHostNormalizationTables(string value, bool expected) =>
        Assert.Equal(expected, BaseUnicode17Nfc.IsNormalized(value));

    [Fact]
    public void PinnedUnicode17NfcRejectsMalformedUtf16() =>
        Assert.Throws<FormatException>(() => BaseUnicode17Nfc.IsNormalized("\ud800"));

    [Fact]
    public void BaseOwnedUtcConverterWritesAndReadsOnlyTheExactZWireForm()
    {
        var value = new CanonicalUtcRecord { At = new DateTimeOffset(2026, 8, 24, 12, 34, 56, TimeSpan.Zero).AddTicks(1234567) };
        string json = JsonSerializer.Serialize(value, CanonicalJsonTestContext.Default.CanonicalUtcRecord);
        Assert.Contains("2026-08-24T12:34:56.1234567Z", json, StringComparison.Ordinal);
        Assert.Equal(value, JsonSerializer.Deserialize(json, CanonicalJsonTestContext.Default.CanonicalUtcRecord));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"At\":\"2026-08-24T12:34:56.1234567+00:00\"}", CanonicalJsonTestContext.Default.CanonicalUtcRecord));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new CanonicalUtcRecord { At = value.At.ToOffset(TimeSpan.FromHours(1)) }, CanonicalJsonTestContext.Default.CanonicalUtcRecord));
    }

    private static RecordPayload Payload(string? json)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (json is not null) { using JsonDocument document = JsonDocument.Parse(json); fields["value"] = document.RootElement.Clone(); }
        return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
    }
}

[JsonSerializable(typeof(BaseCanonicalJson))]
[JsonSerializable(typeof(CanonicalUtcRecord))]
internal sealed partial class CanonicalJsonTestContext : JsonSerializerContext;

internal sealed record CanonicalUtcRecord
{
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset At { get; init; }
}
