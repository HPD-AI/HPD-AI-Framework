using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace HPD.Base;

internal sealed class BaseTextCertificationFaultController(ImmutableArray<BaseTextCertificationFaultSchedule> configured)
{
    private readonly ConcurrentDictionary<BaseTextCertificationOperationKind, int> _occurrences = new();
    private readonly ConcurrentDictionary<(BaseTextCertificationOperationKind Kind, int Occurrence), TaskCompletionSource> _late = new();
    private readonly ConcurrentDictionary<BaseTextCertificationFault, byte> _consumed = new();
    private volatile bool _active;

    internal ImmutableArray<BaseTextCertificationFaultSchedule> Configured { get; } = configured.Select(static value => value with { }).ToImmutableArray();
    internal int RetainedCount => _late.Count;
    internal ImmutableArray<BaseTextCertificationFault> Consumed => _consumed.Keys.Order().ToImmutableArray();
    internal void Activate() => _active = true;

    internal BaseTextCertificationFaultSchedule? Next(BaseTextCertificationOperationKind kind)
    {
        if (!_active) return null;
        int occurrence = _occurrences.AddOrUpdate(kind, 1, static (_, current) => checked(current + 1));
        BaseTextCertificationFaultSchedule? schedule = Configured.SingleOrDefault(value => value.Occurrence == occurrence && Kind(value.Fault) == kind);
        if (schedule is not null) _consumed.TryAdd(schedule.Fault, 0);
        return schedule;
    }

    internal async ValueTask BeforeAsync(BaseTextCertificationOperationKind kind, BaseTextCertificationFaultSchedule? schedule, CancellationToken cancellationToken)
    {
        if (schedule is null) return;
        if (IsNonCooperative(schedule.Fault))
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_late.TryAdd((kind, schedule.Occurrence), completion)) throw new InvalidOperationException("The certification late-work identity is duplicated.");
            await completion.Task.ConfigureAwait(false);
            _late.TryRemove((kind, schedule.Occurrence), out _);
            return;
        }
        if (schedule.Fault is BaseTextCertificationFault.QueryTimeout or BaseTextCertificationFault.ProjectionWriteTimeout or BaseTextCertificationFault.InspectionTimeout or BaseTextCertificationFault.RebuildTimeout)
            await Task.Delay(schedule.Delay, cancellationToken).ConfigureAwait(false);
    }

    internal BaseTextCertificationLateWorkResult Release(BaseTextCertificationOperationKind kind, int occurrence)
    {
        bool retained = _late.TryGetValue((kind, occurrence), out TaskCompletionSource? completion);
        completion?.TrySetResult();
        return new() { OperationKind = kind, Occurrence = occurrence, WasRetained = retained, Released = retained, QuarantineCountAfterRelease = retained ? Math.Max(0, _late.Count - 1) : _late.Count };
    }

    private static BaseTextCertificationOperationKind Kind(BaseTextCertificationFault fault) => fault switch
    {
        BaseTextCertificationFault.ProjectionWriteTimeout or BaseTextCertificationFault.ProjectionWriteNonCooperative or BaseTextCertificationFault.JournalGap or BaseTextCertificationFault.RetentionOvertake => BaseTextCertificationOperationKind.ProjectionWrite,
        BaseTextCertificationFault.InspectionTimeout or BaseTextCertificationFault.InspectionNonCooperative => BaseTextCertificationOperationKind.Inspection,
        BaseTextCertificationFault.RebuildTimeout or BaseTextCertificationFault.RebuildNonCooperative or BaseTextCertificationFault.StagingCorruption or BaseTextCertificationFault.FinalPublicationFailure => BaseTextCertificationOperationKind.Rebuild,
        _ => BaseTextCertificationOperationKind.Query,
    };

    internal static bool IsNonCooperative(BaseTextCertificationFault fault) => fault is BaseTextCertificationFault.QueryNonCooperative or BaseTextCertificationFault.ProjectionWriteNonCooperative or BaseTextCertificationFault.InspectionNonCooperative or BaseTextCertificationFault.RebuildNonCooperative;
}

internal sealed class BaseTextCertificationFaultProvider(IBaseTextProvider inner, BaseTextCertificationFaultController faults) : IBaseTextProvider, IBaseTextAuthority
{
    public BaseTextProviderDescriptor Descriptor => inner.Descriptor;
    public IBaseTextAuthority Authority => this;

    public async ValueTask<OperationResult<IBaseTextHydrationSession>> OpenAsync(BaseTextAuthorityOpenRequest request, CancellationToken cancellationToken = default)
    {
        OperationResult<IBaseTextHydrationSession> opened = await inner.Authority.OpenAsync(request, cancellationToken).ConfigureAwait(false);
        return !opened.Status.IsSuccess() || opened.Value is null ? opened : opened with { Value = new Session(opened.Value, faults) };
    }

    public ValueTask<OperationResult<BaseTextRebuildResult>> RebuildAsync(BaseTextRebuildRequest request, CancellationToken cancellationToken) =>
        InvokeAsync(BaseTextCertificationOperationKind.Rebuild, token => inner.RebuildAsync(request, token), cancellationToken);
    public ValueTask<OperationResult<BaseTextIndexStatus[]>> ListAsync(CancellationToken cancellationToken) =>
        InvokeAsync(BaseTextCertificationOperationKind.Inspection, inner.ListAsync, cancellationToken);
    public ValueTask<OperationResult<BaseTextIndexStatus>> GetAsync(string collectionId, string textIndexId, CancellationToken cancellationToken) =>
        InvokeAsync(BaseTextCertificationOperationKind.Inspection, token => inner.GetAsync(collectionId, textIndexId, token), cancellationToken);

    private async ValueTask<OperationResult<T>> InvokeAsync<T>(BaseTextCertificationOperationKind kind, Func<CancellationToken, ValueTask<OperationResult<T>>> invoke, CancellationToken cancellationToken)
    {
        BaseTextCertificationFaultSchedule? schedule = faults.Next(kind);
        await faults.BeforeAsync(kind, schedule, cancellationToken).ConfigureAwait(false);
        return await invoke(cancellationToken).ConfigureAwait(false);
    }

    private sealed class Session(IBaseTextHydrationSession inner, BaseTextCertificationFaultController faults) : IBaseTextHydrationSession
    {
        public BaseTextAuthoritySnapshot Snapshot => inner.Snapshot;
        public ValueTask<OperationResult<BaseTextConstraintPreparation>> PrepareAsync(BaseTextProviderPreparationRequest request, CancellationToken cancellationToken = default) => inner.PrepareAsync(request, cancellationToken);
        public async ValueTask<OperationResult<BaseTextProviderResult>> SearchAsync(BaseTextExecutionRequest request, CancellationToken cancellationToken = default)
        {
            BaseTextCertificationFaultSchedule? schedule = faults.Next(BaseTextCertificationOperationKind.Query);
            await faults.BeforeAsync(BaseTextCertificationOperationKind.Query, schedule, cancellationToken).ConfigureAwait(false);
            OperationResult<BaseTextProviderResult> result = await inner.SearchAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.Status.IsSuccess() || result.Value is null || schedule is null) return result;
            BaseTextProviderResult value = result.Value;
            BaseTextCandidate[] candidates = value.Candidates.ToArray();
            switch (schedule.Fault)
            {
                case BaseTextCertificationFault.DuplicateCandidate when candidates.Length != 0: candidates = [candidates[0], .. candidates]; break;
                case BaseTextCertificationFault.MissingBetterCandidate when candidates.Length != 0: candidates = candidates[1..]; break;
                case BaseTextCertificationFault.MalformedCandidate when candidates.Length != 0: candidates[0] = candidates[0] with { CanonicalOrderingBoundary = [] }; break;
                case BaseTextCertificationFault.FalseScore when candidates.Length != 0: candidates[0] = candidates[0] with { Score = new BaseTextScore { Units = checked(candidates[0].Score.Units + 1) } }; break;
                case BaseTextCertificationFault.FalseFeatureEvidence when candidates.Length != 0: candidates[0] = candidates[0] with { ScoreProof = candidates[0].ScoreProof with { ProofDigest = ImmutableArray.Create(new byte[32]) } }; break;
                case BaseTextCertificationFault.FalsePrefixExpansion when candidates.Length != 0:
                    BaseTextFeatureEvidence[] features = candidates[0].ScoreProof.Features.ToArray(); if (features.Length != 0) features[0] = features[0] with { PrefixExpansions = [ImmutableArray.Create((byte)0xff)] }; candidates[0] = candidates[0] with { ScoreProof = candidates[0].ScoreProof with { Features = [.. features] } }; break;
                case BaseTextCertificationFault.FalseBoundary when candidates.Length != 0: candidates[0] = candidates[0] with { CanonicalOrderingBoundary = ImmutableArray.Create((byte)0xff) }; break;
                case BaseTextCertificationFault.WrongRevision when candidates.Length != 0: candidates[0] = candidates[0] with { Revision = new RevisionToken("certification:wrong") }; break;
                case BaseTextCertificationFault.WrongSnapshot: value = value with { Snapshot = value.Snapshot with { TextIndexGeneration = checked(value.Snapshot.TextIndexGeneration + 1) } }; break;
            }
            return result with { Value = value with { Candidates = [.. candidates] } };
        }
        public ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseTextCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default) => inner.GetExactAsync(collection, candidates, context, cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
