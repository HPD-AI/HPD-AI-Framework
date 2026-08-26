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
        Schema = BaseAtomicSchemaContract.Freeze(value.Schema),
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
        SemanticActivation = value.SemanticActivation is null ? null : FreezeSemantic(value.SemanticActivation),
        Limits = value.Limits with { },
    };

    private static BaseAtomicSemanticActivationExtension FreezeSemantic(BaseAtomicSemanticActivationExtension value) => new()
    {
        Capture = value.Capture with
        {
            Definition = FreezeSemanticDefinition(value.Capture.Definition),
            CanonicalKey = value.Capture.CanonicalKey.ToArray().ToImmutableArray(),
            KeyPreimageChecksum = value.Capture.KeyPreimageChecksum.ToArray().ToImmutableArray(),
            Scope = value.Capture.Scope with { Value = value.Capture.Scope.Value is null ? null : new string(value.Capture.Scope.Value.AsSpan()) },
            ProposedScopeBindingId = value.Capture.ProposedScopeBindingId.ToArray().ToImmutableArray(),
            StoreAuthority = value.Capture.StoreAuthority with
            {
                ApplicationId = new string(value.Capture.StoreAuthority.ApplicationId.AsSpan()),
                LogicalStoreId = new string(value.Capture.StoreAuthority.LogicalStoreId.AsSpan()),
                StoreInstanceId = new string(value.Capture.StoreAuthority.StoreInstanceId.AsSpan()),
                DefinitionSetChecksum = value.Capture.StoreAuthority.DefinitionSetChecksum.ToArray().ToImmutableArray(),
            },
            Limits = value.Capture.Limits with { },
            RecoveryPreflight = value.Capture.RecoveryPreflight is null ? null : FreezeRecoveryPreflight(value.Capture.RecoveryPreflight),
            RecoveryPending = value.Capture.RecoveryPending is null ? null : FreezeRecoveryPending(value.Capture.RecoveryPending),
        },
        StructuralDigest = value.StructuralDigest.ToArray().ToImmutableArray(),
        Operation = value.Operation switch
        {
            BaseSemanticActivationEnsureIntent ensure => ensure with
            {
                Definition = FreezeSemanticDefinition(ensure.Definition),
                Key = BaseSemanticActivationKeyDigest.Create(ensure.Key.ToArray()),
                CanonicalKey = ensure.CanonicalKey.ToArray().ToImmutableArray(),
                Scope = ensure.Scope with { Value = ensure.Scope.Value is null ? null : new string(ensure.Scope.Value.AsSpan()) },
                SubjectLifetime = FreezeLifetime(ensure.SubjectLifetime),
                Due = ensure.Due with { },
                Activation = ensure.Activation with
                {
                    Definition = ensure.Activation.Definition with { Id = new string(ensure.Activation.Definition.Id.AsSpan()), Checksum = ensure.Activation.Definition.Checksum.ToArray().ToImmutableArray() },
                    CanonicalInput = ensure.Activation.CanonicalInput.ToArray().ToImmutableArray(),
                    InputChecksum = ensure.Activation.InputChecksum.ToArray().ToImmutableArray(),
                    Scope = ensure.Activation.Scope with { Value = ensure.Activation.Scope.Value is null ? null : new string(ensure.Activation.Scope.Value.AsSpan()) },
                    Due = ensure.Activation.Due with { },
                    Limits = ensure.Activation.Limits with
                    {
                        Provider = ensure.Activation.Limits.Provider with { },
                        AtomicCreation = ensure.Activation.Limits.AtomicCreation with { Deadlines = ensure.Activation.Limits.AtomicCreation.Deadlines with { } },
                    },
                    Identity = ensure.Activation.Identity with
                    {
                        SemanticDefinition = FreezeSemanticDefinition(ensure.Activation.Identity.SemanticDefinition),
                        Key = BaseSemanticActivationKeyDigest.Create(ensure.Activation.Identity.Key.ToArray()),
                        ScopeBindingId = ensure.Activation.Identity.ScopeBindingId.ToArray().ToImmutableArray(),
                        DerivedActivationIdBytes = ensure.Activation.Identity.DerivedActivationIdBytes.ToArray().ToImmutableArray(),
                        Checksum = ensure.Activation.Identity.Checksum.ToArray().ToImmutableArray(),
                    },
                },
            },
            BaseSemanticActivationRetireIntent retire => retire with
            {
                Definition = FreezeSemanticDefinition(retire.Definition),
                Key = BaseSemanticActivationKeyDigest.Create(retire.Key.ToArray()),
                CanonicalKey = retire.CanonicalKey.ToArray().ToImmutableArray(),
                Scope = retire.Scope with { Value = retire.Scope.Value is null ? null : new string(retire.Scope.Value.AsSpan()) },
                SubjectLifetime = FreezeLifetime(retire.SubjectLifetime),
                CompletionOperation = retire.CompletionOperation with
                {
                    OperationId = new string(retire.CompletionOperation.OperationId.AsSpan()),
                    OperationChecksum = new string(retire.CompletionOperation.OperationChecksum.AsSpan()),
                },
            },
            _ => throw new InvalidOperationException("base.semanticActivation.contractInvalid"),
        },
    };

    private static BaseSemanticRecoveryPreflightEvidence FreezeRecoveryPreflight(BaseSemanticRecoveryPreflightEvidence value) => value with
    {
        ScopeBinding = value.ScopeBinding with
        {
            BindingId = value.ScopeBinding.BindingId.ToArray().ToImmutableArray(),
            ProtectedCanonicalScope = value.ScopeBinding.ProtectedCanonicalScope.ToArray().ToImmutableArray(),
            SeekDigest = value.ScopeBinding.SeekDigest.ToArray().ToImmutableArray(),
            Checksum = value.ScopeBinding.Checksum.ToArray().ToImmutableArray(),
        },
        Key = BaseSemanticActivationKeyDigest.Create(value.Key.ToArray()),
        Live = value.Live with
        {
            Definition = FreezeSemanticDefinition(value.Live.Definition),
            KeyDigest = BaseSemanticActivationKeyDigest.Create(value.Live.KeyDigest.ToArray()),
            Scope = value.Live.Scope with { Value = value.Live.Scope.Value is null ? null : new string(value.Live.Scope.Value.AsSpan()) },
            ScopeBinding = value.Live.ScopeBinding with
            {
                BindingId = value.Live.ScopeBinding.BindingId.ToArray().ToImmutableArray(),
                ProtectedCanonicalScope = value.Live.ScopeBinding.ProtectedCanonicalScope.ToArray().ToImmutableArray(),
                SeekDigest = value.Live.ScopeBinding.SeekDigest.ToArray().ToImmutableArray(),
                Checksum = value.Live.ScopeBinding.Checksum.ToArray().ToImmutableArray(),
            },
            SubjectLifetime = FreezeLifetime(value.Live.SubjectLifetime),
            ActivationDefinition = value.Live.ActivationDefinition with { Checksum = value.Live.ActivationDefinition.Checksum.ToArray().ToImmutableArray() },
            InputChecksum = value.Live.InputChecksum.ToArray().ToImmutableArray(),
            StoreAuthority = value.Live.StoreAuthority with
            {
                Requirement = value.Live.StoreAuthority.Requirement with { DefinitionSetChecksum = value.Live.StoreAuthority.Requirement.DefinitionSetChecksum.ToArray().ToImmutableArray() },
                Checksum = value.Live.StoreAuthority.Checksum.ToArray().ToImmutableArray(),
            },
            Checksum = value.Live.Checksum.ToArray().ToImmutableArray(),
        },
        ActivationChecksum = value.ActivationChecksum.ToArray().ToImmutableArray(),
        ActivationTerminalReceiptChecksum = value.ActivationTerminalReceiptChecksum.ToArray().ToImmutableArray(),
        TerminalReceipt = value.TerminalReceipt with
        {
            Fingerprint = value.TerminalReceipt.Fingerprint.ToArray().ToImmutableArray(),
            ResultBytes = value.TerminalReceipt.ResultBytes.ToArray().ToImmutableArray(),
            ResultChecksum = value.TerminalReceipt.ResultChecksum.ToArray().ToImmutableArray(),
            AuthorityChecksum = value.TerminalReceipt.AuthorityChecksum.ToArray().ToImmutableArray(),
        },
        ReadIntervals = value.ReadIntervals.Select(static interval => interval with
        {
            CanonicalLowerBound = interval.CanonicalLowerBound.ToArray().ToImmutableArray(),
            CanonicalUpperBound = interval.CanonicalUpperBound.ToArray().ToImmutableArray(),
        }).ToImmutableArray(),
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    private static BaseSemanticRecoveryPendingCommitAuthority FreezeRecoveryPending(BaseSemanticRecoveryPendingCommitAuthority value) => value with
    {
        AuthorityChecksum = value.AuthorityChecksum.ToArray().ToImmutableArray(),
        LocalFingerprint = value.LocalFingerprint.ToArray().ToImmutableArray(),
        LocalStructuralDigest = value.LocalStructuralDigest.ToArray().ToImmutableArray(),
        Intent = value.Intent with
        {
            Boundary = value.Intent.Boundary with
            {
                ScopeBindingId = value.Intent.Boundary.ScopeBindingId.ToArray().ToImmutableArray(),
                Key = BaseSemanticActivationKeyDigest.Create(value.Intent.Boundary.Key.ToArray()),
            },
            RetirementOperationFingerprint = value.Intent.RetirementOperationFingerprint.ToArray().ToImmutableArray(),
            SubjectLifetime = FreezeLifetime(value.Intent.SubjectLifetime),
            Checksum = value.Intent.Checksum.ToArray().ToImmutableArray(),
        },
        Pending = value.Pending with
        {
            IntentChecksum = value.Pending.IntentChecksum.ToArray().ToImmutableArray(),
            Checksum = value.Pending.Checksum.ToArray().ToImmutableArray(),
            Signature = value.Pending.Signature.ToArray().ToImmutableArray(),
        },
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    private static BaseSemanticActivationDefinitionIdentity FreezeSemanticDefinition(BaseSemanticActivationDefinitionIdentity value) => value with
    {
        Id = new string(value.Id.AsSpan()), Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    private static BaseSemanticActivationSubjectLifetimeBinding? FreezeLifetime(BaseSemanticActivationSubjectLifetimeBinding? value) => value is null ? null : value with
    {
        ContractId = new string(value.ContractId.AsSpan()), ContractChecksum = value.ContractChecksum.ToArray().ToImmutableArray(),
        ScopeBindingId = value.ScopeBindingId.ToArray().ToImmutableArray(), Checksum = value.Checksum.ToArray().ToImmutableArray(),
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
        TextIndexChecksum = ImmutableArray.Create(value.TextIndexChecksum.ToArray()), RecordId = RecordId.Create(new string(value.RecordId.Value.AsSpan())),
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
        Indexes = value.Indexes?.Select(BaseSchemaContract.Clone).ToArray(),
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
