using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

[BaseRegisteredModuleMutation("proof.lifecycle-subject.create.v1", typeof(ConsumerJsonSerializerContext),
    typeof(LifecycleSubjectCreateRequest), typeof(LifecycleSubjectCreateResult), Version = 1,
    OwningModuleId = "consumer.module", GrantId = "proof.lifecycle-subject.create.execute")]
internal static partial class LifecycleModuleMutationProof
{
    private const string CreateStatement = "proof.lifecycle-subject.create.statement.create";

    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "proof.lifecycle-subject.create.v1",
        Version = 1,
        OwningModuleId = "consumer.module",
        GrantId = "proof.lifecycle-subject.create.execute",
        Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "proof.lifecycle-subject.create.request",
        ResultTypeId = "proof.lifecycle-subject.create.result",
        SystemCollectionIds = [ConsumerPrivateSubject.Collection.Id],
        SystemSourceGrants =
        [
            new BaseModuleSystemSourceGrant
            {
                CollectionId = ConsumerPrivateSubject.Collection.Id,
                GrantId = "consumer.subject.source",
            },
        ],
        GenerationCellIds = [],
        ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures =
            [
                BaseModuleMutationTemplateBuilder.CaptureRecord(
                    "proof.lifecycle-subject.create.capture.subject",
                    SubjectId("capture"),
                    BaseModuleCapturePresence.RequireMissing),
            ],
            Guards = [],
            Preconditions = [],
            Body = new BaseModuleMutationBlock
            {
                Statements =
                [
                    BaseModuleMutationTemplateBuilder.Create(
                        CreateStatement,
                        SubjectId("create"),
                        BaseModuleMutationTemplateBuilder.Object<ConsumerPrivateSubject>(
                            "proof.lifecycle-subject.create.expression.payload",
                            BaseModuleMutationTemplateBuilder.Field(
                                ConsumerPrivateSubject.Fields.Active,
                                BaseModuleMutationTemplateBuilder.Constant(
                                    "proof.lifecycle-subject.create.expression.active",
                                    ConsumerPrivateSubject.Fields.Active.ConstantAuthority,
                                    true)),
                            BaseModuleMutationTemplateBuilder.Field(
                                ConsumerPrivateSubject.Fields.Tenant,
                                BaseModuleMutationTemplateBuilder.Request(
                                    "proof.lifecycle-subject.create.expression.tenant",
                                    RequestProperties.Tenant)),
                            BaseModuleMutationTemplateBuilder.Field(
                                ConsumerPrivateSubject.Fields.Tombstoned,
                                BaseModuleMutationTemplateBuilder.Constant(
                                    "proof.lifecycle-subject.create.expression.tombstoned",
                                    ConsumerPrivateSubject.Fields.Tombstoned.ConstantAuthority,
                                    false)))),
                ],
            },
            Result = BaseModuleMutationTemplateBuilder.Result(
                BaseModuleMutationTemplateBuilder.ResultObject(
                    "proof.lifecycle-subject.create.expression.result",
                    BaseModuleMutationTemplateBuilder.Property(
                        ResultProperties.Revision,
                        BaseModuleMutationTemplateBuilder.CommittedRevision(
                            "proof.lifecycle-subject.create.expression.result.revision",
                            CreateStatement)))),
        },
        Limits = Limits(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy
        {
            FormatVersion = 1,
            Lifetime = TimeSpan.FromDays(1),
        },
        Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData(
            "proof.lifecycle-subject.create.v1"u8)),
    });

    private static BaseModuleValue<BaseRecordId<ConsumerPrivateSubject>> SubjectId(string usage) =>
        BaseModuleMutationTemplateBuilder.RecordIdFromString<ConsumerPrivateSubject>(
            $"proof.lifecycle-subject.create.expression.subject-id.{usage}",
            BaseModuleMutationTemplateBuilder.Request(
                $"proof.lifecycle-subject.create.expression.subject-id-source.{usage}",
                RequestProperties.SubjectId));

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 1,
        MaximumRecordCaptures = 1,
        MaximumRelationTargetCaptures = 1,
        MaximumGenerationCaptures = 1,
        MaximumRecordMutations = 1,
        MaximumGenerationReads = 1,
        MaximumGenerationComparisons = 1,
        MaximumGenerationIncrements = 1,
        MaximumGuardNodes = 1,
        MaximumGuardDepth = 8,
        MaximumPreconditions = 0,
        MaximumRequestGuardEvaluations = 0,
        MaximumStaticSetMembers = 0,
        MaximumStaticSetComparisons = 0,
        MaximumDisabledCaptures = 0,
        MaximumRemovedFields = 0,
        MaximumStatements = 1,
        MaximumBranches = 1,
        MaximumExpressionNodes = 16,
        MaximumReadIntervals = 16,
        MaximumSubjectValidations = 1,
        MaximumAuthorityReads = 16,
        MaximumRelationChecks = 1,
        MaximumUniqueConstraintChecks = 1,
        MaximumRequestBytes = 4_096,
        MaximumSelectedBytes = 4_096,
        MaximumGenerationBytes = 1,
        MaximumEvidenceBytes = 16_384,
        MaximumWrittenBytes = 4_096,
        MaximumFactBytes = 8_192,
        MaximumJournalBytes = 8_192,
        MaximumReceiptBytes = 16_384,
        MaximumResultBytes = 4_096,
        MaximumTransientBytes = 262_144,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5),
            TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5),
            ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };
}

internal sealed record LifecycleSubjectCreateRequest
{
    [BaseField("proof.lifecycle-subject.create.request.subject-id", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256)]
    public required string SubjectId { get; init; }

    [BaseField("proof.lifecycle-subject.create.request.tenant", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 64)]
    public required string Tenant { get; init; }
}

internal sealed record LifecycleSubjectCreateResult
{
    [BaseField("proof.lifecycle-subject.create.result.revision")]
    public required RevisionToken Revision { get; init; }
}
