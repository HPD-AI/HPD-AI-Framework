using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

EnumAotRecord value = new() { State = EnumAotState.Active };
byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(value, EnumAotJsonContext.Default.EnumAotRecord);
if (!encoded.AsSpan().SequenceEqual("{\"state\":\"active-wire\"}"u8))
    throw new InvalidOperationException("The generated enum wire value is not exact.");
if (JsonSerializer.Deserialize(encoded, EnumAotJsonContext.Default.EnumAotRecord)?.State != EnumAotState.Active)
    throw new InvalidOperationException("The generated enum wire value did not round-trip.");
foreach (string hostile in new[] { "{\"state\":0}", "{\"state\":\"ACTIVE-WIRE\"}", "{\"state\":\"Active\"}", "{\"state\":\"unknown\"}" })
{
    try
    {
        _ = JsonSerializer.Deserialize(hostile, EnumAotJsonContext.Default.EnumAotRecord);
        throw new InvalidOperationException("A hostile enum wire value was admitted.");
    }
    catch (JsonException) { }
}

_ = EnumAotRecord.Collection.Definition;

var typedId = new RecordIdAotDocument
{
    OwnerId = BaseRecordId<RecordIdAotOwner>.Create("owner-1"),
};
byte[] typedIdJson = JsonSerializer.SerializeToUtf8Bytes(
    typedId, EnumAotJsonContext.Default.RecordIdAotDocument);
if (!typedIdJson.AsSpan().SequenceEqual("{\"ownerId\":\"owner-1\"}"u8))
    throw new InvalidOperationException("The typed record-id wire value is not exact.");
if (JsonSerializer.Deserialize(typedIdJson, EnumAotJsonContext.Default.RecordIdAotDocument)?.OwnerId != typedId.OwnerId)
    throw new InvalidOperationException("The typed record-id wire value did not round-trip.");
try
{
    _ = JsonSerializer.SerializeToUtf8Bytes(
        new RecordIdAotDocument { OwnerId = default },
        EnumAotJsonContext.Default.RecordIdAotDocument);
    throw new InvalidOperationException("A default typed record ID was serialized.");
}
catch (JsonException exception) when (exception.Message.StartsWith(
    "RecordId must be a canonical JSON string.", StringComparison.Ordinal))
{
}

_ = RecordIdAotDocument.Collection.Definition;

var canonicalLimits = new BaseCanonicalJsonLimits
{
    MaximumCanonicalBytes = 128, MaximumDepth = 4, MaximumArrayItemsPerContainer = 8,
    MaximumObjectPropertiesPerContainer = 8, MaximumTotalNodes = 16,
    MaximumTotalStringUtf8Bytes = 128, MaximumTotalNameUtf8Bytes = 128,
};
BaseCanonicalJson canonical = BaseCanonicalJson.ParseAndValidate("{\"enabled\":true}"u8, canonicalLimits);
QueryValue queryValue = new()
{
    Kind = QueryValueKind.CanonicalJson,
    CanonicalJsonUtf8 = ImmutableArray.Create(canonical.Utf8.Span.ToArray()),
};
byte[] queryJson = JsonSerializer.SerializeToUtf8Bytes(queryValue, HPDBaseJsonSerializerContext.Default.QueryValue);
if (!System.Text.Encoding.UTF8.GetString(queryJson).Contains("\"canonicalJsonUtf8\":\"eyJlbmFibGVkIjp0cnVlfQ==\"", StringComparison.Ordinal))
    throw new InvalidOperationException("The canonical-JSON provider protocol was not exact under Native AOT.");
_ = CanonicalJsonAotRead.Definition;

var services = new ServiceCollection();
services.AddLogging();
services.AddHPDBase(builder =>
{
    builder.AddPolicyAuthority<AotAllowPolicy>(new BasePolicyAuthorityDefinition
    {
        Id = "aot.canonical-json.policy", Version = 1, OwningModuleId = "aot.canonical-json",
        EvaluatorContractId = "aot.canonical-json.policy.v1", EvaluatorContractVersion = 1,
        CompositionOrder = 0,
    });
    builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
    {
        Id = "aot.canonical-json.read", Version = 1, OwningModuleId = "aot.canonical-json",
        SourceContractId = "aot.canonical-json.grants", SourceContractVersion = 1,
    }, new AccessGrant
    {
        Id = "aot.canonical-json.read", ApplicationId = "hpd.base.application",
        Audience = HPDBaseEndpointAudience.Application,
        Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "canonical-json-aot" },
        Action = "aot.canonical-json.read",
        Scope = new ResourceScope { Kind = ResourceScopeKind.Collection, CollectionId = CanonicalJsonAotRecord.Collection.Id },
    });
    builder.AddCollection(CanonicalJsonAotRecord.Collection)
        .AddRead(CanonicalJsonAotRead.Definition)
        .AddRead(CompoundAotRead.Definition);
});
await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
if (!(await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess())
    throw new InvalidOperationException("Canonical-JSON Native AOT application initialization failed.");
BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.System,
    SubjectKind = AccessSubjectKind.System,
    SubjectId = "canonical-json-aot",
});
(await session.Collection(CanonicalJsonAotRecord.Collection).CreateAsync(
    RecordId.Create("settings"), new CanonicalJsonAotRecord { Settings = canonical })).RequireValue();
CanonicalJsonAotRead.Row[] canonicalRows = (await session.Reads.ToArrayAsync(
    CanonicalJsonAotRead.Handle, new CanonicalJsonAotRead { Settings = canonical })).RequireValue();
if (canonicalRows is not [{ Settings: var returned }] || returned != canonical)
    throw new InvalidOperationException("Canonical-JSON registered-read provider execution failed under Native AOT.");
CompoundAotRead.Row[] compoundRows = (await session.Reads.ToArrayAsync(
    CompoundAotRead.Handle, new CompoundAotRead())).RequireValue();
if (compoundRows is not [{ Kind: "first", Count: 1 }, { Kind: "second", Count: 1 }])
    throw new InvalidOperationException("Compound registered-read execution failed under Native AOT.");

internal enum EnumAotState
{
    [JsonStringEnumMemberName("active-wire")] Active,
    [JsonStringEnumMemberName("disabled-wire")] Disabled,
}

[BaseCollection("aot.enum-records", typeof(EnumAotJsonContext))]
internal sealed partial record EnumAotRecord
{
    [BaseField("state", AllowedEnumLiterals = ["active-wire", "disabled-wire"])]
    [JsonPropertyName("state")]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<EnumAotState>))]
    public required EnumAotState State { get; init; }
}

[BaseCollection("aot.record-id-owners", typeof(EnumAotJsonContext))]
internal sealed partial record RecordIdAotOwner
{
    [BaseField("name"), JsonPropertyName("name")]
    public required string Name { get; init; }
}

[BaseCollection("aot.record-id-documents", typeof(EnumAotJsonContext))]
internal sealed partial record RecordIdAotDocument
{
    [BaseRelation("aot.record-id-document.owner", typeof(RecordIdAotOwner),
        LocalMultiplicity = BaseRelationMultiplicity.ExactlyOne,
        InverseNavigationId = "aot.record-id-owner.documents")]
    [BaseField("ownerId"), JsonPropertyName("ownerId")]
    public required BaseRecordId<RecordIdAotOwner> OwnerId { get; init; }
}

[BaseCollection("aot.canonical-json", typeof(EnumAotJsonContext))]
internal sealed partial record CanonicalJsonAotRecord
{
    [BaseField("settings", MaximumCanonicalJsonBytes = 128, JsonShape = BaseJsonShape.Object,
        MaximumJsonDepth = 4, MaximumJsonArrayItems = 8, MaximumJsonObjectProperties = 8,
        MaximumJsonTotalNodes = 16, MaximumJsonTotalStringUtf8Bytes = 128,
        MaximumJsonTotalNameUtf8Bytes = 128)]
    public required BaseCanonicalJson Settings { get; init; }
}

[BaseRead("aot.canonical-json.read", typeof(EnumAotJsonContext), RequiredGrantId = "aot.canonical-json.read")]
internal sealed partial record CanonicalJsonAotRead
{
    [BaseReadParameter("settings")] public required BaseCanonicalJson Settings { get; init; }
    public sealed partial record Row
    {
        [BaseReadField("settings")] public required BaseCanonicalJson Settings { get; init; }
    }
    public static void Configure(BaseReadDefinitionBuilder<CanonicalJsonAotRead, Row> read) =>
        read.From(CanonicalJsonAotRecord.Collection, "record", out BaseReadSource<CanonicalJsonAotRecord> record)
            .BindCanonicalJsonParameter(Parameters.Settings, CanonicalJsonAotRecord.Fields.Settings)
            .Where(record.Field(CanonicalJsonAotRecord.Fields.Settings).Equal(read.Parameter(Parameters.Settings)))
            .Project(Row.Fields.Settings, record.Field(CanonicalJsonAotRecord.Fields.Settings));
}

[BaseRead("aot.compound.read", typeof(EnumAotJsonContext), RequiredGrantId = "aot.canonical-json.read")]
internal sealed partial record CompoundAotRead
{
    public sealed partial record Row
    {
        [BaseReadField("kind")] public required string Kind { get; init; }
        [BaseReadField("count")] public required long Count { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<CompoundAotRead, Row> read) => read
        .CountBranch("first-branch", Row.Fields.Kind, "first", CanonicalJsonAotRecord.Collection, Row.Fields.Count, static _ => { })
        .CountBranch("second-branch", Row.Fields.Kind, "second", CanonicalJsonAotRecord.Collection, Row.Fields.Count, static _ => { })
        .CompoundLimits(4_096, 8, 2_000, 2, 8);
}

[JsonSerializable(typeof(EnumAotRecord))]
[JsonSerializable(typeof(RecordIdAotOwner))]
[JsonSerializable(typeof(RecordIdAotDocument))]
[JsonSerializable(typeof(CanonicalJsonAotRecord))]
[JsonSerializable(typeof(CanonicalJsonAotRead))]
[JsonSerializable(typeof(CanonicalJsonAotRead.Row), TypeInfoPropertyName = "CanonicalJsonAotReadRow")]
[JsonSerializable(typeof(CompoundAotRead))]
[JsonSerializable(typeof(CompoundAotRead.Row), TypeInfoPropertyName = "CompoundAotReadRow")]
internal sealed partial class EnumAotJsonContext : JsonSerializerContext;

internal sealed class AotAllowPolicy : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}
