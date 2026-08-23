using System.Collections.Immutable;

namespace HPD.Base.Tests.Application.Activations;

public sealed class BaseSemanticActivationRegistryTests
{
    [Fact]
    public void Capability_checksum_binds_maintenance_authority_and_page_limit()
    {
        BaseSemanticActivationCapability identityOnly = BaseSemanticActivationCapabilityContract.BuiltIn(durable: false);
        BaseSemanticActivationCapability maintained = BaseSemanticActivationCapabilityContract.BuiltIn(durable: true);

        BaseSemanticActivationCapabilityContract.IsValid(identityOnly).Should().BeTrue();
        BaseSemanticActivationCapabilityContract.IsValid(maintained).Should().BeTrue();
        identityOnly.Checksum.Should().NotEqual(maintained.Checksum);
        BaseSemanticActivationCapabilityContract.IsValid(identityOnly with { MaintenanceSupported = true }).Should().BeFalse();
        BaseSemanticActivationCapabilityContract.IsValid(identityOnly with { MaximumMaintenancePageSize = 256 }).Should().BeFalse();
        BaseSemanticActivationCapabilityContract.IsValid(maintained with { MaintenanceSupported = false }).Should().BeFalse();
        BaseSemanticActivationCapabilityContract.IsValid(maintained with { MaximumMaintenancePageSize = 0 }).Should().BeFalse();
    }

    [Fact]
    public void Closed_key_compiler_is_deterministic_and_property_bound()
    {
        BaseSemanticActivationKeyDefinition definition = Installed();
        var options = new System.Text.Json.JsonSerializerOptions
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        var binding = new BaseModuleDtoPropertyBinding(
            ["value"], typeof(Request), typeof(string), BaseFieldConfidentiality.Public,
            BaseRecordDisclosure.Include, nullable: false, applicationName: nameof(Request.Value));
        var module = new BaseGeneratedModuleMutationIdentity<Request, object>(
            "test.ensure", 1, new byte[32],
            (System.Text.Json.Serialization.Metadata.JsonTypeInfo<Request>)options.GetTypeInfo(typeof(Request)),
            (System.Text.Json.Serialization.Metadata.JsonTypeInfo<object>)options.GetTypeInfo(typeof(object)),
            [binding], []);
        var expression = new BaseSemanticActivationKeyTupleExpression
        {
            Elements =
            [
                new BaseSemanticActivationKeyConstantExpression
                {
                    ScalarKind = BaseSemanticActivationKeyScalarKind.String,
                    CanonicalBaseJson = "\"auth-user\""u8.ToArray().ToImmutableArray(), MaximumValueBytes = 32,
                },
                new BaseSemanticActivationKeyPropertyExpression
                {
                    Property = new BaseModuleRequestPropertyReference
                    {
                        StablePropertyPath = ["value"], DeclaredTypeId = "string",
                    },
                    ScalarKind = BaseSemanticActivationKeyScalarKind.String, MaximumValueBytes = 64, AllowNull = false,
                },
            ],
        };
        BaseSemanticActivationKeyIdentity<Request, Marker> first = BaseSemanticActivationKeyCompiler.Create<Request, object, Marker>(
            definition.OwningApplicationId, definition.OwningModuleId, definition.Id, definition.Version,
            definition.Checksum.AsSpan(), definition.Limits.MaximumCanonicalKeyBytes, module, expression);
        BaseSemanticActivationKeyIdentity<Request, Marker> second = BaseSemanticActivationKeyCompiler.Create<Request, object, Marker>(
            definition.OwningApplicationId, definition.OwningModuleId, definition.Id, definition.Version,
            definition.Checksum.AsSpan(), definition.Limits.MaximumCanonicalKeyBytes, module, expression);

        first.Create(new Request("42"), 1).CopyCanonicalKey().Should().Equal(second.Create(new Request("42"), 1).CopyCanonicalKey());
        first.Create(new Request("42"), 1).CopyCanonicalKey().Should().NotEqual(first.Create(new Request("43"), 1).CopyCanonicalKey());
    }

    [Fact]
    public void Definition_checksum_is_canonical_and_defensively_owned()
    {
        BaseSemanticActivationKeyDefinition draft = Draft();
        BaseSemanticActivationKeyDefinition sealedDefinition = BaseSemanticActivationDefinitionContract.Seal(draft);
        byte[] checksum = sealedDefinition.Checksum.ToArray();
        byte[] activationChecksum = sealedDefinition.Activation.Checksum.ToArray();

        checksum[0] ^= 0xff;
        activationChecksum[0] ^= 0xff;

        sealedDefinition.Checksum[0].Should().NotBe(checksum[0]);
        sealedDefinition.Activation.Checksum[0].Should().NotBe(activationChecksum[0]);
        BaseSemanticActivationDefinitionContract.Seal(sealedDefinition).Checksum.Should().Equal(sealedDefinition.Checksum);
    }

    [Fact]
    public void Registration_rejects_substituted_identity_authority()
    {
        BaseSemanticActivationKeyDefinition definition = Installed();
        BaseSemanticActivationKeyIdentity<Request, Marker> identity = Identity(definition,
            static request => System.Text.Encoding.UTF8.GetBytes(request.Value), Enumerable.Repeat((byte)9, 32).ToArray());

        Action act = () => new BaseInstalledSemanticActivationRegistration<Request, Marker>(new()
        {
            Definition = definition,
            RequestTypeId = "test.semantic.request.v1",
            RequestSerializerChecksum = Bytes(3),
            KeyIdentity = identity,
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("base.semanticActivation.contractInvalid");
    }

    [Fact]
    public void Key_creation_is_opaque_owned_and_bounded()
    {
        BaseSemanticActivationKeyDefinition definition = Installed() with
        {
            Limits = Installed().Limits with { MaximumCanonicalKeyBytes = 3 },
        };
        definition = BaseSemanticActivationDefinitionContract.Seal(definition with { Checksum = [] });
        byte[] source = [1, 2, 3];
        BaseSemanticActivationKeyIdentity<Request, Marker> identity = Identity(definition, _ => source, maximum: 3);

        BaseSemanticActivationKey<Marker> key = identity.Create(new Request("ignored"), 1);
        source[0] = 9;

        key.CopyCanonicalKey().Should().Equal(1, 2, 3);
        Action tooLarge = () => Identity(definition, _ => new byte[3], maximum: 2).Create(new Request("ignored"), 1);
        tooLarge.Should().Throw<InvalidOperationException>().WithMessage("base.semanticActivation.keyInvalid");
    }

    [Fact]
    public void Registry_rejects_two_versions_of_one_stable_definition()
    {
        BaseSemanticActivationKeyDefinition first = Installed();
        BaseSemanticActivationKeyDefinition second = BaseSemanticActivationDefinitionContract.Seal(first with { Version = 2, Checksum = [] });
        IBaseSemanticActivationRegistration[] registrations =
        [
            Registration(first),
            Registration(second),
        ];

        Action act = () => _ = new BaseSemanticActivationRegistry(registrations);

        act.Should().Throw<InvalidOperationException>().WithMessage("base.semanticActivation.registrationConflict");
    }

    [Fact]
    public void Finalized_registries_issue_distinct_graph_bound_keys()
    {
        BaseSemanticActivationKeyDefinition definition = Installed();
        var firstRegistration = (BaseInstalledSemanticActivationRegistration<Request, Marker>)Registration(definition);
        var secondRegistration = (BaseInstalledSemanticActivationRegistration<Request, Marker>)Registration(definition);
        var first = new BaseSemanticActivationRegistry([firstRegistration]);
        var second = new BaseSemanticActivationRegistry([secondRegistration]);

        BaseSemanticActivationKey<Marker> firstKey = first.CreateKey(firstRegistration.KeyIdentity, new Request("same"));
        BaseSemanticActivationKey<Marker> secondKey = second.CreateKey(secondRegistration.KeyIdentity, new Request("same"));

        firstKey.CopyCanonicalKey().Should().Equal(secondKey.CopyCanonicalKey());
        ((IBaseSemanticActivationKey)firstKey).OwnerGeneration.Should().NotBe(((IBaseSemanticActivationKey)secondKey).OwnerGeneration);
    }

    [Fact]
    public void Retired_authority_accepts_only_the_closed_L51_terminal_union()
    {
        BaseModuleMutationProcessor<Request, object>.SemanticTerminalStateAllowed(BaseActivationState.Succeeded).Should().BeTrue();
        BaseModuleMutationProcessor<Request, object>.SemanticTerminalStateAllowed(BaseActivationState.Exhausted).Should().BeTrue();
        BaseModuleMutationProcessor<Request, object>.SemanticTerminalStateAllowed(BaseActivationState.Cancelled).Should().BeTrue();
        BaseModuleMutationProcessor<Request, object>.SemanticTerminalStateAllowed(BaseActivationState.Migrated).Should().BeTrue();
        BaseModuleMutationProcessor<Request, object>.SemanticTerminalStateAllowed(BaseActivationState.Disposed).Should().BeTrue();
        BaseModuleMutationProcessor<Request, object>.SemanticTerminalStateAllowed(BaseActivationState.OutcomeUnknown).Should().BeFalse();
        BaseModuleMutationProcessor<Request, object>.SemanticTerminalStateAllowed((BaseActivationState)999).Should().BeFalse();
    }

    [Fact]
    public void Key_expression_and_request_receipts_are_definition_authority()
    {
        BaseSemanticActivationKeyExpression first = new BaseSemanticActivationKeyConstantExpression
        {
            ScalarKind = BaseSemanticActivationKeyScalarKind.String,
            CanonicalBaseJson = "\"first\""u8.ToArray().ToImmutableArray(), MaximumValueBytes = 16,
        };
        BaseSemanticActivationKeyExpression second = ((BaseSemanticActivationKeyConstantExpression)first) with
        {
            CanonicalBaseJson = "\"second\""u8.ToArray().ToImmutableArray(),
        };
        BaseSemanticActivationKeyCompiler.ExpressionChecksum(first)
            .Should().NotEqual(BaseSemanticActivationKeyCompiler.ExpressionChecksum(second));

        BaseSemanticActivationKeyDefinition definition = Installed();
        Action alteredRequest = () => new BaseInstalledSemanticActivationRegistration<Request, Marker>(new()
        {
            Definition = definition,
            RequestTypeId = "test.semantic.changed.v1",
            RequestSerializerChecksum = Bytes(3),
            KeyIdentity = Identity(definition, static request => System.Text.Encoding.UTF8.GetBytes(request.Value)),
        });
        alteredRequest.Should().Throw<InvalidOperationException>().WithMessage("base.semanticActivation.contractInvalid");
    }

    private static IBaseSemanticActivationRegistration Registration(BaseSemanticActivationKeyDefinition definition) =>
        new BaseInstalledSemanticActivationRegistration<Request, Marker>(new()
        {
            Definition = definition,
            RequestTypeId = "test.semantic.request.v1",
            RequestSerializerChecksum = Bytes(3),
            KeyIdentity = Identity(definition, static request => System.Text.Encoding.UTF8.GetBytes(request.Value)),
        });

    private static BaseSemanticActivationKeyIdentity<Request, Marker> Identity(
        BaseSemanticActivationKeyDefinition definition,
        Func<Request, ReadOnlyMemory<byte>> factory,
        byte[]? checksum = null,
        int? maximum = null) => new(
            definition.OwningApplicationId, definition.OwningModuleId, definition.Id, definition.Version,
            checksum ?? definition.Checksum.ToArray(), definition.KeyExpressionChecksum.AsSpan(), maximum ?? definition.Limits.MaximumCanonicalKeyBytes,
            () => definition.RequestSerializerChecksum.ToArray(), factory);

    private static BaseSemanticActivationKeyDefinition Installed() =>
        BaseSemanticActivationDefinitionContract.Seal(Draft());

    private static BaseSemanticActivationKeyDefinition Draft() => new()
    {
        Id = "test.semantic.v1",
        Version = 1,
        OwningApplicationId = "test.application",
        OwningModuleId = "test.module",
        EnsureOperation = new() { OperationId = "test.ensure", OperationVersion = 1, OperationChecksum = new string('1', 64) },
        RetirementOperation = new() { OperationId = "test.retire", OperationVersion = 1, OperationChecksum = new string('2', 64) },
        Activation = new() { Id = "test.activation", Version = 1, Checksum = Bytes(4) },
        ScopeKind = BaseSubjectScopeKind.Tenant,
        EnsureGrantId = "test.semantic.ensure",
        RetirementGrantId = "test.semantic.retire",
        MaintenanceGrantId = "test.semantic.maintain",
        Compaction = new BaseSemanticActivationNoCompaction(),
        RequestTypeId = "test.semantic.request.v1",
        RequestSerializerChecksum = Bytes(3),
        KeyExpressionChecksum = Bytes(5),
        Limits = new()
        {
            MaximumCanonicalKeyBytes = 256,
            MaximumLiveSlots = 100,
            MaximumRetiredSlots = 100,
            MaximumAbsenceMarkers = 100,
            Execution = new()
            {
                MaximumOperations = 1,
                MaximumScopeDirectoryReads = 1,
                MaximumSlotReads = 1,
                MaximumActivationReads = 1,
                MaximumReadIntervals = 3,
                MaximumIndexOperations = 4,
                MaximumActivationBytes = 4096,
                MaximumScopeDirectoryBytes = 1024,
                MaximumEvidenceBytes = 4096,
                MaximumReceiptBytes = 4096,
                MaximumTransientBytes = 8192,
            },
            Deadlines = new()
            {
                AcquisitionTimeout = TimeSpan.FromSeconds(1),
                TransactionTimeout = TimeSpan.FromSeconds(1),
                CommitObservationTimeout = TimeSpan.FromSeconds(1),
                ReceiptResolutionTimeout = TimeSpan.FromSeconds(1),
                MaintenanceTimeout = TimeSpan.FromSeconds(1),
                QuarantineRetentionTimeout = TimeSpan.FromSeconds(1),
            },
        },
        Checksum = [],
    };

    private static ImmutableArray<byte> Bytes(byte value) => Enumerable.Repeat(value, 32).ToImmutableArray();
    private sealed record Request(string Value);
    private sealed class Marker;
}
