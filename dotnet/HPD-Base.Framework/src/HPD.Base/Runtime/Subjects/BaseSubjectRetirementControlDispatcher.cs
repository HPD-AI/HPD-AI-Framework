using System.Globalization;
using System.Diagnostics;

namespace HPD.Base;

internal sealed class BaseSubjectRetirementControlDispatcher(
    IRecordStoreRegistry stores,
    IEnumerable<IBaseSubjectRetirementControlObserver> observers,
    IBaseDependencyReferenceFactory? dependencies = null,
    IBaseLiveQueryCoordinator? liveQueries = null,
    TimeProvider? timeProvider = null,
    BaseSubjectRetirementOperationalState? operationalState = null) : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _providerSlot = new(1, 1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly BaseSubjectRetirementOperationalState _operationalState = operationalState ?? new();
    private BaseSubjectRetirementPosition _processed;
    private bool _initialized;

    internal async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); try { IBaseSubjectRetirementStore store = Store(); OperationResult<BaseSubjectRetirementPublicationPage> page = await ReadAsync(store, new() { After = null, Take = 1 }, cancellationToken).ConfigureAwait(false); if (!page.IsSuccess() || page.Value is null) throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid); _processed = page.Value.HighWater; _initialized = true; } finally { _gate.Release(); }
    }

    internal async ValueTask ReconcileAsync(CancellationToken cancellationToken)
    {
        if (!_initialized) return; await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); try { IBaseSubjectRetirementStore store = Store(); while (true) { OperationResult<BaseSubjectRetirementPublicationPage> result = await ReadAsync(store, new() { After = _processed.Value == 0 ? null : _processed, Take = 256 }, cancellationToken).ConfigureAwait(false); if (!result.IsSuccess() || result.Value is null) throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid); BaseSubjectRetirementPublicationPage page = result.Value; if (page.HighWater.Value < _processed.Value) throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid); foreach (BaseSubjectRetirementPublicationRow row in page.Rows) { BaseSubjectRetirementRegistry.ValidatePublication(row); if (row.Fact.Position.Value != checked(_processed.Value + 1) || row.Fact.Position.Value > page.HighWater.Value) throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid); await DispatchAsync(row.Fact, cancellationToken).ConfigureAwait(false); _processed = row.Fact.Position; } if (_processed.Value >= page.HighWater.Value) break; if (page.Rows.IsEmpty) throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid); } } finally { _gate.Release(); }
    }

    private async ValueTask DispatchAsync(BaseSubjectRetirementPublicationFact fact, CancellationToken cancellationToken)
    {
        (string Contract, int Version) = Identity(fact); if (dependencies is not null && liveQueries is not null) { BaseDependencyReference reference = dependencies.Create(BaseDependencyIds.SubjectRetirement, new BaseDependencyParameter("contract", Contract), new BaseDependencyParameter("version", Version.ToString(CultureInfo.InvariantCulture))); await liveQueries.InvalidateAsync(new() { EventId = $"subject-retirement:{fact.Position.Value}", OccurredAt = _timeProvider.GetUtcNow(), Reason = BaseDependencyInvalidationReasons.SubjectRetirementChanged, References = [reference] }, cancellationToken).ConfigureAwait(false); }
        string action = AuditAction(fact.Kind); var notice = new BaseSubjectRetirementControlNotice { Publication = fact with { }, AuditAction = action }; foreach (IBaseSubjectRetirementControlObserver observer in observers) await observer.ObserveAsync(notice, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<OperationResult<BaseSubjectRetirementPublicationPage>> ReadAsync(IBaseSubjectRetirementStore store, BaseSubjectRetirementPublicationReadRequest request, CancellationToken cancellationToken) => DefaultBaseSubjectRetirementRuntime.InvokeProviderAsync(token => store.ReadPublicationsAsync(request, token), TimeSpan.FromSeconds(30), cancellationToken, _providerSlot, _operationalState);
    public async ValueTask DisposeAsync() { long started = Stopwatch.GetTimestamp(); while (_operationalState.Active + _operationalState.Quarantined != 0 && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(30)) await Task.Delay(10).ConfigureAwait(false); if (_operationalState.Active + _operationalState.Quarantined == 0) { _providerSlot.Dispose(); _gate.Dispose(); } }
    public void Dispose() { if (_operationalState.Active + _operationalState.Quarantined == 0) { _providerSlot.Dispose(); _gate.Dispose(); } }

    private IBaseSubjectRetirementStore Store() { RecordStoreRegistration[] registrations = stores.GetRegistrations(); if (registrations.Length != 1 || registrations[0].Store is not IBaseSubjectRetirementStore store) throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid); return store; }
    private static (string, int) Identity(BaseSubjectRetirementPublicationFact fact) => fact.Barrier is { } b ? (b.ContractId, b.ContractVersion) : fact.AdvisoryAcknowledgement is { } a ? (a.ContractId, a.ContractVersion) : fact.Purged is { } p ? (p.ContractId, p.ContractVersion) : fact.ConsumerSet is { } c ? (c.ContractId, c.ContractVersion) : fact.Restore is { } r ? (r.ContractId, r.ContractVersion) : throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
    private static string AuditAction(BaseSubjectRetirementPublicationKind kind) => kind switch { BaseSubjectRetirementPublicationKind.BarrierCreated => "base.subjectRetirement.barrier.created", BaseSubjectRetirementPublicationKind.RequiredAcknowledgementAccepted => "base.subjectRetirement.acknowledgement.accepted", BaseSubjectRetirementPublicationKind.BarrierSatisfied => "base.subjectRetirement.barrier.satisfied", BaseSubjectRetirementPublicationKind.BarrierTimedOut => "base.subjectRetirement.barrier.timedOut", BaseSubjectRetirementPublicationKind.BarrierQuarantined => "base.subjectRetirement.barrier.quarantined", BaseSubjectRetirementPublicationKind.BarrierOverridden => "base.subjectRetirement.barrier.overridden", BaseSubjectRetirementPublicationKind.AdvisoryAcknowledgementAccepted => "base.subjectRetirement.acknowledgement.accepted", BaseSubjectRetirementPublicationKind.SubjectPurged => "base.subjectRetirement.subject.purged", BaseSubjectRetirementPublicationKind.ConsumerSetChanged => "base.subjectRetirement.consumerRemoval.completed", BaseSubjectRetirementPublicationKind.RestoreTransformed => "base.subjectRetirement.restore.transformed", _ => throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid) };
}
