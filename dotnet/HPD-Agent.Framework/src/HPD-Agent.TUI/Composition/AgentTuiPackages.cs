using HPD.Agent;
using HPD.Agent.Packages;

namespace HPD.Agent.TUI.Composition;

public interface IAgentTuiContributor
{
    void ConfigureTui(
        HpdAgentTuiBuilder builder,
        HpdPackageContributionContext context);
}

public static class AgentTuiPackageBuilderExtensions
{
    public static void AddTuiContributor(
        this IHpdPackageBuilder builder,
        string key,
        IAgentTuiContributor contributor,
        int order = 0)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(contributor);
        builder.AddRuntimeContribution(
            key,
            contributor,
            order,
            HpdPackageChangeImpact.LiveNow);
    }
}

public sealed class AgentTuiPackageManager : IHpdPackageRuntime
{
    private readonly HpdPackageManager _packages;
    private readonly AgentTuiContributionStore _tuiContributions;
    private readonly IServiceProvider _services;

    public AgentTuiPackageManager(
        HpdPackageManager packages,
        AgentTuiContributionStore tuiContributions,
        IServiceProvider services)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _tuiContributions = tuiContributions ?? throw new ArgumentNullException(nameof(tuiContributions));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public event EventHandler<HpdPackageChangedEventArgs>? Changed;

    public IReadOnlyList<HpdLoadedPackage> Packages => _packages.Packages;

    public ValueTask<IReadOnlyList<HpdLoadedPackage>> ListAsync(
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Packages);

    public HpdLoadedPackage Enable(
        IHpdPackage package,
        string scope = HpdPackageScopes.App)
    {
        ArgumentNullException.ThrowIfNull(package);
        var prepared = Prepare(package, scope);
        var candidateTuiContributions = BuildTuiCandidate(prepared);
        if (candidateTuiContributions is null)
        {
            var failed = CreateFailedLoadedPackage(prepared);
            OnChanged(HpdPackageChangeKind.Failed, failed);
            return failed;
        }

        var previous = _packages.Packages.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, package.Id, StringComparison.Ordinal));
        var committed = _packages.CommitPrepared(prepared);
        var loaded = committed.Package;
        if (!committed.Committed)
        {
            OnChanged(HpdPackageChangeKind.Failed, loaded);
            return loaded;
        }

        if (previous is not null)
        {
            _tuiContributions.RemoveOwner(previous.Owner);
        }

        _tuiContributions.ApplyFrom(candidateTuiContributions);
        if (package is IAgentTuiContributor)
        {
            loaded = loaded with
            {
                Impacts = loaded.Impacts
                    .Concat([HpdPackageChangeImpact.LiveNow])
                    .Distinct()
                    .ToArray()
            };
        }

        OnChanged(previous is null
            ? HpdPackageChangeKind.Enabled
            : HpdPackageChangeKind.Reloaded, loaded);
        return loaded;
    }

    public ValueTask<HpdPackagePrepareResult> PreparePackageChangeAsync(
        HpdPackageChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var package = request.Package
            ?? HpdPackageRegistry.Find(request.PackageId)
            ?? throw new KeyNotFoundException($"Package '{request.PackageId}' is not registered.");
        var prepared = Prepare(package, request.Scope);
        var candidate = BuildTuiCandidate(prepared);
        if (candidate is null)
        {
            prepared = prepared with
            {
                State = HpdPackageLoadState.Failed
            };
        }

        return ValueTask.FromResult(new HpdPackagePrepareResult(prepared));
    }

    public ValueTask<HpdPackageCommitResult> CommitPackageChangeAsync(
        HpdPackagePreparedChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var candidateTuiContributions = BuildTuiCandidate(change);
        if (candidateTuiContributions is null)
        {
            var failed = CreateFailedLoadedPackage(change);
            OnChanged(HpdPackageChangeKind.Failed, failed);
            return ValueTask.FromResult(new HpdPackageCommitResult(
                failed,
                Committed: false,
                PreviousActiveRetained: true));
        }

        var previous = _packages.Packages.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, change.Package.Id, StringComparison.Ordinal));
        var committed = _packages.CommitPrepared(change);
        if (!committed.Committed)
        {
            OnChanged(HpdPackageChangeKind.Failed, committed.Package);
            return ValueTask.FromResult(committed);
        }

        if (previous is not null)
        {
            _tuiContributions.RemoveOwner(previous.Owner);
        }

        _tuiContributions.ApplyFrom(candidateTuiContributions);
        OnChanged(previous is null
            ? HpdPackageChangeKind.Enabled
            : HpdPackageChangeKind.Reloaded, committed.Package);
        return ValueTask.FromResult(committed);
    }

    public HpdLoadedPackage EnableRegistered(
        string packageId,
        string scope = HpdPackageScopes.App)
    {
        var package = HpdPackageRegistry.Find(packageId)
            ?? throw new KeyNotFoundException($"Package '{packageId}' is not registered.");
        return Enable(package, scope);
    }

    public ValueTask<HpdLoadedPackage> EnableRegisteredAsync(
        string packageId,
        string scope = HpdPackageScopes.App,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(EnableRegistered(packageId, scope));

    public ValueTask<HpdLoadedPackage> ReloadRegisteredAsync(
        string packageId,
        string scope = HpdPackageScopes.App,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(EnableRegistered(packageId, scope));

    public IReadOnlyList<HpdLoadedPackage> EnableRegisteredPackages(
        string scope = HpdPackageScopes.App)
        => HpdPackageRegistry.Snapshot()
            .Select(package => Enable(package, scope))
            .ToArray();

    public bool Disable(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        var package = _packages.Packages.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, packageId, StringComparison.Ordinal));
        if (package is null)
        {
            return false;
        }

        _tuiContributions.RemoveOwner(package.Owner);
        var disabled = _packages.Disable(packageId);
        if (disabled)
        {
            OnChanged(HpdPackageChangeKind.Disabled, package);
        }

        return disabled;
    }

    public ValueTask<bool> DisableAsync(
        string packageId,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Disable(packageId));

    private static HpdContributionOwner CreateOwner(
        IHpdPackage package,
        string scope)
        => new(
            package.Id,
            scope,
            package.Version.ToString(),
            package.DisplayName);

    private HpdPackagePreparedChange Prepare(
        IHpdPackage package,
        string scope)
        => _packages.Prepare(
            HpdPackageChangeRequest.Enable(package, scope),
            builder =>
            {
                if (package is IAgentTuiContributor tuiContributor)
                {
                    builder.AddTuiContributor(
                        $"{package.Id}.tui",
                        tuiContributor);
                }
            });

    private AgentTuiContributionStore? BuildTuiCandidate(
        HpdPackagePreparedChange prepared)
    {
        if (!prepared.IsValid)
        {
            return null;
        }

        var candidateTuiContributions = new AgentTuiContributionStore();
        try
        {
            foreach (var contribution in prepared.CandidateStores.RuntimeContributions.GetContributions<IAgentTuiContributor>())
            {
                contribution.Value.ConfigureTui(
                    new HpdAgentTuiBuilder(candidateTuiContributions, prepared.Owner),
                    new HpdPackageContributionContext
                    {
                        Owner = prepared.Owner,
                        Services = _services
                    });
            }
        }
        catch
        {
            return null;
        }

        return candidateTuiContributions;
    }

    private HpdLoadedPackage CreateFailedLoadedPackage(
        HpdPackagePreparedChange prepared)
        => new(
            prepared.Package.Id,
            prepared.Package.DisplayName,
            prepared.Package.Version,
            prepared.Request.Scope,
            prepared.Package.Manifest,
            prepared.Owner,
            HpdPackageLoadState.Failed,
            prepared.Contributions,
            [HpdPackageChangeImpact.LiveNow],
            prepared.Diagnostics
                .Concat(
                [
                    new HpdPackageDiagnostic(
                        HpdPackageDiagnosticSeverity.Error,
                        "TUI package activation failed.",
                        "HPD_TUI_PACKAGE_ACTIVATION_FAILED")
                ])
                .ToArray());

    private void OnChanged(
        HpdPackageChangeKind kind,
        HpdLoadedPackage package)
        => Changed?.Invoke(this, new HpdPackageChangedEventArgs(kind, package));
}
