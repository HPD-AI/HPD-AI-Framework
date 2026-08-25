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
        var policy = new BasePolicyEvaluation { Decision = PolicyDecision.Allow(), EffectiveTextSearchInfluenceFilters = System.Collections.Immutable.ImmutableDictionary<string, FilterExpression>.Empty };

        RecordEnvelope result = new DefaultBaseRecordRedactor().RedactRecord(record, collection, policy, VisibilityLevel.Authenticated);
        JsonElement json = result.Payload.Json;

        Assert.Equal("shown", json.GetProperty("public").GetString());
        Assert.False(json.TryGetProperty("confidential", out _));
        Assert.Equal("{\"$base\":\"redacted\"}", json.GetProperty("secret").GetRawText());
        Assert.DoesNotContain("never", json.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SystemGateRequiresServiceKindAndOneExactNonBypassGrant()
    {
        Assert.False(BaseSystemCollectionGate.Allows(new PrincipalContext
        { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User }));
        Assert.True(BaseSystemCollectionGate.Allows(new PrincipalContext
        { AuthenticationState = PrincipalAuthenticationState.Service, SubjectKind = AccessSubjectKind.ServicePrincipal }));
        Assert.False(BaseSystemCollectionGate.HasExactGrant(OperationResults.Ok(Evaluation())));
        Assert.False(BaseSystemCollectionGate.HasExactGrant(OperationResults.Ok(Evaluation("other.execute")), "system.read.execute"));
        Assert.True(BaseSystemCollectionGate.HasExactGrant(OperationResults.Ok(Evaluation("system.read.execute")), "system.read.execute"));
    }

    [Fact]
    public void EverySystemReadSourceRequiresItsExactGrant()
    {
        CollectionDefinition first = Collection() with { Id = "system-first", System = true };
        CollectionDefinition second = Collection() with { Id = "system-second", System = true };
        OperationResult<BasePolicyEvaluation> granted = OperationResults.Ok(Evaluation("system.read.execute"));
        OperationResult<BasePolicyEvaluation> denied = OperationResults.Ok(Evaluation("different.execute"));

        Assert.True(BaseSystemCollectionGate.AllowsSource(first, granted, "system.read.execute"));
        Assert.False(BaseSystemCollectionGate.AllowsSource(second, denied, "system.read.execute"));
        Assert.False(new[] { (first, granted), (second, denied) }.All(source =>
            BaseSystemCollectionGate.AllowsSource(source.Item1, source.Item2, "system.read.execute")));
    }

    [Fact]
    public void VerifiedProviderSatisfiesDeclaredRequirementButUnprotectedCoverageNeverDoes()
    {
        BaseStorageProtectionRequirement requirement = Requirement();
        BaseStorageProtectionCapability capability = Capability(BaseStorageProtectionState.Protected);
        BaseStorageProtectionGraph graph = BaseStorageProtectionContract.FinalizeGraph([requirement], [], new Dictionary<(string, string), BaseStorageProtectionRequirement>(), [capability]);
        Assert.Single(graph.Requirements);

        Assert.Throws<InvalidOperationException>(() => BaseStorageProtectionContract.FinalizeGraph(
            [requirement], [], new Dictionary<(string, string), BaseStorageProtectionRequirement>(),
            [Capability(BaseStorageProtectionState.Unprotected)]));
    }

    [Fact]
    public void CapabilityMayDeclareNonOwnedSurfacesNotApplicable()
    {
        BaseStorageProtectionCapability capability = Capability(BaseStorageProtectionState.NotApplicable) with
        {
            Coverage = Capability(BaseStorageProtectionState.NotApplicable).Coverage with
            {
                ExternalFilesAndBlobs = BaseStorageProtectionState.Protected,
            },
        };
        BaseStorageProtectionRequirement requirement = Requirement() with
        {
            Coverage = Requirement().Coverage with
            {
                AuthoritativeRecords = [BaseStorageProtectionState.NotApplicable],
                Journal = [BaseStorageProtectionState.NotApplicable],
                Receipts = [BaseStorageProtectionState.NotApplicable],
                ProviderState = [BaseStorageProtectionState.NotApplicable],
                Indexes = [BaseStorageProtectionState.NotApplicable],
                TemporaryFiles = [BaseStorageProtectionState.NotApplicable],
                AuthoritativeBackups = [BaseStorageProtectionState.NotApplicable],
                ExternalFilesAndBlobs = [BaseStorageProtectionState.Protected],
            },
        };

        BaseStorageProtectionGraph graph = BaseStorageProtectionContract.FinalizeGraph(
            [requirement], [], new Dictionary<(string, string), BaseStorageProtectionRequirement>(), [capability]);

        Assert.Single(graph.Requirements);
    }

    [Fact]
    public void NoneGuaranteeCannotClaimProtectedCoverage()
    {
        BaseStorageProtectionCapability capability = Capability(BaseStorageProtectionState.NotApplicable) with
        {
            Guarantee = BaseStorageEncryptionGuarantee.None,
            KeyOwner = BaseStorageKeyOwner.None,
            Rotation = BaseStorageRotationSupport.None,
            Verification = BaseStorageVerificationStatus.Unverified,
            Coverage = Capability(BaseStorageProtectionState.NotApplicable).Coverage with
            {
                ExternalFilesAndBlobs = BaseStorageProtectionState.Protected,
            },
        };

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => BaseStorageProtectionContract.ValidateCapability(capability));

        Assert.Equal(BaseConfidentialityErrorCodes.StorageDescriptorInvalid, failure.Message);
    }

    private static BasePolicyEvaluation Evaluation(params string[] grantIds) => new()
    {
        EffectiveTextSearchInfluenceFilters = System.Collections.Immutable.ImmutableDictionary<string, FilterExpression>.Empty,
        Decision = new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = PolicyOutcome.Allowed,
        },
        Authority = new BasePolicyEvaluationAuthority
        {
            PolicyGraphGeneration = 1,
            PolicyOwnerChecksum = [1],
            AdmittedGrants = [.. grantIds.Select(id => new BaseAdmittedGrantAuthority
            {
                GrantId = id,
                GrantVersion = 1,
                GrantRegistrationChecksum = [1],
                GrantChecksum = [1],
            })],
            AppliedPolicies = [],
            Constraints = new BasePolicyConstraintAuthority { EffectiveTextSearchInfluenceFilters = System.Collections.Immutable.ImmutableDictionary<string, FilterExpression>.Empty },
            Checksum = BasePolicyEvaluationAuthorityChecksum.Create(new byte[32]),
        },
    };

    private static BaseStorageProtectionCapability Capability(BaseStorageProtectionState state) => new()
    {
        OwningModuleId = "test.storage", Guarantee = BaseStorageEncryptionGuarantee.ProviderVerified,
        KeyOwner = BaseStorageKeyOwner.Provider, Rotation = BaseStorageRotationSupport.Online,
        Verification = BaseStorageVerificationStatus.OperationallyVerified,
        Coverage = new BaseStorageProtectionCoverage
        {
            AuthoritativeRecords = state, Journal = state, Receipts = state, ProviderState = state,
            Indexes = state, TemporaryFiles = state, AuthoritativeBackups = state,
            AdministrativeExports = BaseStorageProtectionState.NotRetained,
            OrdinaryExports = BaseStorageProtectionState.NotRetained,
            ExternalFilesAndBlobs = BaseStorageProtectionState.NotApplicable,
        }
    };

    private static BaseStorageProtectionRequirement Requirement()
    {
        System.Collections.Immutable.ImmutableArray<BaseStorageProtectionState> protectedOnly = [BaseStorageProtectionState.Protected];
        System.Collections.Immutable.ImmutableArray<BaseStorageProtectionState> notRetained = [BaseStorageProtectionState.NotRetained];
        return new BaseStorageProtectionRequirement
        {
            OwningModuleId = "test.storage", PermittedGuarantees = [BaseStorageEncryptionGuarantee.ProviderDeclared],
            PermittedKeyOwners = [BaseStorageKeyOwner.Provider], RequiredRotation = BaseStorageRotationSupport.Offline,
            MinimumVerification = BaseStorageVerificationStatus.ConfigurationValidated,
            Coverage = new BaseStorageProtectionCoverageRequirement
            {
                AuthoritativeRecords = protectedOnly, Journal = protectedOnly, Receipts = protectedOnly,
                ProviderState = protectedOnly, Indexes = protectedOnly, TemporaryFiles = protectedOnly,
                AuthoritativeBackups = protectedOnly, AdministrativeExports = notRetained,
                OrdinaryExports = notRetained, ExternalFilesAndBlobs = [BaseStorageProtectionState.NotApplicable],
            }
        };
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
            Id = "field." + name, ApplicationName = name, WireName = name, Type = "string", Presence = BaseFieldPresence.Required,
            Confidentiality = confidentiality, Disclosure = policy,
        };
    }
}
