using System.Collections.Immutable;
using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

internal sealed class ProofSemanticMarker;

internal static class SemanticProofObservations
{
    private static readonly System.Collections.Concurrent.ConcurrentQueue<object> Values = new();

    internal static void Add(object value) => Values.Enqueue(value);

    internal static object[] Drain()
    {
        var values = new List<object>();
        while (Values.TryDequeue(out object? value)) values.Add(value);
        return [.. values];
    }
}

internal static class SemanticProofRequests
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,
        BaseSubjectReference<ConsumerSubject>> Subjects = new(StringComparer.Ordinal);

    internal static void Retain(string subjectId, BaseSubjectReference<ConsumerSubject> subject) =>
        Subjects[subjectId] = subject;

    internal static bool TryGet(string subjectId, out BaseSubjectReference<ConsumerSubject> subject) =>
        Subjects.TryGetValue(subjectId, out subject);
}

[BaseRegisteredModuleMutation("proof.semantic.ensure.v1", typeof(ConsumerJsonSerializerContext),
    typeof(SemanticProofRequest), typeof(SemanticEnsureProofResult), Version = 1,
    OwningModuleId = "proof.module", GrantId = "proof.semantic.ensure.execute")]
internal static partial class SemanticEnsureProof
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "proof.semantic.ensure.v1", Version = 1, OwningModuleId = "proof.module",
        GrantId = "proof.semantic.ensure.execute",
        Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "proof.semantic.request",
        ResultTypeId = "proof.semantic.ensure.result",
        SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = [],
        ImportedSubjectContractIds = ["consumer.subject"],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [], Guards = StateGuards(), Preconditions = [], Body = StateBody(),
            Result = BaseModuleMutationTemplateBuilder.Result(EnsureResult()),
        },
        Limits = IdentityAndGenerationProofLimits.Create(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData(
            "proof.semantic.ensure.v1"u8)),
    });

    private static ImmutableArray<BaseModuleGuard> StateGuards() =>
    [
        BaseModuleMutationTemplateBuilder.SemanticActivationState("state-absent", BaseModuleSemanticActivationStateTest.CompactedAbsent),
        BaseModuleMutationTemplateBuilder.SemanticActivationState("state-live", BaseModuleSemanticActivationStateTest.Live),
        BaseModuleMutationTemplateBuilder.SemanticActivationState("state-missing", BaseModuleSemanticActivationStateTest.Missing),
        BaseModuleMutationTemplateBuilder.SemanticActivationState("state-retired", BaseModuleSemanticActivationStateTest.Retired),
    ];

    private static BaseModuleMutationBlock StateBody()
    {
        BaseModuleIfStatement retired = BaseModuleMutationTemplateBuilder.If(
            "state-if-retired", "state-retired", Required("require-retired", "state-retired"),
            Required("require-absent", "state-absent"));
        BaseModuleIfStatement live = BaseModuleMutationTemplateBuilder.If(
            "state-if-live", "state-live", Required("require-live", "state-live"),
            new BaseModuleMutationBlock { Statements = [retired] });
        BaseModuleIfStatement missing = BaseModuleMutationTemplateBuilder.If(
            "state-if-missing", "state-missing", Required("require-missing", "state-missing"),
            new BaseModuleMutationBlock { Statements = [live] });
        return new BaseModuleMutationBlock { Statements = [missing] };
    }

    private static BaseModuleMutationBlock Required(string id, string guard) => new()
    {
        Statements = [BaseModuleMutationTemplateBuilder.Require(id, guard, "proof.semantic.state")],
    };

    private static BaseModuleResultObject<SemanticEnsureProofResult> EnsureResult()
    {
        BaseModuleValue<string?> retiredMissing = BaseModuleMutationTemplateBuilder.Missing(
            "activation-id-retired-missing", ResultProperties.ActivationId);
        BaseModuleValue<string?> absentMissing = BaseModuleMutationTemplateBuilder.Missing(
            "activation-id-absent-missing", ResultProperties.ActivationId);
        BaseModuleValue<string?> activationId = BaseModuleMutationTemplateBuilder.SemanticActivationId(
            "activation-id", ResultProperties.ActivationId);
        BaseModuleValue<string?> absentChoice = BaseModuleMutationTemplateBuilder.Conditional(
            "activation-id-absent-choice", "state-absent", absentMissing, activationId);
        BaseModuleValue<string?> resultId = BaseModuleMutationTemplateBuilder.Conditional(
            "activation-id-retired-choice", "state-retired", retiredMissing, absentChoice);
        return BaseModuleMutationTemplateBuilder.ResultObject("ensure-result",
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.ActivationId, resultId),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.Disposition,
                BaseModuleMutationTemplateBuilder.SemanticEnsureDisposition(
                    "ensure-disposition", ResultProperties.Disposition)),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.IncarnationBytes,
                BaseModuleMutationTemplateBuilder.IncarnationBytes(
                    "ensure-incarnation-bytes", ResultProperties.IncarnationBytes,
                    BaseModuleMutationTemplateBuilder.Request(
                        "ensure-incarnation", RequestProperties.Incarnation))),
            BaseModuleMutationTemplateBuilder.Property(ResultProperties.WasMaterialized,
                BaseModuleMutationTemplateBuilder.SemanticActivationWasMaterialized(
                    "ensure-materialized", ResultProperties.WasMaterialized)));
    }
}

[BaseRegisteredModuleMutation("proof.semantic.retire.v1", typeof(ConsumerJsonSerializerContext),
    typeof(SemanticProofRequest), typeof(SemanticRetireProofResult), Version = 1,
    OwningModuleId = "proof.module", GrantId = "proof.semantic.retire.execute")]
internal static partial class SemanticRetireProof
{
    internal static BaseRegisteredModuleMutationDefinition Definition { get; } = BaseModuleMutationContract.Seal(new()
    {
        Id = "proof.semantic.retire.v1", Version = 1, OwningModuleId = "proof.module",
        GrantId = "proof.semantic.retire.execute", Audience = BaseModuleMutationAudience.Service,
        RequestTypeId = "proof.semantic.request", ResultTypeId = "proof.semantic.retire.result",
        SystemCollectionIds = [], SystemSourceGrants = [], GenerationCellIds = [],
        ImportedSubjectContractIds = ["consumer.subject"],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [],
            Guards =
            [
                BaseModuleMutationTemplateBuilder.SemanticActivationState("state-absent", BaseModuleSemanticActivationStateTest.CompactedAbsent),
                BaseModuleMutationTemplateBuilder.SemanticActivationState("state-live", BaseModuleSemanticActivationStateTest.Live),
                BaseModuleMutationTemplateBuilder.SemanticActivationState("state-missing", BaseModuleSemanticActivationStateTest.Missing),
                BaseModuleMutationTemplateBuilder.SemanticActivationState("state-retired", BaseModuleSemanticActivationStateTest.Retired),
            ],
            Preconditions = [], Body = RetireStateBody(),
            Result = BaseModuleMutationTemplateBuilder.Result(
                BaseModuleMutationTemplateBuilder.ResultObject("retire-result",
                    BaseModuleMutationTemplateBuilder.Property(ResultProperties.Disposition,
                        BaseModuleMutationTemplateBuilder.SemanticRetirementDisposition(
                            "retire-disposition", ResultProperties.Disposition)))),
        },
        Limits = IdentityAndGenerationProofLimits.Create(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(System.Security.Cryptography.SHA256.HashData(
            "proof.semantic.retire.v1"u8)),
    });

    private static BaseModuleMutationBlock RetireStateBody()
    {
        BaseModuleMutationBlock Required(string id, string guard) => new()
        {
            Statements = [BaseModuleMutationTemplateBuilder.Require(id, guard, "proof.semantic.retire-state")],
        };
        BaseModuleIfStatement retired = BaseModuleMutationTemplateBuilder.If(
            "retire-if-retired", "state-retired", Required("retire-require-retired", "state-retired"),
            Required("retire-require-absent", "state-absent"));
        BaseModuleIfStatement live = BaseModuleMutationTemplateBuilder.If(
            "retire-if-live", "state-live", Required("retire-require-live", "state-live"),
            new BaseModuleMutationBlock { Statements = [retired] });
        return new BaseModuleMutationBlock
        {
            Statements = [BaseModuleMutationTemplateBuilder.If(
                "retire-if-missing", "state-missing", Required("retire-reject-missing", "state-live"),
                new BaseModuleMutationBlock { Statements = [live] })],
        };
    }
}

internal static class SemanticProof
{
    internal const string DefinitionId = "proof.semantic.slot.v1";
    internal const string EnsureGrant = "proof.semantic.slot.ensure";
    internal const string RetireGrant = "proof.semantic.slot.retire";
    internal const string MaintainGrant = "proof.semantic.slot.maintain";
    internal const string LifecycleRetirementGrant = "proof.semantic.slot.subject-retirement";

    internal static BaseSemanticActivationKeyExpression Expression { get; } =
        BaseSemanticActivationKeyBuilder.Tuple(
            BaseSemanticActivationKeyBuilder.Property(SemanticEnsureProof.RequestProperties.SubjectId, 256),
            BaseSemanticActivationKeyBuilder.Property(SemanticEnsureProof.RequestProperties.Incarnation, 64));

    private static BaseSemanticActivationRegistration<SemanticProofRequest, ProofSemanticMarker>? _registration;
    internal static bool EnableCompaction { get; set; }
    internal static BaseSemanticActivationRegistration<SemanticProofRequest, ProofSemanticMarker> Registration =>
        _registration ??= CreateRegistration();
    internal static BaseSemanticActivationKeyDefinition Definition => Registration.Definition;
    internal static BaseSemanticActivationKeyIdentity<SemanticProofRequest, ProofSemanticMarker> Identity =>
        Registration.KeyIdentity;

    private static BaseSemanticActivationRegistration<SemanticProofRequest, ProofSemanticMarker> CreateRegistration()
    {
        var draft = new BaseSemanticActivationKeyDefinition
        {
            Id = DefinitionId, Version = 1, OwningApplicationId = "hpd.auth.base.consumer-proof",
            OwningModuleId = "proof.module",
            EnsureOperation = new()
            {
                OperationId = SemanticEnsureProof.Definition.Id, OperationVersion = 1,
                OperationChecksum = Convert.ToHexStringLower(SemanticEnsureProof.Definition.Checksum.ToArray()),
            },
            RetirementOperation = new()
            {
                OperationId = SemanticRetireProof.Definition.Id, OperationVersion = 1,
                OperationChecksum = Convert.ToHexStringLower(SemanticRetireProof.Definition.Checksum.ToArray()),
            },
            Activation = new()
            {
                Id = ProofActivation.Registration.Definition.Id, Version = ProofActivation.Registration.Definition.Version,
                Checksum = ProofActivation.Registration.Definition.Checksum,
            },
            ScopeKind = BaseSubjectScopeKind.Tenant, EnsureGrantId = EnsureGrant,
            RetirementGrantId = RetireGrant, MaintenanceGrantId = MaintainGrant,
            Compaction = EnableCompaction
                ? BaseGeneratedSemanticActivationCompactions.SubjectRetirement(
                    ConsumerSubject.HPDBaseSubjectRegistration,
                    SemanticEnsureProof.RequestProperties.Subject,
                    LifecycleRetirementGrant)
                : new BaseSemanticActivationNoCompaction(), RequestTypeId = "proof.semantic.request",
            RequestSerializerChecksum = [], KeyExpressionChecksum = [],
            Limits = new BaseSemanticActivationLimits
            {
                MaximumCanonicalKeyBytes = 256, MaximumLiveSlots = 32,
                MaximumRetiredSlots = 32, MaximumAbsenceMarkers = 32,
                Execution = new BaseSemanticActivationExecutionLimits
                {
                    MaximumOperations = 1, MaximumScopeDirectoryReads = 1, MaximumSlotReads = 1,
                    MaximumActivationReads = 1, MaximumReadIntervals = 4, MaximumIndexOperations = 8,
                    MaximumActivationBytes = 4096, MaximumScopeDirectoryBytes = 1024,
                    MaximumEvidenceBytes = 8192, MaximumReceiptBytes = 8192,
                    MaximumTransientBytes = 1_048_576,
                },
                Deadlines = new BaseSemanticActivationDeadlineCapability
                {
                    AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
                    CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
                    MaintenanceTimeout = TimeSpan.FromSeconds(5), QuarantineRetentionTimeout = TimeSpan.FromSeconds(5),
                },
            },
            Checksum = [],
        };
        return BaseSemanticActivationDeclarationBuilder.Create<SemanticProofRequest,
            SemanticEnsureProofResult, SemanticRetireProofResult, ProofSemanticMarker>(
            draft, SemanticEnsureProof.Identity, SemanticRetireProof.Identity,
            new ConsumerJsonSerializerContext(BaseSerializerGeneratedContract.CreateOptions(
                System.Text.Json.JsonNamingPolicy.CamelCase)).SemanticProofRequest,
            Expression);
    }
}

internal sealed record SemanticProofRequest
{
    [BaseField("proof.semantic.request.subject")]
    [BaseSubjectReference(typeof(ConsumerSubject), Requirement = BaseSubjectReferenceRequirement.Exists,
        Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot)]
    public required BaseSubjectReference<ConsumerSubject> Subject { get; init; }

    [BaseField("proof.semantic.request.subject-id")]
    public required BaseRecordId<ConsumerPrivateSubject> SubjectId { get; init; }

    [BaseField("proof.semantic.request.incarnation")]
    public required BaseSubjectIncarnation Incarnation { get; init; }
}

internal sealed record SemanticEnsureProofResult
{
    [BaseField("proof.semantic.ensure.result.disposition", AllowedEnumLiterals = ["created", "existing", "retired"])]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<BaseSemanticActivationEnsureDisposition>))]
    public required BaseSemanticActivationEnsureDisposition Disposition { get; init; }

    [BaseField("proof.semantic.ensure.result.activation-id", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.NonNullable, MaximumUtf8Bytes = 256)]
    public string? ActivationId { get; init; }

    [BaseField("proof.semantic.ensure.result.materialized")]
    public required bool WasMaterialized { get; init; }

    [BaseField("proof.semantic.ensure.result.incarnation-bytes", MinimumBytes = 24, MaximumBytes = 24)]
    public required BaseBinary IncarnationBytes { get; init; }
}

internal sealed record SemanticRetireProofResult
{
    [BaseField("proof.semantic.retire.result.disposition",
        AllowedEnumLiterals = ["alreadyCompacted", "alreadyRetired", "retiredNow"])]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<BaseSemanticActivationRetirementDisposition>))]
    public required BaseSemanticActivationRetirementDisposition Disposition { get; init; }
}
