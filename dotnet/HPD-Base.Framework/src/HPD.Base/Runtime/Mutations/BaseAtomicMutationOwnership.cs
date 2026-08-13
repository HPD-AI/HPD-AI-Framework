using System.Collections.Immutable;

namespace HPD.Base;

internal static class BaseAtomicMutationOwnership
{
    internal static BaseAtomicMutationPlan FreezePlan(BaseAtomicMutationPlan value) => value with
    {
        PlanDigest = new string(value.PlanDigest.AsSpan()),
        IntentDigest = new string(value.IntentDigest.AsSpan()),
        CaptureDigest = new string(value.CaptureDigest.AsSpan()),
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
        Limits = value.Limits with { },
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
