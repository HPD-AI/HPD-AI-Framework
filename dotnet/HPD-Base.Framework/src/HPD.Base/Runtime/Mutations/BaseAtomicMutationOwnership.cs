using System.Collections.Immutable;

namespace HPD.Base;

internal static class BaseAtomicMutationOwnership
{
    internal static BaseFinalizedAtomicExecutionPlan FreezePlan(BaseFinalizedAtomicExecutionPlan value) => value with
    {
        PlanDigest = new string(value.PlanDigest.AsSpan()),
        IntentDigest = new string(value.IntentDigest.AsSpan()),
        CaptureDigest = new string(value.CaptureDigest.AsSpan()),
        PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(value.PolicyAuthorityDigest.ToArray()),
        ActivationGuard = value.ActivationGuard is null ? null : FreezeGuard(value.ActivationGuard),
        Authority = value.Authority with
        {
            ApplicationId = new string(value.Authority.ApplicationId.AsSpan()),
            StoreInstanceId = new string(value.Authority.StoreInstanceId.AsSpan()),
        },
        Items = value.Items.Select(FreezeItem).ToImmutableArray(),
        SubjectValidations = value.SubjectValidations.Select(static validation => validation with
        {
            SourceFieldId = new string(validation.SourceFieldId.AsSpan()),
            ValidationPlanId = new string(validation.ValidationPlanId.AsSpan()),
            Reference = new BaseOwnedSubjectReference(
                validation.Reference.SubjectId,
                validation.Reference.AuthorityEpoch,
                validation.Reference.Incarnation),
            Scope = validation.Scope with
            {
                Value = validation.Scope.Value is null ? null : new string(validation.Scope.Value.AsSpan()),
            },
        }).ToImmutableArray(),
        Module = value.Module is null ? null : value.Module with
        {
            OperationId = new string(value.Module.OperationId.AsSpan()),
            OperationChecksum = new string(value.Module.OperationChecksum.AsSpan()),
            Decisions = value.Module.Decisions.Select(static decision => decision with
            {
                DecisionId = new string(decision.DecisionId.AsSpan()),
            }).ToImmutableArray(),
            ItemBindings = value.Module.ItemBindings.Select(static binding => binding with { }).ToImmutableArray(),
            RelationTargets = value.Module.RelationTargets.Select(static target => target with
            {
                SourceStatementId = new string(target.SourceStatementId.AsSpan()),
                SourceFieldId = new string(target.SourceFieldId.AsSpan()),
                TargetCollectionId = new string(target.TargetCollectionId.AsSpan()),
                PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(target.PolicyAuthorityDigest.ToArray()),
            }).ToImmutableArray(),
            Comparisons = value.Module.Comparisons.Select(static comparison => comparison with { }).ToImmutableArray(),
            Increments = value.Module.Increments.Select(static increment => increment with { }).ToImmutableArray(),
            ResultProjectionDigest = new string(value.Module.ResultProjectionDigest.AsSpan()),
        },
        SubjectRetirement=value.SubjectRetirement is null?null:value.SubjectRetirement with
        {
            PlanChecksum=new string(value.SubjectRetirement.PlanChecksum.AsSpan()),
            Items=value.SubjectRetirement.Items.Select(static item=>item with
            {
                ContractId=new string(item.ContractId.AsSpan()),ContractChecksum=new string(item.ContractChecksum.AsSpan()),
                RetirementPolicyChecksum=new string(item.RetirementPolicyChecksum.AsSpan()),AcceptedConsumerSetChecksum=new string(item.AcceptedConsumerSetChecksum.AsSpan()),
                Scope=item.Scope with{Value=item.Scope.Value is null?null:new string(item.Scope.Value.AsSpan())},
                RequiredConsumers=item.RequiredConsumers.Select(static consumer=>consumer with{ConsumerId=new string(consumer.ConsumerId.AsSpan()),OwningModuleId=new string(consumer.OwningModuleId.AsSpan()),LifecycleConsumerChecksum=new string(consumer.LifecycleConsumerChecksum.AsSpan()),RetirementProfileId=new string(consumer.RetirementProfileId.AsSpan()),RetirementProfileChecksum=new string(consumer.RetirementProfileChecksum.AsSpan()),AcknowledgementGrantId=new string(consumer.AcknowledgementGrantId.AsSpan()),RetirementConsumerChecksum=new string(consumer.RetirementConsumerChecksum.AsSpan()),Limits=consumer.Limits with{}}).ToImmutableArray(),
            }).ToImmutableArray(),
        },
        Text = value.Text is null ? null : value.Text with
        {
            ProjectionDigest = ImmutableArray.Create(value.Text.ProjectionDigest.ToArray()),
            Facts = value.Text.Facts.Select(FreezeTextFact).ToImmutableArray(),
        },
        Activations = value.Activations is null ? null : new BaseActivationCreationExtension
        {
            StructuralDigest = ImmutableArray.Create(value.Activations.StructuralDigest.ToArray()),
            Items = value.Activations.Items.Select(static item => item with
            {
                Definition = item.Definition with
                {
                    Id = new string(item.Definition.Id.AsSpan()),
                    Checksum = ImmutableArray.Create(item.Definition.Checksum.ToArray()),
                },
                CanonicalInput = ImmutableArray.Create(item.CanonicalInput.ToArray()),
                InputChecksum = ImmutableArray.Create(item.InputChecksum.ToArray()),
                OccurrenceId = item.OccurrenceId is null ? null : new string(item.OccurrenceId.AsSpan()),
                OverlapKey = item.OverlapKey.IsDefaultOrEmpty
                    ? []
                    : ImmutableArray.Create(item.OverlapKey.ToArray()),
                Scope = item.Scope with
                {
                    Value = item.Scope.Value is null ? null : new string(item.Scope.Value.AsSpan()),
                },
                Identity = item.Identity with
                {
                    IdempotencyKey = new string(item.Identity.IdempotencyKey.AsSpan()),
                },
            }).ToImmutableArray(),
        },
        Limits = value.Limits with { },
    };

    private static BaseActivationGuard FreezeGuard(BaseActivationGuard value) => value with
    {
        Claim = value.Claim with
        {
            ActivationId = new string(value.Claim.ActivationId.AsSpan()),
            FencingToken = ImmutableArray.Create(value.Claim.FencingToken.ToArray()),
            WorkerIdentity = new string(value.Claim.WorkerIdentity.AsSpan()),
            StoreInstanceId = new string(value.Claim.StoreInstanceId.AsSpan()),
            DefinitionChecksum = ImmutableArray.Create(value.Claim.DefinitionChecksum.ToArray()),
        },
        StepId = new string(value.StepId.AsSpan()),
        ChildRequestFingerprint = ImmutableArray.Create(value.ChildRequestFingerprint.ToArray()),
    };

    private static BaseTextProjectionFact FreezeTextFact(BaseTextProjectionFact value) => value with
    {
        CollectionId = new(value.CollectionId.AsSpan()), TextIndexId = new(value.TextIndexId.AsSpan()),
        TextIndexChecksum = ImmutableArray.Create(value.TextIndexChecksum.ToArray()), RecordId = new(new string(value.RecordId.Value.AsSpan())),
        Before = FreezeTextState(value.Before), After = FreezeTextState(value.After), FactChecksum = ImmutableArray.Create(value.FactChecksum.ToArray()),
    };

    private static BaseTextProjectionRecordState? FreezeTextState(BaseTextProjectionRecordState? value) => value is null ? null : value with
    {
        Revision = value.Revision is null ? null : new RevisionToken(new string(value.Revision.Value.Value.AsSpan())),
        TenantId = value.TenantId is null ? null : new(value.TenantId.AsSpan()), ProjectId = value.ProjectId is null ? null : new(value.ProjectId.AsSpan()),
        StateChecksum = ImmutableArray.Create(value.StateChecksum.ToArray()),
        Fields = value.Fields.Select(static field => field with { StableFieldId = new(field.StableFieldId.AsSpan()), CanonicalJsonUtf8 = ImmutableArray.Create(field.CanonicalJsonUtf8.ToArray()) }).ToImmutableArray(),
    };

    private static BaseAtomicMutationPlanItem FreezeItem(BaseAtomicMutationPlanItem value) => value with
    {
        ItemId = value.ItemId is null ? null : new string(value.ItemId.AsSpan()),
        EventId = new string(value.EventId.AsSpan()),
        Collection = FreezeCollection(value.Collection),
        ProposedPayload = value.ProposedPayload is null ? null : RecordCloneHelpers.ClonePayload(value.ProposedPayload),
        Delete = value.Delete is null ? null : value.Delete with { },
        Current = value.Current is null ? null : RecordCloneHelpers.CloneEnvelope(value.Current),
        ChangedFields = value.ChangedFields.Select(static field => new string(field.AsSpan())).ToImmutableArray(),
        SubjectLifecycle = value.SubjectLifecycle is null ? null : value.SubjectLifecycle with
        {
            ContractId = new string(value.SubjectLifecycle.ContractId.AsSpan()),
            ContractChecksum = new string(value.SubjectLifecycle.ContractChecksum.AsSpan()),
            Memberships = value.SubjectLifecycle.Memberships.Select(static membership => membership with
            {
                ConsumerId = new string(membership.ConsumerId.AsSpan()),
                ConsumerChecksum = new string(membership.ConsumerChecksum.AsSpan()),
            }).ToImmutableArray(),
        },
        SubjectLifecycleTransition = value.SubjectLifecycleTransition is null ? null : value.SubjectLifecycleTransition with
        {
            Subject = new BaseOwnedSubjectReference(
                value.SubjectLifecycleTransition.Subject.SubjectId,
                value.SubjectLifecycleTransition.Subject.AuthorityEpoch,
                value.SubjectLifecycleTransition.Subject.Incarnation),
        },
        Operation = value.Operation with
        {
            ApplicationId = new string(value.Operation.ApplicationId.AsSpan()),
            CollectionId = value.Operation.CollectionId is { } collectionId ? new string(collectionId.AsSpan()) : null!,
            RecordId = value.Operation.RecordId is null ? null : new string(value.Operation.RecordId.AsSpan()),
            TenantId = value.Operation.TenantId is null ? null : new string(value.Operation.TenantId.AsSpan()),
            ProjectId = value.Operation.ProjectId is null ? null : new string(value.Operation.ProjectId.AsSpan()),
        },
    };

    private static CollectionDefinition FreezeCollection(CollectionDefinition value) => value with
    {
        Id = new string(value.Id.AsSpan()),
        Name = new string(value.Name.AsSpan()),
        DisplayName = value.DisplayName is null ? null : new string(value.DisplayName.AsSpan()),
        Kind = new string(value.Kind.AsSpan()),
        SystemOwnerModuleId = value.SystemOwnerModuleId is null ? null : new string(value.SystemOwnerModuleId.AsSpan()),
        Fields = value.Fields?.Select(static field => field with
        {
            Id = new string(field.Id.AsSpan()),
            ApplicationName = new string(field.ApplicationName.AsSpan()),
            WireName = new string(field.WireName.AsSpan()),
            SubjectReference = field.SubjectReference is null ? null : field.SubjectReference with
            {
                ContractId = new string(field.SubjectReference.ContractId.AsSpan()),
                ContractChecksum = new string(field.SubjectReference.ContractChecksum.AsSpan()),
            },
        }).ToArray(),
        Indexes = value.Indexes?.Select(static index => index with
        {
            Parts = index.Parts?.Select(static part => part with
            {
                FieldId = part.FieldId is null ? null : new string(part.FieldId.AsSpan()),
                Expression = part.Expression is null ? null : new string(part.Expression.AsSpan()),
                Collation = part.Collation is null ? null : new string(part.Collation.AsSpan()),
                OperatorClass = part.OperatorClass is null ? null : new string(part.OperatorClass.AsSpan()),
                Extensions = part.Extensions?.ToDictionary(
                    static pair => new string(pair.Key.AsSpan()),
                    static pair => pair.Value.Clone(),
                    StringComparer.Ordinal),
            }).ToArray(),
            Extensions = index.Extensions?.ToDictionary(
                static pair => new string(pair.Key.AsSpan()),
                static pair => pair.Value.Clone(),
                StringComparer.Ordinal),
        }).ToArray(),
        VectorIndexes = value.VectorIndexes?.Select(static index => index with
        {
            FilterFieldIds = index.FilterFieldIds.Select(static field => new string(field.AsSpan())).ToArray(),
        }).ToArray(),
        TextIndexes = value.TextIndexes?.Select(BaseTextIndexContract.Seal).ToArray(),
        PolicyRefs = value.PolicyRefs?.Select(static item => new string(item.AsSpan())).ToArray(),
        RequiredCapabilities = value.RequiredCapabilities?.Select(static item => new string(item.AsSpan())).ToArray(),
        Extensions = value.Extensions?.ToDictionary(
            static pair => new string(pair.Key.AsSpan()),
            static pair => pair.Value.Clone(),
            StringComparer.Ordinal),
        StorageProtectionRequirements = value.StorageProtectionRequirements?.Select(static requirement => requirement with
        {
            PermittedGuarantees = requirement.PermittedGuarantees.ToImmutableArray(),
            PermittedKeyOwners = requirement.PermittedKeyOwners.ToImmutableArray(),
        }).ToArray(),
    };
}
