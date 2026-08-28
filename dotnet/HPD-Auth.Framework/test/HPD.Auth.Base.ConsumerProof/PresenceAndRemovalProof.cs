using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

[BaseRegisteredModuleMutation("proof.presence-and-removal.v1", typeof(ConsumerJsonSerializerContext),
    typeof(PresenceAndRemovalRequest), typeof(PresenceAndRemovalResult), Version = 1,
    OwningModuleId = "proof.module", GrantId = "proof.presence-and-removal.execute")]
internal static partial class PresenceAndRemovalProof
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "proof.presence-and-removal.v1", Version = 1, OwningModuleId = "proof.module",
        GrantId = "proof.presence-and-removal.execute", Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "proof.presence-and-removal.request",
        ResultTypeId = "proof.presence-and-removal.result",
        SystemCollectionIds = [ProofOwner.Collection.Id],
        SystemSourceGrants =
        [
            new BaseModuleSystemSourceGrant { CollectionId = ProofOwner.Collection.Id, GrantId = "proof.owner.source" },
        ],
        GenerationCellIds = [], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [Capture()], Guards = [], Preconditions = [],
            Body = new BaseModuleMutationBlock { Statements = [Removal()] },
            Result = BaseModuleMutationTemplateBuilder.Result(
                BaseModuleMutationTemplateBuilder.ResultObject("presence-result",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.Missing,
                        BaseModuleMutationTemplateBuilder.Missing("missing", ResultProperties.Missing)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.Null,
                        BaseModuleMutationTemplateBuilder.Constant("null", ResultProperties.Null.ConstantAuthority, (string?)null)),
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.Value,
                        BaseModuleMutationTemplateBuilder.LiftOptional("lift-value", ResultProperties.Value,
                            BaseModuleMutationTemplateBuilder.Request("value", RequestProperties.Value))))),
        },
        Limits = IdentityAndGenerationProofLimits.Create() with
        {
            MaximumCaptures = 2, MaximumRecordCaptures = 2, MaximumRelationTargetCaptures = 1,
            MaximumRecordMutations = 1, MaximumRemovedFields = 1, MaximumStatements = 1,
            MaximumExpressionNodes = 32,
        },
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData(
            "proof.presence-and-removal.v1"u8)),
    });

    private static BaseModuleValue<BaseRecordId<ProofOwner>> RecordId() =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<ProofOwner>("record-id",
            BaseModuleMutationTemplateBuilder.Request("record-id-source", RequestProperties.RecordId));

    private static BaseModuleRecordCapture Capture() => BaseModuleMutationTemplateBuilder.CaptureRecord(
        "record", RecordId(), BaseModuleCapturePresence.RequirePresent);

    private static BaseModulePatchStatement Removal() => BaseModuleMutationTemplateBuilder.Patch(
        "remove-note", RecordId(), BaseModuleMutationTemplateBuilder.Object<ProofOwner>("removal",
            BaseModuleMutationTemplateBuilder.Remove(ProofOwner.Fields.Note.ModuleMutation)));
}

internal sealed record PresenceAndRemovalRequest
{
    [BaseField("proof.presence.request.record-id", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256)]
    public required string RecordId { get; init; }

    [BaseField("proof.presence.request.value", MaximumUtf8Bytes = 64)]
    public required string? Value { get; init; }
}

internal sealed record PresenceAndRemovalResult
{
    [BaseField("proof.presence.result.missing", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.Nullable, MaximumUtf8Bytes = 64)]
    public string? Missing { get; init; }

    [BaseField("proof.presence.result.null", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.Nullable, MaximumUtf8Bytes = 64)]
    public string? Null { get; init; }

    [BaseField("proof.presence.result.value", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.Nullable, MaximumUtf8Bytes = 64)]
    public string? Value { get; init; }
}
