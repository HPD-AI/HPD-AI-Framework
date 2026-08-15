using HPD.Base;

namespace HPD.Base.Testing;

internal sealed class BaseTestStoreInitializer(
    IRecordStoreRegistry registry,
    BaseTestFaults faults)
{
    private bool _initialized;

    /// <summary>Executes the initialize operation.</summary>
    public void Initialize()
    {
        if (_initialized)
            return;

        RecordStoreRegistration registration = registry.GetRegistrations().LastOrDefault()
            ?? throw new InvalidOperationException(
                "The HPD.BASE application provider was not initialized.");
        if (registration.Store is not IAtomicRecordStore atomic)
        {
            throw new InvalidOperationException(
                "The HPD.BASE test provider must support atomic mutations.");
        }

        IRecordStore decorated = atomic is ITransactionalMutationJournalStore journal
            ? new BaseTestJournalAtomicRecordStore(atomic, journal, faults)
            : new BaseTestAtomicRecordStore(atomic, faults);
        registry.Add(registration with
        {
            Store = decorated,
        });
        _initialized = true;
    }
}

internal class BaseTestAtomicRecordStore(
    IAtomicRecordStore inner,
    BaseTestFaults faults) : IAtomicRecordStore
{
    /// <summary>Gets the capabilities.</summary>
    public StoreCapabilityDescriptor Capabilities => inner.Capabilities;

    /// <summary>Executes the list async operation.</summary>
    public ValueTask<OperationResult<RecordPage>> ListAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        inner.ListAsync(collection, query, context, cancellationToken);

    /// <summary>Executes the get async operation.</summary>
    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        inner.GetAsync(collection, id, context, cancellationToken);

    public ValueTask<OperationResult<BaseAtomicMutationAuthorityRequirement>> CaptureAtomicMutationAuthorityRequirementAsync(
        string applicationId,
        System.Collections.Immutable.ImmutableArray<CollectionDefinition> collections,
        BaseAtomicMutationExecutionLimits limits,
        CancellationToken cancellationToken = default) =>
        inner.CaptureAtomicMutationAuthorityRequirementAsync(applicationId, collections, limits, cancellationToken);

    public ValueTask<RecordMutationExecutionResult> ResolveAtomicReceiptAsync(
        IAtomicMutationProcessor processor,
        BaseMutationRequestIdentity identity,
        TimeSpan resolutionTimeout,
        CancellationToken cancellationToken = default) =>
        inner.ResolveAtomicReceiptAsync(processor, identity, resolutionTimeout, cancellationToken);

    /// <summary>Executes the execute single async operation.</summary>
    public ValueTask<RecordMutationExecutionResult> ExecuteSingleAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        inner.ExecuteSingleAsync(processor, request, cancellationToken);

    /// <summary>Executes the execute atomic async operation.</summary>
    public async ValueTask<RecordMutationExecutionResult> ExecuteAtomicAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        bool failCommit = faults.TakeAtomicCommitFailure();
        bool makeIndeterminate = faults.TakeIndeterminateAtomicCommit();
        RecordMutationExecutionResult result = await inner.ExecuteAtomicAsync(
            failCommit ? new RejectCommitProcessor(processor) : processor,
            request,
            cancellationToken).ConfigureAwait(false);

        if (failCommit
            && result.Outcome == RecordMutationExecutionOutcome.RollbackConfirmed)
        {
            return new RecordMutationExecutionResult(
                result.Outcome,
                result.Processing,
                BaseTestFaults.AtomicCommitError());
        }

        if (makeIndeterminate
            && result.Outcome == RecordMutationExecutionOutcome.Committed)
        {
            return new RecordMutationExecutionResult(
                RecordMutationExecutionOutcome.Indeterminate,
                processing: null,
                BaseTestFaults.AtomicCommitError());
        }

        return result;
    }

    private sealed class RejectCommitProcessor(
        IAtomicMutationProcessor innerProcessor) : IAtomicMutationProcessor
    {
        /// <summary>Executes the process async operation.</summary>
        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            AtomicMutationProcessingResult processing =
                await innerProcessor.ProcessAsync(session, cancellationToken)
                    .ConfigureAwait(false);
            if (processing.Outcome != AtomicMutationProcessingOutcome.ReadyToCommit)
                return processing;

            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed,
                processing.Mutations,
                BaseTestFaults.AtomicCommitError());
        }
    }
}

internal sealed class BaseTestJournalAtomicRecordStore(
    IAtomicRecordStore inner,
    ITransactionalMutationJournalStore journal,
    BaseTestFaults faults)
    : BaseTestAtomicRecordStore(inner, faults),
      ITransactionalMutationJournalStore
{
    /// <summary>Executes the get mutation journal bounds async operation.</summary>
    public ValueTask<BaseMutationJournalBounds> GetMutationJournalBoundsAsync(
        CancellationToken cancellationToken = default) =>
        journal.GetMutationJournalBoundsAsync(cancellationToken);

    /// <summary>Executes the read mutation journal async operation.</summary>
    public ValueTask<BaseMutationJournalPage> ReadMutationJournalAsync(
        BaseMutationJournalReadRequest request,
        CancellationToken cancellationToken = default) =>
        journal.ReadMutationJournalAsync(request, cancellationToken);

    /// <summary>Executes the find mutation journal entry async operation.</summary>
    public ValueTask<BaseMutationJournalEntry?> FindMutationJournalEntryAsync(
        string eventId,
        CancellationToken cancellationToken = default) =>
        journal.FindMutationJournalEntryAsync(eventId, cancellationToken);
}
