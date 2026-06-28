using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Packages;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Hosting.Packages;

public static class HpdPackageHostingExtensions
{
    public static HpdPackageManager CreatePackageManager(
        this HPDAgentConfig config,
        IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(services);

        return new HpdPackageManager(services, config.PackageContributions);
    }

    public static IDisposable MarkAgentsStaleOnPackageChanges(
        this HpdPackageManager packages,
        AgentManager agents)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(agents);

        return new PackageStalenessSubscription(packages, agents);
    }

    private sealed class PackageStalenessSubscription : IDisposable
    {
        private readonly HpdPackageManager _packages;
        private readonly AgentManager _agents;
        private bool _disposed;

        public PackageStalenessSubscription(
            HpdPackageManager packages,
            AgentManager agents)
        {
            _packages = packages;
            _agents = agents;
            _packages.Changed += OnPackageChanged;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _packages.Changed -= OnPackageChanged;
            _disposed = true;
        }

        private void OnPackageChanged(
            object? sender,
            HpdPackageChangedEventArgs e)
        {
            if (e.Kind == HpdPackageChangeKind.Disabled ||
                e.Package.Impacts.Contains(HpdPackageChangeImpact.CachedAgentsStale))
            {
                _agents.MarkAllAgentsStale();
            }
        }
    }
}
