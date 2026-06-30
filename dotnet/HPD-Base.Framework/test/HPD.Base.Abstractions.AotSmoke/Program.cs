using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Base;
using HPD.Base.Abstractions.AotSmoke;
using HPD.Base.Descriptors;
using HPD.Base.Events;
using HPD.Base.Health;
using HPD.Base.Policy;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Serialization;

var options = new JsonSerializerOptions(HPDBaseJsonSerializerContext.Default.Options)
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        HPDBaseJsonSerializerContext.Default,
        SampleAppJsonSerializerContext.Default)
};
options.MakeReadOnly();

ProvePrimitiveStringShape();
RoundTrip(CreateManifest(), HPDBaseJsonSerializerContext.Default.BaseManifest, options);
RoundTrip(CreateCapabilityDescriptor(), HPDBaseJsonSerializerContext.Default.CapabilityDescriptor, options);
RoundTrip(CreateSchema(), HPDBaseJsonSerializerContext.Default.SchemaMetadata, options);
RoundTrip(CreateQuery(), HPDBaseJsonSerializerContext.Default.RecordQuery, options);
RoundTrip(CreateRecordPage(), HPDBaseJsonSerializerContext.Default.RecordPage, options);
RoundTrip(CreateRecordEnvelope(), HPDBaseJsonSerializerContext.Default.RecordEnvelope, options);
RoundTrip(CreateValidationError(), HPDBaseJsonSerializerContext.Default.BaseError, options);
RoundTrip(CreateEvent(), HPDBaseJsonSerializerContext.Default.BaseRecordMutationEvent, options);
RoundTrip(new[] { CreateHealth() }, HPDBaseJsonSerializerContext.Default.HealthDescriptorArray, options);
RoundTrip(new[] { CreateDiagnostic() }, HPDBaseJsonSerializerContext.Default.DiagnosticDescriptorArray, options);
RoundTrip(CreateTypedEnvelope(), SampleAppJsonSerializerContext.Default.RecordEnvelopeSampleAppPayload, options);

static void RoundTrip<T>(T value, JsonTypeInfo<T> typeInfo, JsonSerializerOptions options)
{
    var json = JsonSerializer.Serialize(value, typeInfo);
    var roundTrip = JsonSerializer.Deserialize(json, typeInfo);

    if (roundTrip is null)
    {
        throw new InvalidOperationException($"Round trip failed for {typeof(T).FullName}.");
    }

    _ = options;
}

static void ProvePrimitiveStringShape()
{
    const string recordJson = "\"rec_aot\"";
    const string revisionJson = "\"rev_aot\"";

    var recordIdJson = JsonSerializer.Serialize(new RecordId("rec_aot"), HPDBaseJsonSerializerContext.Default.RecordId);
    var revisionTokenJson = JsonSerializer.Serialize(new RevisionToken("rev_aot"), HPDBaseJsonSerializerContext.Default.RevisionToken);

    if (recordIdJson != recordJson || revisionTokenJson != revisionJson)
    {
        throw new InvalidOperationException("BASE primitives must serialize as JSON strings.");
    }

    var recordId = JsonSerializer.Deserialize(recordJson, HPDBaseJsonSerializerContext.Default.RecordId);
    var revisionToken = JsonSerializer.Deserialize(revisionJson, HPDBaseJsonSerializerContext.Default.RevisionToken);

    if (recordId.Value != "rec_aot" || revisionToken.Value != "rev_aot")
    {
        throw new InvalidOperationException("BASE primitives must round trip from JSON strings.");
    }
}

static BaseManifest CreateManifest() => new()
{
    ManifestVersion = "1.0",
    ContractVersion = "1.0",
    Runtime = new RuntimeDescriptor { Id = "runtime", Name = "AOT Smoke", Mode = RuntimeMode.Test },
    Compatibility = new CompatibilityDescriptor
    {
        BaseContractVersion = "1.0",
        MinClientContractVersion = "1.0",
        MaxClientContractVersion = "1.0"
    },
    Visibility = VisibilityLevel.Public,
    GeneratedAt = DateTimeOffset.UnixEpoch,
    Links =
    [
        new ManifestLinkDescriptor
        {
            Rel = ManifestLinkKind.Schema,
            Href = "/base/schema",
            ResponseDtoId = BaseDtoIds.SchemaMetadata
        }
    ]
};

static CapabilityDescriptor CreateCapabilityDescriptor() => new()
{
    DescriptorVersion = "1.0",
    RuntimeId = "runtime",
    Families =
    [
        new CapabilityFamilyDescriptor
        {
            FamilyId = BaseCapabilityFamilies.Store,
            FamilyVersion = "1.0",
            Status = CapabilityStatus.Available,
            Features =
            [
                new CapabilityFeatureDescriptor
                {
                    FeatureId = BaseFeatureIds.RecordsGet,
                    Version = "1.0",
                    Status = CapabilityStatus.Available,
                    SupportLevel = SupportLevel.Required,
                    Scope = CapabilityScope.Collection
                }
            ]
        }
    ]
};

static SchemaMetadata CreateSchema() => new()
{
    RuntimeId = "runtime",
    ContractVersion = "1.0",
    Visibility = VisibilityLevel.Public,
    Collections =
    [
        new CollectionDefinition
        {
            Id = "items",
            Name = "items",
            Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Loose,
            UnknownFields = UnknownFieldPolicy.Preserve,
            Fields =
            [
                new FieldDefinition
                {
                    Id = "title",
                    Name = "title",
                    Type = BaseFieldTypes.String,
                    Required = true,
                    Nullable = false
                }
            ]
        }
    ]
};

static RecordQuery CreateQuery() => new()
{
    Filter = new FilterExpression
    {
        Kind = FilterNodeKind.And,
        Children =
        [
            new FilterExpression
            {
                Kind = FilterNodeKind.Compare,
                Field = "title",
                Operator = FilterOperator.Equal,
                Value = new QueryValue { Kind = QueryValueKind.String, String = "hello" }
            }
        ]
    },
    Count = QueryCountMode.IfAvailable
};

static RecordEnvelope CreateRecordEnvelope() => new()
{
    CollectionId = "items",
    Id = new RecordId("rec_1"),
    Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = new Dictionary<string, JsonElement>() },
    Metadata = new RecordMetadata { Revision = new RevisionToken("rev_1") }
};

static RecordPage CreateRecordPage() => new()
{
    Items = [CreateRecordEnvelope()],
    Page = new PageInfo { Limit = 1, HasMore = false }
};

static BaseError CreateValidationError() => new()
{
    Code = BaseErrorCodes.ValidationFailed,
    Message = "Validation failed.",
    Category = ErrorCategory.Validation,
    Validation =
    [
        new ValidationIssue { Path = "title", Code = "required", Message = "Title is required." }
    ]
};

static BaseRecordMutationEvent CreateEvent() => new()
{
    EventId = "evt_1",
    Type = BaseEventTypes.RecordCreated,
    SchemaVersion = BaseEventSchemaVersions.V1,
    Resource = new EventResource { Kind = EventResourceKind.Record, CollectionId = "items", RecordId = new RecordId("rec_1") },
    Operation = BaseOperationKind.Create,
    Timestamp = DateTimeOffset.UnixEpoch,
    Visibility = VisibilityLevel.Public,
    Principal = new EventPrincipalSummary
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectKind = AccessSubjectKind.System
    }
};

static HealthDescriptor CreateHealth() => new()
{
    Id = "runtime",
    Scope = HealthScope.Runtime,
    Status = HealthStatus.Healthy,
    CheckedAt = DateTimeOffset.UnixEpoch,
    PublicSafe = true,
    Visibility = VisibilityLevel.Public
};

static DiagnosticDescriptor CreateDiagnostic() => new()
{
    Id = "diag_1",
    Code = "base.ok",
    Severity = DiagnosticSeverity.Info,
    Message = "OK",
    Category = DiagnosticCategory.Health,
    Visibility = VisibilityLevel.Admin,
    EmittedAt = DateTimeOffset.UnixEpoch
};

static RecordEnvelope<SampleAppPayload> CreateTypedEnvelope() => new()
{
    CollectionId = "items",
    Id = new RecordId("rec_2"),
    Payload = new SampleAppPayload { Title = "typed", Priority = 1 },
    Metadata = new RecordMetadata()
};
