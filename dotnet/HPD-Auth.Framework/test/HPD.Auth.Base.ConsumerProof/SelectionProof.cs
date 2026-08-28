using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

internal static class SelectionProof
{
    internal const string GrantId = "proof.selection.delete";

    internal static BaseSelectionOperationProfile Profile { get; } = new()
    {
        Id = "proof.selection.delete.v1",
        Version = 1,
        ApplicationId = "hpd.auth.base.consumer-proof",
        CollectionId = "proof.selection-items",
        RequiredGrantId = GrantId,
        MutationKind = BaseSelectionMutationKind.Delete,
        Limits = new BaseSelectionOperationLimits
        {
            MaximumQueryNodes = 16, MaximumQueryDepth = 4, MaximumLiteralValues = 16,
            MaximumSelectedRecords = 8, MaximumSelectedBytes = 16_384,
            MaximumProducedMutations = 8, MaximumQueryExecutions = 1, MaximumReadIntervals = 8,
            MaximumWrittenBytes = 16_384, MaximumFactBytes = 16_384, MaximumJournalBytes = 16_384,
            MaximumReceiptBytes = 16_384, MaximumRelationChecks = 16, MaximumUniqueConstraintChecks = 16,
            MaximumPreviousStateRequirements = 8, MaximumTransientBytes = 65_536,
            MaximumResultBytes = 4096, AcquisitionTimeout = TimeSpan.FromSeconds(5),
            ExecutionTimeout = TimeSpan.FromSeconds(5), CallerCommitObservationTimeout = TimeSpan.FromSeconds(5),
        },
    };

    internal static BaseGeneratedSelectionProfileIdentity Identity { get; } =
        BaseGeneratedSelectionProfiles.RegisterSelectionProfile(
            BaseGeneratedModules.RegisterCollectionModule(Profile.ApplicationId, Profile.CollectionId),
            Profile);
}

[BaseCollection("proof.selection-items", typeof(ConsumerJsonSerializerContext), SystemOwnerModuleId = "proof.module")]
internal sealed partial record ProofSelectionItem
{
    [BaseField("proof.selection.name", MaximumUtf8Bytes = 64,
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    public required string Name { get; init; }
}
