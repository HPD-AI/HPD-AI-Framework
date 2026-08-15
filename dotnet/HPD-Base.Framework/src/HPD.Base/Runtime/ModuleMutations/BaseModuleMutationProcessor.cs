using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed class BaseModuleMutationProcessor<TRequest, TResult>(
    BaseRegisteredModuleMutationDefinition definition,
    BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity,
    TRequest request,
    BaseAtomicMutationIntent intent,
    BaseModuleMutationCaptureExtension extension,
    BaseAtomicMutationExecutionLimits limits,
    IReadOnlyDictionary<string, CollectionDefinition> collections) : IAtomicMutationProcessor
{
    internal BaseModuleMutationExecutionResult<TResult>? Result { get; private set; }

    public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession provider,
        CancellationToken cancellationToken = default)
    {
        var captureRequest = new BaseAtomicMutationCaptureRequest
        {
            Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
            Intent = intent,
            Module = extension,
            Limits = limits,
        };
        OperationResult<BaseCapturedAtomicMutationAuthority> captured = await provider
            .CaptureAtomicMutationAuthorityAsync(captureRequest, cancellationToken).ConfigureAwait(false);
        if (!captured.IsSuccess() || captured.Value is null)
            return Failed(captured.Error ?? Error(BaseModuleMutationErrorCodes.StoreError, ErrorCategory.Store));
        BaseCapturedAtomicMutationAuthority evidence = captured.Value;
        if (!CapturedMatches(evidence))
            return Failed(Error("base.moduleMutation.captureEvidenceInvalid", ErrorCategory.Store));

        var evaluator = new BaseModuleProgramEvaluator<TRequest, TResult>(definition, identity, request, evidence, collections);
        var increments = ImmutableArray.CreateBuilder<BaseModuleGenerationIncrement>();
        var comparisons = ImmutableArray.CreateBuilder<BaseModuleGenerationComparison>();
        foreach (BaseModuleGenerationCaptureRequest generation in extension.Generations)
        {
            if (generation.Absence == BaseModuleGenerationAbsenceBehavior.RequireExisting)
                comparisons.Add(new BaseModuleGenerationComparison { CaptureOrdinal = generation.Ordinal, Kind = BaseModuleGenerationComparisonKind.MustExist });
            else if (generation.Absence == BaseModuleGenerationAbsenceBehavior.RequireMissing)
                comparisons.Add(new BaseModuleGenerationComparison { CaptureOrdinal = generation.Ordinal, Kind = BaseModuleGenerationComparisonKind.MustBeMissing });
        }
        try
        {
            if (!EvaluateBlock(definition.Template.Body, evaluator, increments, out BaseError? programError))
                return Failed(programError!);
        }
        catch (OverflowException) { return Failed(Error(BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation)); }
        catch { return Failed(Error("base.moduleMutation.programInvalid", ErrorCategory.Validation)); }

        string planDigest = Digest(extension.RequestDigest, evidence.CaptureDigest,
            string.Join(';', evaluator.Decisions.Select(static value => $"{value.EvaluationOrdinal}:{value.Kind}:{value.DecisionId}:{value.SelectedTrue}")),
            string.Join(';', increments.Select(static value => $"{value.CaptureOrdinal}:{value.CreateIfAbsent}")));
        var plan = new BaseAtomicMutationPlan
        {
            Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
            IntentDigest = intent.IntentDigest,
            CaptureDigest = evidence.CaptureDigest,
            Authority = intent.Authority,
            Items = [],
            SubjectValidations = [],
            Module = new BaseFinalizedModuleMutationExtension
            {
                OperationId = definition.Id, OperationVersion = definition.Version,
                OperationChecksum = extension.OperationChecksum, Decisions = evaluator.Decisions,
                ItemBindings = [], Comparisons = comparisons.ToImmutable(), Increments = increments.ToImmutable(),
                ResultProjectionDigest = Digest(definition.Id, "result", Convert.ToHexString(definition.Checksum.ToArray())),
            },
            Limits = limits,
            PlanDigest = planDigest,
        };
        OperationResult<BasePreparedAtomicMutation> prepared = await provider
            .PrepareAtomicMutationAsync(evidence, plan, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess() || prepared.Value is null || !PreparedMatches(plan, evidence, prepared.Value))
            return Failed(prepared.Error ?? Error("base.moduleMutation.preparedEvidenceInvalid", ErrorCategory.Store));
        OperationResult<BaseProvisionalAppliedAtomicMutation> applied = await provider
            .ApplyPreparedAtomicMutationAsync(prepared.Value, cancellationToken).ConfigureAwait(false);
        if (!applied.IsSuccess() || applied.Value is null || !AppliedMatches(plan, applied.Value))
            return Failed(applied.Error ?? Error("base.moduleMutation.appliedEvidenceInvalid", ErrorCategory.Store));

        IReadOnlyDictionary<string, BaseModuleCommittedGeneration> committedGenerations = applied.Value.Generations
            .ToDictionary(static value => value.CaptureId, StringComparer.Ordinal);
        TResult typed;
        ImmutableArray<byte> resultBytes;
        try { typed = evaluator.ProjectResult(definition.Template.Result, new Dictionary<string, BaseRecordMutationFact>(), committedGenerations, out resultBytes); }
        catch { return Failed(Error("base.moduleMutation.resultInvalid", ErrorCategory.Validation)); }
        var moduleReceipt = new BaseModuleMutationReceiptResult
        {
            OperationId = definition.Id, OperationVersion = definition.Version,
            Disposition = BaseMutationRequestDisposition.Committed, Outcome = BaseModuleMutationOutcome.Committed,
            Generations = applied.Value.Generations.Select(static value => value with { }).ToImmutableArray(),
            CanonicalResultBytes = resultBytes.ToArray().ToImmutableArray(),
        };
        var receipt = new BaseAtomicReceiptResult
        {
            Kind = BaseAtomicReceiptResultKind.ModuleMutation,
            Mutations = applied.Value.Facts.Select(static value => BaseOwnedMutationFact.FromCanonicalBytes(value.CopyCanonicalBytes(), value.CodecVersion)).ToImmutableArray(),
            ModuleMutation = moduleReceipt,
        };
        long receiptBytes = JsonSerializer.SerializeToUtf8Bytes(
            BaseAtomicReceiptWire.From(receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire).LongLength;
        BaseProvisionalAtomicMutationAccounting prior = applied.Value.Accounting;
        long transient = checked(prior.TransientBytes + receiptBytes + resultBytes.Length);
        if (receiptBytes > limits.MaximumReceiptBytes || resultBytes.Length > limits.MaximumResultBytes || transient > limits.MaximumTransientBytes)
            return Failed(Error(BaseModuleMutationErrorCodes.LimitExceeded, ErrorCategory.Validation));
        var finalization = new BaseAtomicMutationCommitFinalization
        {
            PlanDigest = plan.PlanDigest, Receipt = receipt, CanonicalResultBytes = resultBytes,
            Accounting = new BaseAtomicCommitAccounting
            {
                WrittenBytes = prior.WrittenBytes, GenerationBytes = prior.GenerationBytes, FactBytes = prior.FactBytes,
                JournalBytes = prior.JournalBytes, ReceiptBytes = receiptBytes, ResultBytes = resultBytes.Length,
                RelationChecks = prior.RelationChecks, UniqueConstraintChecks = prior.UniqueConstraintChecks,
                AuthorityReads = prior.AuthorityReads, ReadIntervals = prior.ReadIntervals,
                SelectedBytes = prior.SelectedBytes, EvidenceBytes = prior.EvidenceBytes, TransientBytes = transient,
            },
        };
        Result = new BaseModuleMutationExecutionResult<TResult>
        {
            Disposition = BaseMutationRequestDisposition.Committed,
            Outcome = BaseModuleMutationOutcome.Committed,
            Result = typed,
        };
        return new AtomicMutationProcessingResult(finalization);
    }

    public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseAtomicReceiptResult committedResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseModuleMutationReceiptResult? module = committedResult.ModuleMutation;
        if (committedResult.Kind != BaseAtomicReceiptResultKind.ModuleMutation || module is null
            || !string.Equals(module.OperationId, definition.Id, StringComparison.Ordinal)
            || module.OperationVersion != definition.Version)
            return ValueTask.FromResult(Failed(Error(BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization)));
        try
        {
            TResult? typed = JsonSerializer.Deserialize(module.CanonicalResultBytes.AsSpan(), identity.ResultTypeInfo);
            if (typed is null) throw new JsonException();
            Result = new BaseModuleMutationExecutionResult<TResult>
            {
                Disposition = BaseMutationRequestDisposition.Duplicate,
                Outcome = BaseModuleMutationOutcome.Duplicate,
                Result = typed,
            };
            return ValueTask.FromResult(new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, committedResult));
        }
        catch { return ValueTask.FromResult(Failed(Error(BaseModuleMutationErrorCodes.ReceiptUnavailable, ErrorCategory.Authorization))); }
    }

    private bool EvaluateBlock(
        BaseModuleMutationBlock block,
        BaseModuleProgramEvaluator<TRequest, TResult> evaluator,
        ImmutableArray<BaseModuleGenerationIncrement>.Builder increments,
        out BaseError? error)
    {
        foreach (BaseModuleStatement statement in block.Statements)
        {
            switch (statement)
            {
                case BaseModuleRequireStatement requirement when !evaluator.Guard(requirement.GuardId):
                    error = Error("base.moduleMutation.requirementFailed", ErrorCategory.Validation); return false;
                case BaseModuleRequireStatement: break;
                case BaseModuleIncrementGenerationStatement increment:
                    BaseModuleGenerationCapture capture = definition.Template.Captures.OfType<BaseModuleGenerationCapture>()
                        .Single(value => string.Equals(value.Id, increment.CaptureId, StringComparison.Ordinal));
                    int ordinal = extension.Generations.Single(value => string.Equals(value.CaptureId, capture.Id, StringComparison.Ordinal)).Ordinal;
                    increments.Add(new BaseModuleGenerationIncrement { CaptureOrdinal = ordinal, CreateIfAbsent = increment.CreateIfAbsent });
                    break;
                case BaseModuleIfStatement branch:
                    bool selected = evaluator.Guard(branch.GuardId);
                    evaluator.RecordIfDecision(branch.Id, selected);
                    if (!EvaluateBlock(selected ? branch.WhenTrue : branch.WhenFalse, evaluator, increments, out error)) return false;
                    break;
                default:
                    error = Error(BaseModuleMutationErrorCodes.Invalid, ErrorCategory.Validation); return false;
            }
        }
        error = null; return true;
    }

    private bool CapturedMatches(BaseCapturedAtomicMutationAuthority value) =>
        value.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
        && string.Equals(value.IntentDigest, intent.IntentDigest, StringComparison.Ordinal)
        && value.ModuleRecords.Length == extension.Records.Length
        && value.Generations.Length == extension.Generations.Length
        && value.ReadIntervals.Length == value.Accounting.ReadIntervals;

    private static bool PreparedMatches(BaseAtomicMutationPlan plan, BaseCapturedAtomicMutationAuthority captured, BasePreparedAtomicMutation prepared) =>
        prepared.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
        && string.Equals(prepared.PlanDigest, plan.PlanDigest, StringComparison.Ordinal)
        && prepared.Generations.Length == captured.Generations.Length
        && prepared.Accounting.GenerationReads == captured.Generations.Length;

    private static bool AppliedMatches(BaseAtomicMutationPlan plan, BaseProvisionalAppliedAtomicMutation applied) =>
        applied.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
        && string.Equals(applied.PlanDigest, plan.PlanDigest, StringComparison.Ordinal)
        && applied.Facts.Length == plan.Items.Length;

    private static AtomicMutationProcessingResult Failed(BaseError error) => new(
        AtomicMutationProcessingOutcome.Failed, [], error);
    private static BaseError Error(string code, ErrorCategory category) => new()
    {
        Code = code, Message = "The registered module mutation could not be completed.", Category = category,
    };
    private static string Digest(params string[] values) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', values)))).ToLowerInvariant();
}
