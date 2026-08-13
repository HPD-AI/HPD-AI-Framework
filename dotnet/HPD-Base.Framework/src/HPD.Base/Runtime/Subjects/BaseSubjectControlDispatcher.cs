using System.Globalization;

namespace HPD.Base;

internal sealed class BaseSubjectControlDispatcher(
    IRecordStoreRegistry stores,
    IBaseDependencyReferenceFactory? dependencies = null,
    IBaseLiveQueryCoordinator? liveQueries = null,
    BaseSubjectLiveControlHub? liveControls = null,
    BaseSubjectControlOperationalState? operationalState = null,
    TimeSpan? callbackTimeout = null)
{
    private static readonly TimeSpan DefaultCallbackTimeout = TimeSpan.FromSeconds(15);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly BaseSubjectControlOperationalState _operationalState = operationalState ?? new();
    private readonly TimeSpan _callbackTimeout = callbackTimeout ?? DefaultCallbackTimeout;
    private readonly Dictionary<string, long> _generations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BaseSubjectAuthorityPublicationFact> _publicationFacts = new(StringComparer.Ordinal);
    private BaseMutationJournalPosition _processed;
    private long _restoreEpoch = -1;

    internal async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        if (_callbackTimeout <= TimeSpan.Zero || _callbackTimeout > DefaultCallbackTimeout)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RecordStoreRegistration[] registrations = stores.GetRegistrations();
            if (registrations.Length != 1
                || registrations[0].Store is not ITransactionalMutationJournalStore journal
                || registrations[0].Store is not IBaseSubjectPublicationStore publications)
                throw new InvalidOperationException(BaseSubjectErrorCodes.GuaranteeUnavailable);
            await ReconcileCoreAsync(journal, publications, cancellationToken).ConfigureAwait(false);
            _operationalState.MarkReady();
        }
        catch
        {
            _operationalState.MarkDegraded();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal ValueTask ReconcileAsync(CancellationToken cancellationToken) => InitializeAsync(cancellationToken);

    private async ValueTask ReconcileCoreAsync(
        ITransactionalMutationJournalStore journal,
        IBaseSubjectPublicationStore publications,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            BaseMutationJournalBounds bounds = await journal.GetMutationJournalBoundsAsync(cancellationToken).ConfigureAwait(false);
            if (_restoreEpoch != bounds.RestoreEpoch)
            {
                _restoreEpoch = bounds.RestoreEpoch;
                _processed = new BaseMutationJournalPosition(Math.Max(0, bounds.Earliest.Value - 1));
                _generations.Clear();
                _publicationFacts.Clear();
            }
            if (_processed.Value < bounds.Earliest.Value - 1)
            {
                _processed = new BaseMutationJournalPosition(Math.Max(0, bounds.Earliest.Value - 1));
                _generations.Clear();
                _publicationFacts.Clear();
            }

            while (_processed.Value < bounds.HighWatermark.Value)
            {
                BaseMutationJournalPage page = await journal.ReadMutationJournalAsync(
                    new BaseMutationJournalReadRequest
                    {
                        After = _processed,
                        Through = bounds.HighWatermark,
                        Limit = 256,
                    },
                    cancellationToken).ConfigureAwait(false);
                if (page.Entries.Length == 0)
                    throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                foreach (BaseMutationJournalEntry entry in page.Entries)
                {
                    if (entry.Position.Value <= _processed.Value || entry.Position.Value > bounds.HighWatermark.Value)
                        throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                    if (entry.Kind == BaseMutationJournalEntryKind.SubjectAuthorityPublication)
                    {
                        BaseSubjectAuthorityPublicationFact publication = entry.SubjectAuthorityPublication
                            ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                        if (publication.Position != entry.Position
                            || publication.RestoreEpoch > bounds.RestoreEpoch
                            || publication.PublishedStateGeneration <= 0
                            || publication.PreviousStateGeneration < 0
                            || !Enum.IsDefined(publication.Kind))
                            throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                        // A restored artifact deliberately preserves historical journal
                        // bytes from its earlier restore domain. Those control facts are
                        // history, not ingress for the newly published domain. Advance
                        // over them without replaying invalidation; the mandatory
                        // RestoreTransformation fact in the current domain reconciles
                        // the installed authority below.
                        if (publication.RestoreEpoch == bounds.RestoreEpoch)
                            await ReconcilePublicationAsync(publication, cancellationToken).ConfigureAwait(false);
                    }
                    else if (entry.Kind != BaseMutationJournalEntryKind.RecordMutation || entry.RecordMutation is null)
                    {
                        throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                    }
                    _processed = entry.Position;
                }
            }

            OperationResult<BaseSubjectCurrentPublicationState[]> current =
                await publications.ReadCurrentSubjectPublicationsAsync(cancellationToken).ConfigureAwait(false);
            if (!current.IsSuccess() || current.Value is null)
                throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            var currentKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (BaseSubjectCurrentPublicationState state in current.Value
                .OrderBy(static state => state.ContractId, StringComparer.Ordinal)
                .ThenBy(static state => state.ContractVersion))
            {
                string key = Key(state.ContractId, state.ContractVersion);
                if (!currentKeys.Add(key)
                    || state.ContractVersion <= 0
                    || state.Receipt.RestoreEpoch != bounds.RestoreEpoch
                    || state.Receipt.PublishedStateGeneration <= 0
                    || state.Receipt.PreviousStateGeneration < 0
                    || state.Receipt.OriginalPublicationPosition.Value <= 0
                    || state.Receipt.OriginalPublicationPosition.Value > bounds.HighWatermark.Value
                    || !Enum.IsDefined(state.Receipt.Kind)
                    || !ValidTransition(state.Receipt)
                    || !string.Equals(
                        state.Receipt.PublicationDigest,
                        BaseSubjectPublicationIntegrity.Compute(
                            state.ContractId,
                            state.ContractVersion,
                            state.ContractChecksum,
                            state.Receipt.PreviousStateGeneration,
                            state.Receipt.PublishedStateGeneration,
                            state.Receipt.RestoreEpoch,
                            state.Receipt.Kind,
                            state.Receipt.OriginalPublicationPosition,
                            state.AuthorityEpoch),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                if (state.Receipt.OriginalPublicationPosition.Value >= bounds.Earliest.Value)
                {
                    if (!_publicationFacts.TryGetValue(key, out BaseSubjectAuthorityPublicationFact? retained)
                        || !Matches(retained, state.Receipt))
                        throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                }
                if (!_generations.TryGetValue(key, out long generation)
                    || generation < state.Receipt.PublishedStateGeneration)
                {
                    await ReconcilePublicationAsync(new BaseSubjectAuthorityPublicationFact
                    {
                        Position = state.Receipt.OriginalPublicationPosition,
                        ContractId = state.ContractId,
                        ContractVersion = state.ContractVersion,
                        PreviousStateGeneration = state.Receipt.PreviousStateGeneration,
                        PublishedStateGeneration = state.Receipt.PublishedStateGeneration,
                        RestoreEpoch = state.Receipt.RestoreEpoch,
                        Kind = state.Receipt.Kind,
                    }, cancellationToken).ConfigureAwait(false);
                }
                else if (generation != state.Receipt.PublishedStateGeneration)
                {
                    throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                }
            }

            BaseMutationJournalBounds after = await journal.GetMutationJournalBoundsAsync(cancellationToken).ConfigureAwait(false);
            if (after.RestoreEpoch == bounds.RestoreEpoch && after.HighWatermark == bounds.HighWatermark)
                return;
        }
    }

    private async ValueTask ReconcilePublicationAsync(
        BaseSubjectAuthorityPublicationFact publication,
        CancellationToken cancellationToken)
    {
        string key = Key(publication.ContractId, publication.ContractVersion);
        if (_generations.TryGetValue(key, out long current))
        {
            if (current == publication.PublishedStateGeneration)
            {
                if (_publicationFacts.TryGetValue(key, out BaseSubjectAuthorityPublicationFact? existing)
                    && !Matches(existing, publication))
                    throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                return;
            }
            if (publication.Kind != BaseSubjectAuthorityPublicationKind.RestoreTransformation
                && publication.PreviousStateGeneration != current)
                throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            if (publication.PublishedStateGeneration <= current)
                throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
        if (publication.Kind != BaseSubjectAuthorityPublicationKind.InitialInstallation
            && publication.PreviousStateGeneration > 0
            && dependencies is not null
            && liveQueries is not null)
        {
            BaseDependencyReference reference = dependencies.Create(
                BaseDependencyIds.SubjectContract,
                new BaseDependencyParameter("contract", publication.ContractId),
                new BaseDependencyParameter("version", publication.ContractVersion.ToString(CultureInfo.InvariantCulture)),
                new BaseDependencyParameter("generation", publication.PreviousStateGeneration.ToString(CultureInfo.InvariantCulture)));
            BaseDependencyInvalidation invalidation = new()
            {
                EventId = $"subject:{publication.ContractId}:{publication.ContractVersion}:{publication.PublishedStateGeneration}",
                OccurredAt = DateTimeOffset.UtcNow,
                Reason = BaseDependencyInvalidationReasons.SubjectAuthorityChanged,
                References = [reference],
            };
            Task callback = liveQueries
                .InvalidateSubjectAuthorityAsync(publication, invalidation, cancellationToken)
                .AsTask();
            try
            {
                await callback.WaitAsync(_callbackTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _operationalState.Quarantine();
                _ = CompleteLateCallbackAsync(callback, publication with { });
                throw new InvalidOperationException(BaseSubjectErrorCodes.ValidationUnavailable);
            }
        }
        if (publication.Kind != BaseSubjectAuthorityPublicationKind.InitialInstallation
            && publication.PreviousStateGeneration > 0)
            liveControls?.Publish(publication);
        _generations[key] = publication.PublishedStateGeneration;
        _publicationFacts[key] = publication with { };
    }

    private static bool ValidTransition(BaseSubjectCurrentPublicationReceipt receipt) => receipt.Kind switch
    {
        BaseSubjectAuthorityPublicationKind.InitialInstallation =>
            receipt.PreviousStateGeneration == 0 && receipt.PublishedStateGeneration == 1,
        BaseSubjectAuthorityPublicationKind.EpochRotation =>
            receipt.PreviousStateGeneration > 0
            && receipt.PreviousStateGeneration < long.MaxValue
            && receipt.PublishedStateGeneration == receipt.PreviousStateGeneration + 1,
        BaseSubjectAuthorityPublicationKind.RestoreTransformation =>
            receipt.PreviousStateGeneration > 0
            && receipt.PublishedStateGeneration > receipt.PreviousStateGeneration,
        _ => false,
    };

    private static bool Matches(
        BaseSubjectAuthorityPublicationFact publication,
        BaseSubjectCurrentPublicationReceipt receipt) =>
        publication.Position == receipt.OriginalPublicationPosition
        && publication.PreviousStateGeneration == receipt.PreviousStateGeneration
        && publication.PublishedStateGeneration == receipt.PublishedStateGeneration
        && publication.RestoreEpoch == receipt.RestoreEpoch
        && publication.Kind == receipt.Kind;

    private static bool Matches(
        BaseSubjectAuthorityPublicationFact left,
        BaseSubjectAuthorityPublicationFact right) =>
        left.Position == right.Position
        && string.Equals(left.ContractId, right.ContractId, StringComparison.Ordinal)
        && left.ContractVersion == right.ContractVersion
        && left.PreviousStateGeneration == right.PreviousStateGeneration
        && left.PublishedStateGeneration == right.PublishedStateGeneration
        && left.RestoreEpoch == right.RestoreEpoch
        && left.Kind == right.Kind;

    private static string Key(string contractId, int version) =>
        contractId + "\u001f" + version.ToString(CultureInfo.InvariantCulture);

    private async Task CompleteLateCallbackAsync(
        Task callback,
        BaseSubjectAuthorityPublicationFact publication)
    {
        bool delivered = false;
        try
        {
            await callback.ConfigureAwait(false);
            delivered = true;
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                string key = Key(publication.ContractId, publication.ContractVersion);
                if (_generations.TryGetValue(key, out long current)
                    && current != publication.PreviousStateGeneration
                    && current != publication.PublishedStateGeneration)
                    return;
                if (current != publication.PublishedStateGeneration)
                {
                    liveControls?.Publish(publication);
                    _generations[key] = publication.PublishedStateGeneration;
                    _publicationFacts[key] = publication with { };
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch { }
        finally
        {
            _operationalState.ReleaseQuarantine();
        }
        if (!delivered) return;
        try { await ReconcileAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { }
    }
}
