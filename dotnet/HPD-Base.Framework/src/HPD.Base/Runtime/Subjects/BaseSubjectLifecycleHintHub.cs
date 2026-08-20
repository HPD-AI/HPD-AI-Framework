using System.Threading.Channels;

namespace HPD.Base;

/// <summary>Owns process-local, non-authoritative lifecycle wake-up hints.</summary>
internal sealed class BaseSubjectLifecycleHintHub
{
    private readonly object _sync = new();
    private readonly Dictionary<long, Subscription> _subscriptions = [];
    private long _nextId;

    internal Lease Subscribe(string contractId, int contractVersion, BaseOwnedSubjectScopeEvidence scope)
    {
        var channel = Channel.CreateBounded<BaseSubjectLifecycleCommitEvidence>(new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = false,
            // Wait mode makes TryWrite return false at capacity. The hub never
            // waits; it closes only this hint channel and requires durable catch-up.
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
        long id;
        lock (_sync)
        {
            id = checked(++_nextId);
            _subscriptions.Add(id, new(contractId, contractVersion, scope with
            {
                Value = scope.Value is null ? null : new string(scope.Value.AsSpan()),
            }, channel));
        }
        return new(this, id, channel.Reader);
    }

    internal void Publish(BaseSubjectLifecycleCommitEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Subscription[] targets;
        lock (_sync)
            targets = _subscriptions.Values.Where(value => value.ContractId == evidence.ContractId
                && value.ContractVersion == evidence.ContractVersion && SameScope(value.Scope, evidence.Scope)).ToArray();
        foreach (Subscription target in targets)
            if (!target.Channel.Writer.TryWrite(Clone(evidence)))
                target.Channel.Writer.TryComplete(new BaseRealtimeFeedException(
                    BaseRealtimeErrorCodes.ReplacementRequired,
                    "The lifecycle hint channel overflowed; durable catch-up is required."));
    }

    private void Remove(long id)
    {
        Subscription? subscription;
        lock (_sync) { if (!_subscriptions.Remove(id, out subscription)) return; }
        subscription.Channel.Writer.TryComplete();
    }

    private static bool SameScope(BaseOwnedSubjectScopeEvidence left, BaseOwnedSubjectScopeEvidence right) =>
        left.Kind == right.Kind && string.Equals(left.Value, right.Value, StringComparison.Ordinal);

    private static BaseSubjectLifecycleCommitEvidence Clone(BaseSubjectLifecycleCommitEvidence value) => value with
    {
        ContractId = new string(value.ContractId.AsSpan()), SubjectId = new string(value.SubjectId.AsSpan()),
        Scope = value.Scope with { Value = value.Scope.Value is null ? null : new string(value.Scope.Value.AsSpan()) },
    };

    private sealed record Subscription(string ContractId, int ContractVersion, BaseOwnedSubjectScopeEvidence Scope,
        Channel<BaseSubjectLifecycleCommitEvidence> Channel);

    internal sealed class Lease(BaseSubjectLifecycleHintHub owner, long id,
        ChannelReader<BaseSubjectLifecycleCommitEvidence> reader) : IDisposable
    {
        internal ChannelReader<BaseSubjectLifecycleCommitEvidence> Reader { get; } = reader;
        public void Dispose() => owner.Remove(id);
    }
}
