using System.Threading.Channels;

namespace HPD.Base;

internal sealed class BaseSubjectLiveControlHub(BaseSubjectControlOperationalState? operationalState = null)
{
    private readonly object _sync = new();
    private readonly Dictionary<long, Subscription> _subscriptions = [];
    private readonly Dictionary<(string ContractId, int ContractVersion), BaseSubjectAuthorityPublicationFact> _current = [];
    private long _nextId;

    internal Lease Subscribe(IReadOnlySet<(string ContractId, int ContractVersion)> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        if (operationalState is not null && !operationalState.AdmitsLiveState)
            throw new BaseRealtimeFeedException(
                BaseRealtimeErrorCodes.ReplacementRequired,
                "Exported-subject control state is not reconciled.");
        var channel = Channel.CreateBounded<BaseSubjectAuthorityPublicationFact>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false,
        });
        long id;
        lock (_sync)
        {
            id = checked(++_nextId);
            var subscription = new Subscription(contracts.ToHashSet(), channel);
            foreach (BaseSubjectAuthorityPublicationFact publication in _current
                .Where(pair => subscription.Contracts.Contains(pair.Key))
                .Select(static pair => pair.Value)
                .Where(static value => value.Kind != BaseSubjectAuthorityPublicationKind.InitialInstallation)
                .OrderBy(static value => value.Position.Value))
            {
                if (channel.Writer.TryWrite(publication with { })) continue;
                channel.Writer.TryComplete(new BaseRealtimeFeedException(
                    BaseRealtimeErrorCodes.ReplacementRequired,
                    "The live subject-authority reconciliation queue overflowed."));
                break;
            }
            _subscriptions.Add(id, subscription);
        }
        return new Lease(this, id, channel.Reader);
    }

    internal void Publish(BaseSubjectAuthorityPublicationFact publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        Subscription[] targets;
        lock (_sync)
        {
            var key = (publication.ContractId, publication.ContractVersion);
            if (_current.TryGetValue(key, out BaseSubjectAuthorityPublicationFact? current)
                && (publication.PublishedStateGeneration < current.PublishedStateGeneration
                    || publication.PublishedStateGeneration == current.PublishedStateGeneration
                    && (publication.Position != current.Position || publication.Kind != current.Kind)))
                throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            _current[key] = publication with { };
            targets = _subscriptions.Values.Where(value => value.Contracts.Contains(
                (publication.ContractId, publication.ContractVersion))).ToArray();
        }
        foreach (Subscription target in targets)
            if (!target.Channel.Writer.TryWrite(publication with { }))
                target.Channel.Writer.TryComplete(new BaseRealtimeFeedException(
                    BaseRealtimeErrorCodes.ReplacementRequired,
                    "The live subject-authority control queue overflowed."));
    }

    private void Remove(long id)
    {
        Subscription? subscription;
        lock (_sync)
        {
            if (!_subscriptions.Remove(id, out subscription)) return;
        }
        subscription.Channel.Writer.TryComplete();
    }

    private sealed record Subscription(
        HashSet<(string ContractId, int ContractVersion)> Contracts,
        Channel<BaseSubjectAuthorityPublicationFact> Channel);

    internal sealed class Lease(
        BaseSubjectLiveControlHub owner,
        long id,
        ChannelReader<BaseSubjectAuthorityPublicationFact> reader) : IDisposable
    {
        internal ChannelReader<BaseSubjectAuthorityPublicationFact> Reader { get; } = reader;
        public void Dispose() => owner.Remove(id);
    }
}
