namespace HPD.Agent;

public interface IAgentBuilderContributor
{
    void ConfigureAgent(
        AgentBuilder builder,
        HpdAgentContributionContext context);
}

public sealed class HpdAgentContributionContext
{
    public required HpdContributionOwner Owner { get; init; }

    public required IServiceProvider Services { get; init; }

    public required string AgentId { get; init; }
}

public sealed class DelegateAgentBuilderContributor : IAgentBuilderContributor
{
    private readonly Action<AgentBuilder> _configure;

    public DelegateAgentBuilderContributor(Action<AgentBuilder> configure)
    {
        _configure = configure ?? throw new ArgumentNullException(nameof(configure));
    }

    public void ConfigureAgent(
        AgentBuilder builder,
        HpdAgentContributionContext context)
        => _configure(builder);
}

public sealed record AgentBuilderContribution(
    string Key,
    IAgentBuilderContributor Contributor,
    HpdContributionOwner Owner,
    int Order = 0);

public sealed class AgentBuilderContributorStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AgentBuilderContribution> _contributions =
        new(StringComparer.Ordinal);
    private int _nextOrder;

    public event EventHandler<AgentBuilderContributorChangedEventArgs>? Changed;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _contributions.Count;
            }
        }
    }

    public IAgentBuilderContributor this[int index] => Contributions[index].Contributor;

    public IReadOnlyList<AgentBuilderContribution> Contributions
    {
        get
        {
            lock (_gate)
            {
                return _contributions.Values
                    .OrderBy(static contribution => contribution.Order)
                    .ThenBy(static contribution => contribution.Key, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public IReadOnlyList<HpdContributionOwner> Owners
    {
        get
        {
            lock (_gate)
            {
                return _contributions.Values
                    .Select(static contribution => contribution.Owner)
                    .Distinct()
                    .OrderBy(static owner => owner.Scope, StringComparer.Ordinal)
                    .ThenBy(static owner => owner.Id, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public void Add(IAgentBuilderContributor contributor) =>
        Add(contributor, HpdContributionOwner.App);

    public void Add(
        IAgentBuilderContributor contributor,
        HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        ArgumentNullException.ThrowIfNull(owner);
        Add(CreateImplicitKey(contributor), contributor, owner, _nextOrder++);
    }

    public void Add(
        string key,
        IAgentBuilderContributor contributor,
        HpdContributionOwner owner,
        int order = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(contributor);
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            if (_contributions.ContainsKey(key))
            {
                throw new InvalidOperationException($"An agent builder contributor is already registered for '{key}'.");
            }

            _contributions[key] = new AgentBuilderContribution(key, contributor, owner, order);
        }

        OnChanged(AgentBuilderContributorChangeKind.Added, owner);
    }

    internal void Add(AgentBuilderContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);

        lock (_gate)
        {
            if (_contributions.ContainsKey(contribution.Key))
            {
                throw new InvalidOperationException($"An agent builder contributor is already registered for '{contribution.Key}'.");
            }

            _contributions[contribution.Key] = contribution;
        }

        OnChanged(AgentBuilderContributorChangeKind.Added, contribution.Owner);
    }

    public bool RemoveOwner(HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var removed = false;
        lock (_gate)
        {
            foreach (var key in _contributions
                         .Where(pair => pair.Value.Owner == owner)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                removed |= _contributions.Remove(key);
            }
        }

        if (removed)
        {
            OnChanged(AgentBuilderContributorChangeKind.OwnerRemoved, owner);
        }

        return removed;
    }

    public void ApplyTo(
        AgentBuilder builder,
        IServiceProvider services,
        string agentId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(services);

        foreach (var contribution in Contributions)
        {
            contribution.Contributor.ConfigureAgent(
                builder,
                new HpdAgentContributionContext
                {
                    Owner = contribution.Owner,
                    Services = services,
                    AgentId = agentId
                });
        }
    }

    private string CreateImplicitKey(IAgentBuilderContributor contributor)
    {
        var typeName = contributor.GetType().FullName ?? "contributor";
        return $"{typeName}#{_nextOrder}";
    }

    private void OnChanged(
        AgentBuilderContributorChangeKind kind,
        HpdContributionOwner owner)
        => Changed?.Invoke(this, new AgentBuilderContributorChangedEventArgs(kind, owner));
}

public sealed class AgentBuilderContributorChangedEventArgs : EventArgs
{
    public AgentBuilderContributorChangedEventArgs(
        AgentBuilderContributorChangeKind kind,
        HpdContributionOwner owner)
    {
        Kind = kind;
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public AgentBuilderContributorChangeKind Kind { get; }

    public HpdContributionOwner Owner { get; }
}

public enum AgentBuilderContributorChangeKind
{
    Added,
    OwnerRemoved
}

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public static EmptyServiceProvider Instance { get; } = new();

    private EmptyServiceProvider()
    {
    }

    public object? GetService(Type serviceType) => null;
}
