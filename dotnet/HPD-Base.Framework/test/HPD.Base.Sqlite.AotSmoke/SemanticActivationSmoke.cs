using System.Collections.Immutable;

namespace HPD.Base.Sqlite.AotSmoke;

internal sealed class SemanticActivationSmokeMarker;

internal static class SemanticActivationSmoke
{
    internal const string DefinitionId = "hpd.base.sqlite.aot.semantic.v1";
    internal const string EnsureGrant = "hpd.base.sqlite.aot.semantic.ensure";
    internal const string RetireGrant = "hpd.base.sqlite.aot.semantic.retire";
    internal const string MaintainGrant = "hpd.base.sqlite.aot.semantic.maintain";

    internal static BaseSemanticActivationKeyExpression Expression { get; } = new BaseSemanticActivationKeyPropertyExpression
    {
        Property = new BaseModuleRequestPropertyReference
        {
            StablePropertyPath = ["hpd.base.sqlite.aot.semantic.request.marker"],
            DeclaredTypeId = "hpd.base.sqlite.aot.semantic.request",
        },
        ScalarKind = BaseSemanticActivationKeyScalarKind.String,
        MaximumValueBytes = 128,
        AllowNull = false,
    };

    internal static BaseSemanticActivationKeyDefinition Definition { get; } = CreateDefinition();
    internal static BaseSemanticActivationKeyIdentity<SemanticMutationSmokeRequest, SemanticActivationSmokeMarker> Identity { get; } =
        SemanticEnsureMutationSmoke.CreateSemanticActivationKeyIdentity<SemanticActivationSmokeMarker>(Definition, Expression);
    internal static BaseSemanticActivationRegistration<SemanticMutationSmokeRequest, SemanticActivationSmokeMarker> Registration { get; } = new()
    {
        Definition = Definition,
        RequestTypeId = Definition.RequestTypeId,
        RequestSerializerChecksum = Definition.RequestSerializerChecksum,
        KeyIdentity = Identity,
    };

    private static BaseSemanticActivationKeyDefinition CreateDefinition()
    {
        var serializer = new SemanticMutationSmokeJsonContext(BaseSerializerGeneratedContract.CreateOptions(null));
        ImmutableArray<byte> requestChecksum = Convert.FromHexString(BaseSerializerContract.GraphFingerprint(
            serializer.SemanticMutationSmokeRequest, SemanticEnsureMutationSmoke.Identity.SerializerDeclarations)).ToImmutableArray();
        return BaseSemanticActivationDefinitionContract.Seal(new BaseSemanticActivationKeyDefinition
        {
            Id = DefinitionId, Version = 1, OwningApplicationId = "hpd.base.sqlite.aot",
            OwningModuleId = "hpd.base.sqlite.aot",
            EnsureOperation = new()
            {
                OperationId = SemanticEnsureMutationSmoke.Definition.Id, OperationVersion = 1,
                OperationChecksum = Convert.ToHexStringLower(SemanticEnsureMutationSmoke.Definition.Checksum.ToArray()),
            },
            RetirementOperation = new()
            {
                OperationId = SemanticRetirementMutationSmoke.Definition.Id, OperationVersion = 1,
                OperationChecksum = Convert.ToHexStringLower(SemanticRetirementMutationSmoke.Definition.Checksum.ToArray()),
            },
            Activation = new()
            {
                Id = ActivationSmoke.Registration.Definition.Id, Version = ActivationSmoke.Registration.Definition.Version,
                Checksum = ActivationSmoke.Registration.Definition.Checksum,
            },
            ScopeKind = BaseSubjectScopeKind.Tenant,
            EnsureGrantId = EnsureGrant, RetirementGrantId = RetireGrant, MaintenanceGrantId = MaintainGrant,
            Compaction = new BaseSemanticActivationNoCompaction(),
            RequestTypeId = "hpd.base.sqlite.aot.semantic.request", RequestSerializerChecksum = requestChecksum,
            KeyExpressionChecksum = BaseSemanticActivationKeyCompiler.ExpressionChecksum(Expression).ToImmutableArray(),
            Limits = new BaseSemanticActivationLimits
            {
                MaximumCanonicalKeyBytes = 256, MaximumLiveSlots = 32, MaximumRetiredSlots = 32,
                MaximumAbsenceMarkers = 32,
                Execution = new BaseSemanticActivationExecutionLimits
                {
                    MaximumOperations = 1, MaximumScopeDirectoryReads = 1, MaximumSlotReads = 1,
                    MaximumActivationReads = 1, MaximumReadIntervals = 4, MaximumIndexOperations = 8,
                    MaximumActivationBytes = 4096, MaximumScopeDirectoryBytes = 1024,
                    MaximumEvidenceBytes = 8192, MaximumReceiptBytes = 8192, MaximumTransientBytes = 16384,
                },
                Deadlines = new BaseSemanticActivationDeadlineCapability
                {
                    AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
                    CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
                    MaintenanceTimeout = TimeSpan.FromSeconds(5), QuarantineRetentionTimeout = TimeSpan.FromSeconds(5),
                },
            },
            Checksum = [],
        });
    }
}
