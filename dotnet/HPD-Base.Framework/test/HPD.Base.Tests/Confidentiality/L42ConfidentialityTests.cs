using System.Text.Json;

namespace HPD.Base.Tests.Confidentiality;

public sealed class L42ConfidentialityTests
{
    [Fact]
    public void BinaryOwnsBytesAndRequiresCanonicalBase64()
    {
        byte[] source = [1, 2, 3];
        BaseBinary value = BaseBinary.From(source);
        source[0] = 9;
        byte[] returned = value.ToArray();
        returned[1] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, value.ToArray());
        Assert.Equal(value, BaseBinary.FromBase64("AQID"));
        Assert.Throws<FormatException>(() => BaseBinary.FromBase64("AQID\n"));
        Assert.Throws<FormatException>(() => BaseBinary.FromBase64("AQI"));
    }

    [Fact]
    public async Task MutationValidationRejectsOversizedBinaryAndStructuralMarkerInput()
    {
        CollectionDefinition collection = Collection(
            Field("blob", BaseFieldConfidentiality.Secret, BaseRecordDisclosure.Omit) with
            { Type = "string", Format = "base64", MaximumBytes = 2 });
        var validator = new DefaultBaseSchemaValidator();

        OperationResult<BaseValidatedPayload> oversized = await validator.ValidateCreateAsync(Request(collection, "{\"blob\":\"AQID\"}"));
        OperationResult<BaseValidatedPayload> marker = await validator.ValidateCreateAsync(Request(collection, "{\"blob\":{\"$base\":\"redacted\"}}"));

        Assert.Equal(BaseBinaryErrorCodes.ValueTooLarge, oversized.Error?.Code);
        Assert.Equal(BaseConfidentialityErrorCodes.RedactedMarkerForbidden, marker.Error?.Code);
    }

    [Fact]
    public void RecordProjectionUsesExactOmissionAndStructuralMarker()
    {
        CollectionDefinition collection = Collection(
            Field("public", BaseFieldConfidentiality.Public, BaseRecordDisclosure.Include),
            Field("confidential", BaseFieldConfidentiality.Confidential, BaseRecordDisclosure.Omit),
            Field("secret", BaseFieldConfidentiality.Internal, BaseRecordDisclosure.FixedMarker));
        RecordEnvelope record = new()
        {
            CollectionId = collection.Id,
            Id = RecordId.Create("record-1"),
            Payload = Payload("{\"public\":\"shown\",\"confidential\":\"hidden\",\"secret\":\"never\"}"),
            Metadata = new RecordMetadata { Revision = new RevisionToken("r1") },
        };
        var policy = new BasePolicyEvaluation { Decision = PolicyDecision.Allow() };

        RecordEnvelope result = new DefaultBaseRecordRedactor().RedactRecord(record, collection, policy, VisibilityLevel.Authenticated);
        JsonElement json = result.Payload.Json;

        Assert.Equal("shown", json.GetProperty("public").GetString());
        Assert.False(json.TryGetProperty("confidential", out _));
        Assert.Equal("{\"$base\":\"redacted\"}", json.GetProperty("secret").GetRawText());
        Assert.DoesNotContain("never", json.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SystemGateRejectsAdministratorsAndAcceptsBoundServiceSubjects()
    {
        Assert.False(BaseSystemCollectionGate.Allows(new PrincipalContext
        { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User }));
        Assert.True(BaseSystemCollectionGate.Allows(new PrincipalContext
        { AuthenticationState = PrincipalAuthenticationState.Service, SubjectKind = AccessSubjectKind.ServicePrincipal }));
    }

    private static BasePayloadValidationRequest Request(CollectionDefinition collection, string json) => new()
    {
        Collection = collection,
        Principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated },
        Operation = new OperationContext { Operation = BaseOperationKind.Create, CollectionId = collection.Id },
        Payload = Payload(json),
    };

    private static RecordPayload Payload(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }

    private static CollectionDefinition Collection(params FieldDefinition[] fields) => new()
    {
        Id = "confidential-records", Name = "confidential-records", Kind = "record",
        SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, Fields = fields,
    };

    private static FieldDefinition Field(string name, BaseFieldConfidentiality confidentiality, BaseRecordDisclosure recordRead)
    {
        BaseFieldDisclosurePolicy policy = BaseFieldDisclosurePolicies.For(confidentiality) with { RecordRead = recordRead };
        return new FieldDefinition
        {
            Id = "field." + name, Name = name, Type = "string", Required = true,
            Confidentiality = confidentiality, Disclosure = policy,
        };
    }
}
