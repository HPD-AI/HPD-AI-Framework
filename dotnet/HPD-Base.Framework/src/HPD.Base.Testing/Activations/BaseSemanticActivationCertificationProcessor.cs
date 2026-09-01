using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base.Testing;

/// <summary>Executes one real provider-neutral semantic activation transaction for certification.</summary>
internal sealed class BaseSemanticActivationCertificationProcessor(
    BaseAtomicMutationAuthorityRequirement authority,
    BaseAtomicMutationExecutionLimits limits,
    string logicalStoreId,
    string parentIdentity,
    bool retire = false,
    BaseSemanticActivationExecutionLimits? semanticLimits = null,
    long acceptedTime = 1,
    string semanticKey = "certification-subject",
    BaseSemanticActivationSubjectLifetimeBinding? subjectLifetime = null,
    BaseSemanticActivationKeyDefinition? installedDefinition = null)
    : IBaseSemanticActivationCertificationProcessor
{
    public bool ContainsSemanticActivation => true;
    private static readonly byte[] ActivationChecksum = SHA256.HashData("certification-activation-definition"u8);
    private readonly byte[] canonicalKey = Encoding.UTF8.GetBytes(semanticKey);
    private static readonly byte[] ProposedBinding =
        BaseSemanticActivationCertificationSubjectAuthority.ScopeBindingId.ToArray();
    private static readonly byte[] CompletionChecksum = SHA256.HashData("certification-completion-operation"u8);
    private static readonly byte[] SubjectContractChecksum = Convert.FromHexString(
        BaseSemanticActivationCertificationSubjectAuthority.Registration.Checksum);

    internal static ImmutableArray<byte> InstalledDefinitionSetChecksum =>
        InstalledDefinition(DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(
            BaseModuleMutationPlatform.MaximumLimits)).Checksum;

    internal static BaseSemanticActivationSubjectLifetimeBinding SubjectLifetime(int ordinal)
    {
        byte[] epoch = SHA256.HashData(Encoding.UTF8.GetBytes($"certification-subject-epoch:{ordinal}"))[..16];
        byte[] incarnation = new byte[24];
        BinaryPrimitives.WriteInt64BigEndian(incarnation, 1);
        SHA256.HashData(Encoding.UTF8.GetBytes($"certification-subject-incarnation:{ordinal}"))[..16]
            .CopyTo(incarnation, 8);
        var value = new BaseSemanticActivationSubjectLifetimeBinding
        {
            ContractId = "certification.subject", ContractVersion = 1,
            ContractChecksum = SubjectContractChecksum.ToImmutableArray(),
            SubjectId = BaseSubjectId.Create($"subject-{ordinal}", BaseSubjectIdKind.OrdinalString),
            AuthorityEpoch = new BaseSubjectAuthorityEpoch(epoch),
            Incarnation = new BaseSubjectIncarnation(incarnation),
            ScopeBindingId = ProposedBinding.ToImmutableArray(), Checksum = [],
        };
        return value with { Checksum = BaseSemanticActivationEvidenceContract.SubjectLifetimeChecksum(value) };
    }

    internal static BaseSemanticActivationKeyDefinition InstalledDefinition(
        BaseAtomicMutationExecutionLimits atomicLimits,
        BaseSemanticActivationExecutionLimits? executionLimits = null) =>
        BaseSemanticActivationDefinitionContract.Seal(new()
    {
        Id = "certification.semantic", Version = 1, OwningApplicationId = "certification-application",
        OwningModuleId = "certification",
        EnsureOperation = new BaseSemanticActivationModuleOperationIdentity
        {
            OperationId = "certification.semantic.ensure", OperationVersion = 1,
            OperationChecksum = Convert.ToHexStringLower(CompletionChecksum),
        },
        RetirementOperation = CompletionOperation(),
        Activation = new BaseActivationDefinitionKey
        {
            Id = "certification.activation", Version = 1, Checksum = ActivationChecksum.ToImmutableArray(),
        },
        ScopeKind = BaseSubjectScopeKind.Global, EnsureGrantId = "certification.semantic.ensure",
        RetirementGrantId = "certification.semantic.retire", MaintenanceGrantId = "certification.semantic.maintain",
        Compaction = new BaseSemanticActivationSubjectRetirementCompaction(
            new BaseSemanticActivationSubjectContractIdentity(
                "certification.subject", 1, SubjectContractChecksum.ToImmutableArray()),
            "subject", "certification.subject.retire"),
        RequestTypeId = "certification.request",
        RequestSerializerChecksum = SHA256.HashData("certification-request"u8).ToImmutableArray(),
        KeyExpressionChecksum = SHA256.HashData("certification-key-expression"u8).ToImmutableArray(),
        Limits = new BaseSemanticActivationLimits
        {
            MaximumCanonicalKeyBytes = 256, MaximumLiveSlots = 100, MaximumRetiredSlots = 100,
            MaximumAbsenceMarkers = 100, Execution = executionLimits ?? SemanticLimits(), Deadlines = new BaseSemanticActivationDeadlineCapability
            {
                AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
                CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
                MaintenanceTimeout = TimeSpan.FromSeconds(5), QuarantineRetentionTimeout = TimeSpan.FromSeconds(5),
            },
        },
        Checksum = [],
    });

    internal static BaseSemanticActivationKeyDefinition InstalledDefinitionV2(
        BaseAtomicMutationExecutionLimits atomicLimits,
        BaseSemanticActivationExecutionLimits? executionLimits = null) =>
        BaseSemanticActivationDefinitionContract.Seal(
            InstalledDefinition(atomicLimits, executionLimits) with { Version = 2, Checksum = [] });

    internal static BaseSemanticActivationMigrationDefinition InstalledMigration(
        BaseAtomicMutationExecutionLimits atomicLimits,
        BaseSemanticActivationExecutionLimits? executionLimits = null) =>
        BaseSemanticActivationMigrationContract.Seal(new()
    {
        Id = "certification.semantic.migration",
        Version = 1,
        From = DefinitionKey(InstalledDefinition(atomicLimits, executionLimits)),
        To = DefinitionKey(InstalledDefinitionV2(atomicLimits, executionLimits)),
        Checksum = [],
    });

    internal static BaseSemanticActivationRemovalAuthority InstalledRemoval(
        BaseAtomicMutationExecutionLimits atomicLimits,
        BaseSemanticActivationExecutionLimits? executionLimits = null) =>
        BaseSemanticActivationRemovalAuthorityContract.Seal(new()
    {
        Id = "certification.semantic.removal",
        Version = 1,
        From = InstalledDefinition(atomicLimits, executionLimits),
        ResultingDefinitionSetChecksum = InstalledDefinitionV2(atomicLimits, executionLimits).Checksum,
        Checksum = [],
    });

    private static BaseSemanticActivationDefinitionKey DefinitionKey(
        BaseSemanticActivationKeyDefinition value) => new()
    {
        Id = value.Id,
        Version = value.Version,
        Checksum = value.Checksum,
    };

    public ImmutableArray<byte> ParentActivationAuthorityChecksum { get; } =
        BoundHash("base.semanticActivation.certificationParent.v2\0", Encoding.UTF8.GetBytes(parentIdentity)).ToImmutableArray();

    public ImmutableArray<byte> SemanticIntentChecksum { get; } =
        BoundHash("base.semanticActivation.certificationIntent.v2\0", Encoding.UTF8.GetBytes(logicalStoreId),
            DefinitionChecksumFor(installedDefinition), Encoding.UTF8.GetBytes(semanticKey), ProposedBinding,
            [retire ? (byte)2 : (byte)1]).ToImmutableArray();

    internal BaseProvisionalSemanticActivation? Provisional { get; private set; }
    internal BaseCapturedSemanticActivationEvidence? Captured { get; private set; }
    internal BaseSemanticActivationAccounting? PreparedAccounting { get; private set; }
    internal byte[]? RecoveryReceiptJson { get; private set; }
    internal string? FailureStage { get; private set; }

    public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseAtomicReceiptResult committedResult, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(committedResult.Kind == BaseAtomicReceiptResultKind.ModuleMutation
            && committedResult.ModuleMutation?.SemanticActivation is not null
            ? new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, committedResult)
            : Failure(null));
    }

    public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseRecordMutationFact[] committedMutations, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Failure(null));
    }

    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession session, CancellationToken cancellationToken = default)
    {
        BaseSemanticActivationDefinitionIdentity definition = Definition(installedDefinition);
        byte[] definitionChecksum = definition.Checksum.ToArray();
        BaseOwnedSubjectScopeEvidence scope = new() { Kind = BaseSubjectScopeKind.Global };
        BaseSemanticActivationDueAuthority due = new()
        {
            Mode = BaseSemanticActivationDueMode.ExplicitUtcInstant,
            CanonicalUnixMilliseconds = 1,
        };
        BaseSemanticActivationKeyDigest key = BaseSemanticActivationKeyDigest.Create(
            BoundHash("base.semanticActivation.key.v1\0", Encoding.UTF8.GetBytes(definition.Id), ProposedBinding, canonicalKey));
        byte[] activationId = BoundHash("base.semanticActivation.activation.v1\0",
            Encoding.UTF8.GetBytes(authority.ApplicationId), Encoding.UTF8.GetBytes(authority.StoreInstanceId),
            "certification"u8.ToArray(), "certification.semantic"u8.ToArray(), ProposedBinding, canonicalKey);
        byte[] creationChecksum = BoundHash("base.semanticActivation.creation.v1\0",
            definitionChecksum, key.ToArray(), ProposedBinding, activationId);
        BaseSemanticActivationOperation operation = retire
            ? new BaseSemanticActivationRetireIntent
            {
                Definition = definition, Key = key, CanonicalKey = canonicalKey.ToImmutableArray(), Scope = scope,
                SubjectLifetime = subjectLifetime,
                CompletionOperation = CompletionOperation(),
            }
            : new BaseSemanticActivationEnsureIntent
            {
                Definition = definition, Key = key, CanonicalKey = canonicalKey.ToImmutableArray(), Scope = scope, Due = due,
                SubjectLifetime = subjectLifetime,
                Activation = new BaseSemanticActivationCreateIntent
                {
                    Definition = new BaseActivationDefinitionKey
                    {
                        Id = "certification.activation", Version = 1, Checksum = ActivationChecksum.ToImmutableArray(),
                    },
                    ReceiptRetention = DefaultReceiptRetention(),
                    CanonicalInput = "certification-payload"u8.ToArray().ToImmutableArray(),
                    InputChecksum = SHA256.HashData("certification-payload"u8).ToImmutableArray(), Scope = scope, Due = due,
                    Priority = 0, InitiallyEligible = true, Limits = ActivationLimits(),
                    Identity = new BaseSemanticActivationCreationIdentity
                    {
                        SemanticDefinition = definition, Key = key, ScopeBindingId = ProposedBinding.ToImmutableArray(),
                        DerivedActivationIdBytes = activationId.ToImmutableArray(), Checksum = creationChecksum.ToImmutableArray(),
                    },
                },
            };
        BaseSemanticActivationExecutionLimits effectiveLimits = semanticLimits ?? SemanticLimits();
        BaseSemanticActivationStoreAuthorityRequirement installedAuthority = authority.SemanticActivation
            ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        BaseAtomicSemanticActivationExtension extension = new()
        {
            Capture = new BaseSemanticActivationCaptureRequest
            {
                Definition = definition, CanonicalKey = canonicalKey.ToImmutableArray(),
                KeyPreimageChecksum = SHA256.HashData(canonicalKey).ToImmutableArray(), Scope = scope,
                ProposedScopeBindingId = ProposedBinding.ToImmutableArray(),
                Operation = retire ? BaseSemanticActivationOperationKind.Retire : BaseSemanticActivationOperationKind.Ensure,
                StoreAuthority = new BaseSemanticActivationStoreAuthorityRequirement
                {
                    ApplicationId = authority.ApplicationId, LogicalStoreId = logicalStoreId,
                    StoreInstanceId = authority.StoreInstanceId, RestoreEpoch = authority.RestoreEpoch,
                    SchemaGeneration = authority.SchemaGeneration,
                    SemanticAuthorityGeneration = installedAuthority.SemanticAuthorityGeneration,
                    DefinitionSetChecksum = installedAuthority.DefinitionSetChecksum.ToArray().ToImmutableArray(),
                },
                Limits = effectiveLimits, AcceptedTime = AcceptedTime(),
            },
            Operation = operation,
            StructuralDigest = BoundHash("base.semanticActivation.extension.v1\0", definitionChecksum,
                canonicalKey, ProposedBinding, [retire ? (byte)2 : (byte)1]).ToImmutableArray(),
        };
        BaseAtomicExecutionRequest request = new()
        {
            Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
            Intent = new BaseAtomicMutationIntent { IntentDigest = parentIdentity, Authority = authority, Items = [] },
            Module = new BaseModuleMutationCaptureExtension
            {
                OperationId = retire ? "certification.semantic.retire" : "certification.semantic.ensure",
                OperationVersion = 1, OperationChecksum = Convert.ToHexStringLower(CompletionChecksum),
                RequestDigest = parentIdentity, Records = [], RelationTargets = [], Generations = [],
            },
            SemanticActivation = extension, Limits = limits,
        };
        OperationResult<BaseCapturedAtomicExecution> capture =
            await session.CaptureAtomicExecutionAsync(request, cancellationToken).ConfigureAwait(false);
        if (!capture.IsSuccess() || capture.Value?.SemanticActivation is null) { FailureStage = "capture:" + capture.Error?.Code; return Failure(capture.Error); }
        if (!BaseModuleMutationProcessor<object, object>.CapturedSemanticMatches(
                extension, capture.Value.SemanticActivation))
        { FailureStage = "capture-correspondence"; return Failure(null); }
        Captured = capture.Value.SemanticActivation;
        BaseAtomicSemanticActivationExtension finalizedSemantic =
            BaseModuleMutationProcessor<object, object>.FinalizeSemantic(extension, Captured)
            ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        BaseFinalizedAtomicExecutionPlan plan = new()
        {
            Kind = request.Kind, PlanDigest = "certification-plan:" + parentIdentity,
            IntentDigest = request.Intent.IntentDigest, CaptureDigest = capture.Value.CaptureDigest,
            PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]), Authority = authority,
            Items = [], SubjectValidations = [], Limits = limits, SemanticActivation = finalizedSemantic,
            Module = new BaseFinalizedModuleMutationExtension
            {
                OperationId = request.Module.OperationId, OperationVersion = request.Module.OperationVersion,
                OperationChecksum = request.Module.OperationChecksum, Decisions = [], ItemBindings = [], RelationTargets = [],
                Comparisons = [], Increments = [], ResultProjectionDigest = parentIdentity,
            },
        };
        OperationResult<BasePreparedAtomicExecution> prepared =
            await session.PrepareAtomicExecutionAsync(capture.Value, plan, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess() || prepared.Value?.SemanticActivation is null) { FailureStage = "prepare:" + prepared.Error?.Code; return Failure(prepared.Error); }
        PreparedAccounting = prepared.Value.SemanticActivation.Accounting;
        OperationResult<BaseProvisionalAtomicExecution> applied =
            await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken).ConfigureAwait(false);
        if (!applied.IsSuccess() || applied.Value?.SemanticActivation is null) { FailureStage = "apply:" + applied.Error?.Code; return Failure(applied.Error); }
        Provisional = applied.Value.SemanticActivation;
        BaseSemanticActivationReceiptEvidence semanticReceipt =
            BaseModuleMutationProcessor<object, object>.CreateSemanticReceipt(finalizedSemantic, Captured, Provisional)
            ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        BaseModuleMutationReceiptResult moduleReceipt = new()
        {
            OperationId = request.Module.OperationId, OperationVersion = request.Module.OperationVersion,
            Disposition = BaseMutationRequestDisposition.Committed, Outcome = BaseModuleMutationOutcome.Committed,
            Generations = [], CanonicalResultBytes = [], SemanticActivation = semanticReceipt,
        };
        var receipt = new BaseAtomicReceiptResult
        {
            Kind = BaseAtomicReceiptResultKind.ModuleMutation,
            Mutations = applied.Value.Facts.Select(static fact =>
                BaseOwnedMutationFact.FromCanonicalBytes(fact.CopyCanonicalBytes(), fact.CodecVersion)).ToImmutableArray(),
            ModuleMutation = moduleReceipt,
        };
        RecoveryReceiptJson = JsonSerializer.SerializeToUtf8Bytes(
            BaseAtomicReceiptWire.From(receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        long receiptBytes = RecoveryReceiptJson.LongLength;
        BaseProvisionalAtomicMutationAccounting prior = applied.Value.Accounting;
        return new AtomicMutationProcessingResult(new BaseAtomicMutationCommitFinalization
        {
            PlanDigest = applied.Value.PlanDigest, Receipt = receipt, CanonicalResultBytes = [],
            Accounting = new BaseAtomicCommitAccounting
            {
                WrittenBytes = prior.WrittenBytes, GenerationBytes = prior.GenerationBytes,
                FactBytes = prior.FactBytes, JournalBytes = prior.JournalBytes, ReceiptBytes = receiptBytes,
                ResultBytes = 0, RelationChecks = prior.RelationChecks,
                UniqueConstraintChecks = prior.UniqueConstraintChecks, AuthorityReads = prior.AuthorityReads,
                ReadIntervals = prior.ReadIntervals, SelectedBytes = prior.SelectedBytes,
                EvidenceBytes = prior.EvidenceBytes,
                TransientBytes = checked(prior.TransientBytes + receiptBytes),
                RetirementBarrierReads = prior.RetirementBarrierReads,
                RetirementAcknowledgementReads = prior.RetirementAcknowledgementReads,
                RetirementProjections = prior.RetirementProjections,
                RetirementPublications = prior.RetirementPublications,
                RetirementEvidenceBytes = prior.RetirementEvidenceBytes,
                RetirementPublicationBytes = prior.RetirementPublicationBytes,
            },
        });
    }

    internal static BaseSemanticActivationExecutionLimits SemanticLimits() => new()
    {
        MaximumOperations = 1, MaximumScopeDirectoryReads = 1, MaximumSlotReads = 1, MaximumActivationReads = 1,
        MaximumReadIntervals = 8, MaximumIndexOperations = 4096, MaximumActivationBytes = 4096,
        MaximumScopeDirectoryBytes = 4096, MaximumEvidenceBytes = 16384, MaximumReceiptBytes = 4096,
        MaximumTransientBytes = 262144,
    };

    internal static BaseSemanticActivationDefinitionIdentity Definition(
        BaseSemanticActivationKeyDefinition? installedDefinition = null) => new()
    {
        Id = installedDefinition?.Id ?? "certification.semantic",
        Version = installedDefinition?.Version ?? 1,
        Checksum = DefinitionChecksumFor(installedDefinition).ToImmutableArray(),
        OwnerGeneration = 1, OwningModuleId = "certification", RetirementOperation = CompletionOperation(),
    };

    private static byte[] DefinitionChecksumFor(BaseSemanticActivationKeyDefinition? installedDefinition) =>
        (installedDefinition ?? InstalledDefinition(
            DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(
                BaseModuleMutationPlatform.MaximumLimits))).Checksum.ToArray();

    private static BaseSemanticActivationModuleOperationIdentity CompletionOperation() => new()
    {
        OperationId = "certification.semantic.retire", OperationVersion = 1,
        OperationChecksum = Convert.ToHexStringLower(CompletionChecksum),
    };

    private BaseAcceptedTimeReceipt AcceptedTime()
    {
        long captured = acceptedTime, monotonic = acceptedTime, sequence = checked(acceptedTime + 1);
        const long skew = 30_000;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "base.activation.acceptedTime.v2\0"); Append(hash, authority.ApplicationId);
        Append(hash, 1L); Append(hash, captured); Append(hash, monotonic); Append(hash, sequence); Append(hash, skew);
        return new BaseAcceptedTimeReceipt(authority.ApplicationId, 1, captured, monotonic, sequence, skew, hash.GetHashAndReset());
    }

    private BaseActivationLimits ActivationLimits() => new()
    {
        MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 3, MaximumYields = 0,
        MaximumRenewalsPerSlice = 3, MaximumChildrenPerSlice = 8, MaximumLineageDepth = 8,
        LeaseDuration = TimeSpan.FromMinutes(1), HandlerTimeout = TimeSpan.FromMinutes(1),
        Provider = new BaseActivationExecutionLimits
        {
            MaximumCandidates = 8, MaximumInputBytes = 4096, MaximumResultBytes = 4096,
            MaximumEvidenceBytes = 4096, MaximumTransientBytes = 16384, MaximumReadIntervals = 8,
            MaximumIndexOperations = 16, AcquisitionTimeout = TimeSpan.FromSeconds(5),
            TransactionTimeout = TimeSpan.FromSeconds(5), CommitObservationTimeout = TimeSpan.FromSeconds(5),
            ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
        AtomicCreation = limits,
    };

    private static BaseActivationReceiptRetentionPolicy DefaultReceiptRetention() => new()
    {
        FormatVersion = 1,
        DuplicateResolutionLifetime = TimeSpan.FromHours(24),
        ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
    };

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length)); hash.AppendData(length); hash.AppendData(bytes);
    }

    private static void Append(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes);
    }

    private static AtomicMutationProcessingResult Failure(BaseError? error) => new(
        AtomicMutationProcessingOutcome.Failed, [], error is null
            || error.Code == BaseSubjectErrorCodes.ProviderContractInvalid ? new BaseError
        {
            Code = BaseSemanticActivationErrorCodes.ProviderContractInvalid,
            Message = "The semantic activation provider evidence was invalid.", Category = ErrorCategory.Unsupported,
        } : error);

    private static byte[] BoundHash(string purpose, params byte[][] parts)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(purpose)); Span<byte> length = stackalloc byte[4];
        foreach (byte[] part in parts)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, part.Length); hash.AppendData(length); hash.AppendData(part);
        }
        return hash.GetHashAndReset();
    }
}
