using HPD.Agent;
using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Packages;

public interface IHpdPackage
{
    HpdPackageManifest Manifest { get; }

    string Id { get; }

    string DisplayName { get; }

    Version Version { get; }

    void Configure(IHpdPackageBuilder builder);
}

public abstract class HpdPackage : IHpdPackage
{
    public abstract HpdPackageManifest Manifest { get; }

    public string Id => Manifest.Id;

    public string DisplayName => Manifest.DisplayName;

    public Version Version => Manifest.Version;

    public abstract void Configure(IHpdPackageBuilder builder);
}

public interface IHpdPackageBuilder
{
    IServiceCollection Services { get; }

    HpdContributionOwner Owner { get; }

    void AddAgentContributor(
        string key,
        IAgentBuilderContributor contributor,
        int order = 0);

    void AddProviderContributor(IProviderContributor contributor);

    void AddExternalProcess(
        string key,
        HpdExternalPackageProcessSpec process,
        int order = 0);

    void AddRuntimeContribution<TContribution>(
        string key,
        TContribution contribution,
        int order = 0,
        HpdPackageChangeImpact impact = HpdPackageChangeImpact.LiveNow)
        where TContribution : notnull;
}

public sealed class HpdPackageContributionContext
{
    public required HpdContributionOwner Owner { get; init; }

    public required IServiceProvider Services { get; init; }
}

public sealed class HpdPackageContributionStores
{
    public HpdPackageContributionStores(
        AgentBuilderContributorStore agentContributors,
        ProviderContributionStore providerContributions)
        : this(
            agentContributors,
            providerContributions,
            new HpdPackageRuntimeContributionStore(),
            new HpdExternalPackageProcessRuntime())
    {
    }

    public HpdPackageContributionStores(
        AgentBuilderContributorStore agentContributors,
        ProviderContributionStore providerContributions,
        HpdPackageRuntimeContributionStore runtimeContributions)
        : this(
            agentContributors,
            providerContributions,
            runtimeContributions,
            new HpdExternalPackageProcessRuntime())
    {
    }

    public HpdPackageContributionStores(
        AgentBuilderContributorStore agentContributors,
        ProviderContributionStore providerContributions,
        HpdPackageRuntimeContributionStore runtimeContributions,
        IHpdExternalPackageProcessRuntime externalProcesses)
    {
        AgentContributors = agentContributors ?? throw new ArgumentNullException(nameof(agentContributors));
        ProviderContributions = providerContributions ?? throw new ArgumentNullException(nameof(providerContributions));
        RuntimeContributions = runtimeContributions ?? throw new ArgumentNullException(nameof(runtimeContributions));
        ExternalProcesses = externalProcesses ?? throw new ArgumentNullException(nameof(externalProcesses));
    }

    public AgentBuilderContributorStore AgentContributors { get; }

    public ProviderContributionStore ProviderContributions { get; }

    public HpdPackageRuntimeContributionStore RuntimeContributions { get; }

    public IHpdExternalPackageProcessRuntime ExternalProcesses { get; }
}

public interface IHpdPackageRuntime
{
    event EventHandler<HpdPackageChangedEventArgs>? Changed;

    IReadOnlyList<HpdLoadedPackage> Packages { get; }

    ValueTask<IReadOnlyList<HpdLoadedPackage>> ListAsync(
        CancellationToken cancellationToken = default);

    ValueTask<HpdPackagePrepareResult> PreparePackageChangeAsync(
        HpdPackageChangeRequest request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This package runtime does not support package prepare.");

    ValueTask<HpdPackageCommitResult> CommitPackageChangeAsync(
        HpdPackagePreparedChange change,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This package runtime does not support package commit.");

    ValueTask<HpdLoadedPackage> EnableRegisteredAsync(
        string packageId,
        string scope = HpdPackageScopes.App,
        CancellationToken cancellationToken = default);

    ValueTask<HpdLoadedPackage> ReloadRegisteredAsync(
        string packageId,
        string scope = HpdPackageScopes.App,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DisableAsync(
        string packageId,
        CancellationToken cancellationToken = default);
}

public sealed class HpdPackageManager
{
    private readonly object _gate = new();
    private readonly IServiceCollection _services;
    private readonly HpdPackageContributionStores _stores;
    private readonly Dictionary<string, HpdLoadedPackage> _packages = new(StringComparer.Ordinal);

    public HpdPackageManager(
        IServiceCollection services,
        HpdPackageContributionStores stores)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _stores = stores ?? throw new ArgumentNullException(nameof(stores));
    }

    public event EventHandler<HpdPackageChangedEventArgs>? Changed;

    public IReadOnlyList<HpdLoadedPackage> Packages
    {
        get
        {
            lock (_gate)
            {
                return _packages.Values
                    .OrderBy(static package => package.Owner.Scope, StringComparer.Ordinal)
                    .ThenBy(static package => package.Id, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public HpdLoadedPackage Enable(
        IHpdPackage package,
        string scope = HpdPackageScopes.App)
    {
        var prepared = Prepare(
            HpdPackageChangeRequest.Enable(package, scope),
            static _ => { });
        return CommitPrepared(prepared).Package;
    }

    public HpdLoadedPackage EnableRegistered(
        string packageId,
        string scope = HpdPackageScopes.App)
    {
        var package = HpdPackageRegistry.Find(packageId)
            ?? throw new KeyNotFoundException($"Package '{packageId}' is not registered.");
        return Enable(package, scope);
    }

    public IReadOnlyList<HpdLoadedPackage> EnableRegisteredPackages(
        string scope = HpdPackageScopes.App)
        => HpdPackageRegistry.Snapshot()
            .Select(package => Enable(package, scope))
            .ToArray();

    public HpdLoadedPackage Reload(
        IHpdPackage package,
        string scope = HpdPackageScopes.App)
        => Enable(package, scope);

    public HpdPackagePreparedChange Prepare(
        HpdPackageChangeRequest request)
        => Prepare(request, static _ => { });

    public HpdPackagePreparedChange Prepare(
        HpdPackageChangeRequest request,
        Action<IHpdPackageBuilder> configureCandidate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(configureCandidate);
        var package = request.Package
            ?? HpdPackageRegistry.Find(request.PackageId)
            ?? throw new KeyNotFoundException($"Package '{request.PackageId}' is not registered.");
        var scope = request.Scope;
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var owner = CreateOwner(package, scope);
        var candidateStores = new HpdPackageContributionStores(
            new AgentBuilderContributorStore(),
            new ProviderContributionStore(applySecretAliases: false),
            new HpdPackageRuntimeContributionStore(),
            new HpdExternalPackageProcessRuntime());
        var builder = new HpdPackageBuilder(_services, candidateStores, owner);
        try
        {
            configureCandidate(builder);
            AddManifestEntrypointContributions(package, builder);
            package.Configure(builder);
            var diagnostics = builder.Diagnostics
                .Concat(ValidateCandidate(package.Id, owner, candidateStores))
                .ToArray();
            var state = diagnostics.Any(static diagnostic => diagnostic.Severity == HpdPackageDiagnosticSeverity.Error)
                ? HpdPackageLoadState.Failed
                : HpdPackageLoadState.Enabled;

            return new HpdPackagePreparedChange(
                request,
                package,
                owner,
                state,
                candidateStores,
                CreateContributionSummary(candidateStores),
                builder.Impacts,
                diagnostics);
        }
        catch (Exception ex)
        {
            return new HpdPackagePreparedChange(
                request,
                package,
                owner,
                HpdPackageLoadState.Failed,
                candidateStores,
                CreateContributionSummary(candidateStores),
                builder.Impacts,
                builder.Diagnostics.Concat(
                [
                    new HpdPackageDiagnostic(
                        HpdPackageDiagnosticSeverity.Error,
                        $"Package activation failed: {ex.Message}",
                        ex.GetType().FullName)
                ]).ToArray());
        }
    }

    public HpdPackageCommitResult CommitPrepared(HpdPackagePreparedChange prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (prepared.State != HpdPackageLoadState.Enabled)
        {
            var failed = CreateLoadedPackage(
                prepared.Package,
                prepared.Request.Scope,
                prepared.Owner,
                HpdPackageLoadState.Failed,
                prepared.Contributions,
                prepared.Impacts,
                prepared.Diagnostics);
            lock (_gate)
            {
                if (!_packages.ContainsKey(prepared.Package.Id))
                {
                    _packages[prepared.Package.Id] = failed;
                }
            }

            OnChanged(HpdPackageChangeKind.Failed, failed);
            return new HpdPackageCommitResult(failed, Committed: false, PreviousActiveRetained: true);
        }

        HpdLoadedPackage loaded;
        HpdPackageChangeKind changeKind;
        lock (_gate)
        {
            var isReload = _packages.ContainsKey(prepared.Package.Id);
            DisableLocked(prepared.Package.Id, out _);
            CommitCandidate(prepared.CandidateStores);

            loaded = CreateLoadedPackage(
                prepared.Package,
                prepared.Request.Scope,
                prepared.Owner,
                HpdPackageLoadState.Enabled,
                prepared.Contributions,
                prepared.Impacts,
                prepared.Diagnostics);
            _packages[prepared.Package.Id] = loaded;
            changeKind = isReload
                ? HpdPackageChangeKind.Reloaded
                : HpdPackageChangeKind.Enabled;
        }

        OnChanged(changeKind, loaded);
        return new HpdPackageCommitResult(loaded, Committed: true, PreviousActiveRetained: false);
    }

    public bool Disable(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        HpdLoadedPackage? disabled;
        lock (_gate)
        {
            if (!DisableLocked(packageId, out disabled))
            {
                return false;
            }
        }

        OnChanged(HpdPackageChangeKind.Disabled, disabled!);
        return true;
    }

    private bool DisableLocked(
        string packageId,
        out HpdLoadedPackage? loaded)
    {
        if (!_packages.Remove(packageId, out loaded))
        {
            return false;
        }

        RemoveOwnerContributions(loaded.Owner);
        return true;
    }

    private void RemoveOwnerContributions(HpdContributionOwner owner)
    {
        _stores.AgentContributors.RemoveOwner(owner);
        _stores.ProviderContributions.RemoveOwner(owner);
        _stores.RuntimeContributions.RemoveOwner(owner);
        _stores.ExternalProcesses.RemoveOwner(owner);
    }

    private void CommitCandidate(HpdPackageContributionStores candidateStores)
    {
        foreach (var contribution in candidateStores.AgentContributors.Contributions)
        {
            _stores.AgentContributors.Add(contribution);
        }

        foreach (var contribution in candidateStores.ProviderContributions.ProviderFactories)
        {
            _stores.ProviderContributions.AddProviderFactory(contribution);
        }

        foreach (var registration in candidateStores.ProviderContributions.GetProviderConfigSerializers())
        {
            _stores.ProviderContributions.AddProviderConfigSerializer(
                (registration.Key.Family, registration.Value));
        }

        foreach (var contribution in candidateStores.ProviderContributions.SecretAliases)
        {
            _stores.ProviderContributions.AddSecretAlias(contribution);
        }

        foreach (var contribution in candidateStores.ProviderContributions.ModelCatalogs)
        {
            _stores.ProviderContributions.AddModelCatalog(contribution);
        }

        foreach (var contribution in candidateStores.RuntimeContributions.Contributions)
        {
            _stores.RuntimeContributions.Add(contribution);
        }

        foreach (var contribution in candidateStores.ExternalProcesses.Processes)
        {
            _stores.ExternalProcesses.Add(contribution);
        }
    }

    private IReadOnlyList<HpdPackageDiagnostic> ValidateCandidate(
        string packageId,
        HpdContributionOwner owner,
        HpdPackageContributionStores candidateStores)
    {
        var diagnostics = new List<HpdPackageDiagnostic>();
        foreach (var contribution in candidateStores.AgentContributors.Contributions)
        {
            var existing = _stores.AgentContributors.Contributions.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, contribution.Key, StringComparison.Ordinal));
            if (existing is not null && existing.Owner.Id != packageId)
            {
                diagnostics.Add(new HpdPackageDiagnostic(
                    HpdPackageDiagnosticSeverity.Error,
                    $"Agent contributor '{contribution.Key}' conflicts with owner '{existing.Owner.Id}'.",
                    "HPD_PACKAGE_CONFLICT"));
            }
        }

        foreach (var contribution in candidateStores.RuntimeContributions.Contributions)
        {
            var existing = _stores.RuntimeContributions.Contributions.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, contribution.Key, StringComparison.Ordinal));
            if (existing is not null && existing.Owner.Id != owner.Id)
            {
                diagnostics.Add(new HpdPackageDiagnostic(
                    HpdPackageDiagnosticSeverity.Error,
                    $"Runtime contribution '{contribution.Key}' conflicts with owner '{existing.Owner.Id}'.",
                    "HPD_PACKAGE_CONFLICT"));
            }
        }

        foreach (var contribution in candidateStores.ExternalProcesses.Processes)
        {
            var existing = _stores.ExternalProcesses.Processes.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, contribution.Key, StringComparison.Ordinal));
            if (existing is not null && existing.Owner.Id != owner.Id)
            {
                diagnostics.Add(new HpdPackageDiagnostic(
                    HpdPackageDiagnosticSeverity.Error,
                    $"External package process '{contribution.Key}' conflicts with owner '{existing.Owner.Id}'.",
                    "HPD_PACKAGE_CONFLICT"));
            }
        }

        return diagnostics;
    }

    private static HpdLoadedPackage CreateLoadedPackage(
        IHpdPackage package,
        string scope,
        HpdContributionOwner owner,
        HpdPackageLoadState state,
        HpdPackageContributionSummary contributions,
        IReadOnlyList<HpdPackageChangeImpact> impacts,
        IReadOnlyList<HpdPackageDiagnostic> diagnostics)
        => new(
            package.Id,
            package.DisplayName,
            package.Version,
            scope,
            package.Manifest,
            owner,
            state,
            contributions,
            impacts.ToArray(),
            diagnostics.ToArray());

    private static HpdPackageContributionSummary CreateContributionSummary(
        HpdPackageContributionStores stores)
        => new(
            AgentContributors: stores.AgentContributors.Contributions
                .Select(static contribution => contribution.Key)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ProviderFactories: stores.ProviderContributions.ProviderFactories
                .Select(static contribution => contribution.Key)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ProviderConfigSerializers: stores.ProviderContributions.GetProviderConfigSerializers()
                .Select(static pair => $"{pair.Key.ProviderKey}:{pair.Key.Family}")
                .Order(StringComparer.Ordinal)
                .ToArray(),
            SecretAliases: stores.ProviderContributions.SecretAliases
                .Select(static contribution => contribution.Key)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ModelCatalogs: stores.ProviderContributions.ModelCatalogs
                .Select(static contribution => contribution.Key)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            RuntimeContributions: stores.RuntimeContributions.Contributions
                .Select(static contribution => contribution.Key)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ExternalProcesses: stores.ExternalProcesses.Processes
                .Select(static contribution => contribution.Key)
                .Order(StringComparer.Ordinal)
                .ToArray());

    private static HpdContributionOwner CreateOwner(
        IHpdPackage package,
        string scope)
        => new(
            package.Id,
            scope,
            package.Version.ToString(),
            package.DisplayName);

    private void OnChanged(
        HpdPackageChangeKind kind,
        HpdLoadedPackage package)
        => Changed?.Invoke(this, new HpdPackageChangedEventArgs(kind, package));

    private static void AddManifestEntrypointContributions(
        IHpdPackage package,
        IHpdPackageBuilder builder)
    {
        if (package.Manifest.Entrypoints.Process is { } process)
        {
            builder.AddExternalProcess(
                $"{package.Id}.process",
                HpdExternalPackageProcessSpec.FromManifest(
                    package.Id,
                    process));
        }

        foreach (var mcp in package.Manifest.Entrypoints.Mcp)
        {
            builder.AddExternalProcess(
                $"{package.Id}.mcp.{mcp.Name}",
                HpdExternalPackageProcessSpec.FromMcpManifest(
                    package.Id,
                    mcp));
        }
    }
}

public static class HpdPackageScopes
{
    public const string Framework = "framework";
    public const string App = "app";
    public const string User = "user";
    public const string Workspace = "workspace";
    public const string Session = "session";
}

public sealed record HpdLoadedPackage(
    string Id,
    string DisplayName,
    Version Version,
    string Scope,
    HpdPackageManifest Manifest,
    HpdContributionOwner Owner,
    HpdPackageLoadState State,
    HpdPackageContributionSummary Contributions,
    IReadOnlyList<HpdPackageChangeImpact> Impacts,
    IReadOnlyList<HpdPackageDiagnostic> Diagnostics);

public sealed record HpdPackageContributionSummary(
    IReadOnlyList<string> AgentContributors,
    IReadOnlyList<string> ProviderFactories,
    IReadOnlyList<string> ProviderConfigSerializers,
    IReadOnlyList<string> SecretAliases,
    IReadOnlyList<string> ModelCatalogs,
    IReadOnlyList<string>? RuntimeContributions = null,
    IReadOnlyList<string>? ExternalProcesses = null)
{
    public static HpdPackageContributionSummary Empty { get; } = new([], [], [], [], []);

    public bool HasAny =>
        AgentContributors.Count > 0 ||
        ProviderFactories.Count > 0 ||
        ProviderConfigSerializers.Count > 0 ||
        SecretAliases.Count > 0 ||
        ModelCatalogs.Count > 0 ||
        RuntimeContributions?.Count > 0 ||
        ExternalProcesses?.Count > 0;
}

public enum HpdPackageLoadState
{
    Enabled,
    Disabled,
    Failed
}

public enum HpdPackageChangeKind
{
    Enabled,
    Reloaded,
    Disabled,
    Failed
}

public sealed class HpdPackageChangedEventArgs : EventArgs
{
    public HpdPackageChangedEventArgs(
        HpdPackageChangeKind kind,
        HpdLoadedPackage package)
    {
        Kind = kind;
        Package = package ?? throw new ArgumentNullException(nameof(package));
    }

    public HpdPackageChangeKind Kind { get; }

    public HpdLoadedPackage Package { get; }
}

public enum HpdPackageChangeImpact
{
    LiveNow,
    FutureAgentBuilds,
    CachedAgentsStale,
    RequiresHostCoordination,
    RequiresExternalProcess
}

public sealed record HpdPackageChangeRequest(
    HpdPackageChangeOperation Operation,
    string PackageId,
    string Scope = HpdPackageScopes.App,
    IHpdPackage? Package = null)
{
    public static HpdPackageChangeRequest Enable(
        IHpdPackage package,
        string scope = HpdPackageScopes.App)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new HpdPackageChangeRequest(HpdPackageChangeOperation.Enable, package.Id, scope, package);
    }

    public static HpdPackageChangeRequest Reload(
        IHpdPackage package,
        string scope = HpdPackageScopes.App)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new HpdPackageChangeRequest(HpdPackageChangeOperation.Reload, package.Id, scope, package);
    }

    public static HpdPackageChangeRequest EnableRegistered(
        string packageId,
        string scope = HpdPackageScopes.App)
        => new(HpdPackageChangeOperation.Enable, packageId, scope);

    public static HpdPackageChangeRequest ReloadRegistered(
        string packageId,
        string scope = HpdPackageScopes.App)
        => new(HpdPackageChangeOperation.Reload, packageId, scope);
}

public enum HpdPackageChangeOperation
{
    Enable,
    Reload
}

public sealed record HpdPackagePreparedChange(
    HpdPackageChangeRequest Request,
    IHpdPackage Package,
    HpdContributionOwner Owner,
    HpdPackageLoadState State,
    HpdPackageContributionStores CandidateStores,
    HpdPackageContributionSummary Contributions,
    IReadOnlyList<HpdPackageChangeImpact> Impacts,
    IReadOnlyList<HpdPackageDiagnostic> Diagnostics)
{
    public bool IsValid => State == HpdPackageLoadState.Enabled &&
        !Diagnostics.Any(static diagnostic => diagnostic.Severity == HpdPackageDiagnosticSeverity.Error);
}

public sealed record HpdPackagePrepareResult(
    HpdPackagePreparedChange Change)
{
    public bool CanCommit => Change.IsValid;

    public HpdPackageContributionSummary Contributions => Change.Contributions;

    public IReadOnlyList<HpdPackageDiagnostic> Diagnostics => Change.Diagnostics;
}

public sealed record HpdPackageCommitResult(
    HpdLoadedPackage Package,
    bool Committed,
    bool PreviousActiveRetained);

public sealed record HpdPackageRuntimeContribution(
    string Key,
    object Value,
    Type ContributionType,
    HpdContributionOwner Owner,
    int Order = 0,
    HpdPackageChangeImpact Impact = HpdPackageChangeImpact.LiveNow);

public sealed record HpdPackageRuntimeContribution<TContribution>(
    string Key,
    TContribution Value,
    HpdContributionOwner Owner,
    int Order = 0,
    HpdPackageChangeImpact Impact = HpdPackageChangeImpact.LiveNow)
    where TContribution : notnull;

public sealed record HpdExternalPackageProcessSpec(
    string PackageId,
    string Name,
    string Command,
    IReadOnlyList<string> Args,
    string Protocol = "process",
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    public static HpdExternalPackageProcessSpec FromManifest(
        string packageId,
        HpdProcessPackageEntrypoint entrypoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(entrypoint);
        return new HpdExternalPackageProcessSpec(
            packageId,
            packageId,
            entrypoint.Command,
            entrypoint.Args,
            string.IsNullOrWhiteSpace(entrypoint.Protocol)
                ? "process"
                : entrypoint.Protocol);
    }

    public static HpdExternalPackageProcessSpec FromMcpManifest(
        string packageId,
        HpdMcpPackageEntrypoint entrypoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(entrypoint);
        return new HpdExternalPackageProcessSpec(
            packageId,
            entrypoint.Name,
            entrypoint.Command,
            entrypoint.Args,
            "mcp");
    }
}

public sealed record HpdExternalPackageProcessContribution(
    string Key,
    HpdExternalPackageProcessSpec Spec,
    HpdContributionOwner Owner,
    int Order = 0);

public interface IHpdExternalPackageProcessRuntime
{
    IReadOnlyList<HpdExternalPackageProcessContribution> Processes { get; }

    void Add(HpdExternalPackageProcessContribution contribution);

    bool RemoveOwner(HpdContributionOwner owner);
}

public sealed class HpdExternalPackageProcessRuntime : IHpdExternalPackageProcessRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HpdExternalPackageProcessContribution> _processes =
        new(StringComparer.Ordinal);

    public IReadOnlyList<HpdExternalPackageProcessContribution> Processes
    {
        get
        {
            lock (_gate)
            {
                return _processes.Values
                    .OrderBy(static contribution => contribution.Order)
                    .ThenBy(static contribution => contribution.Key, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public void Add(HpdExternalPackageProcessContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        lock (_gate)
        {
            if (_processes.ContainsKey(contribution.Key))
            {
                throw new InvalidOperationException($"An external package process is already registered for '{contribution.Key}'.");
            }

            _processes[contribution.Key] = contribution;
        }
    }

    public bool RemoveOwner(HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var removed = false;
        lock (_gate)
        {
            foreach (var key in _processes
                         .Where(pair => pair.Value.Owner == owner)
                         .Select(static pair => pair.Key)
                         .ToArray())
            {
                removed |= _processes.Remove(key);
            }
        }

        return removed;
    }
}

public sealed class HpdPackageRuntimeContributionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HpdPackageRuntimeContribution> _contributions =
        new(StringComparer.Ordinal);

    public event EventHandler<HpdPackageRuntimeContributionChangedEventArgs>? Changed;

    public IReadOnlyList<HpdPackageRuntimeContribution> Contributions
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

    public IReadOnlyList<HpdPackageRuntimeContribution<TContribution>> GetContributions<TContribution>()
        where TContribution : notnull
    {
        lock (_gate)
        {
            return _contributions.Values
                .Where(static contribution => contribution.Value is TContribution)
                .OrderBy(static contribution => contribution.Order)
                .ThenBy(static contribution => contribution.Key, StringComparer.Ordinal)
                .Select(static contribution => new HpdPackageRuntimeContribution<TContribution>(
                    contribution.Key,
                    (TContribution)contribution.Value,
                    contribution.Owner,
                    contribution.Order,
                    contribution.Impact))
                .ToArray();
        }
    }

    public void Add(HpdPackageRuntimeContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        lock (_gate)
        {
            if (_contributions.ContainsKey(contribution.Key))
            {
                throw new InvalidOperationException($"A runtime contribution is already registered for '{contribution.Key}'.");
            }

            _contributions[contribution.Key] = contribution;
        }

        OnChanged(HpdPackageRuntimeContributionChangeKind.Added, contribution.Owner);
    }

    public void Add<TContribution>(
        string key,
        TContribution contribution,
        HpdContributionOwner owner,
        int order = 0,
        HpdPackageChangeImpact impact = HpdPackageChangeImpact.LiveNow)
        where TContribution : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(owner);
        Add(new HpdPackageRuntimeContribution(
            key,
            contribution,
            typeof(TContribution),
            owner,
            order,
            impact));
    }

    public bool RemoveOwner(HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var removed = false;
        lock (_gate)
        {
            foreach (var key in _contributions
                         .Where(pair => pair.Value.Owner == owner)
                         .Select(static pair => pair.Key)
                         .ToArray())
            {
                removed |= _contributions.Remove(key);
            }
        }

        if (removed)
        {
            OnChanged(HpdPackageRuntimeContributionChangeKind.OwnerRemoved, owner);
        }

        return removed;
    }

    private void OnChanged(
        HpdPackageRuntimeContributionChangeKind kind,
        HpdContributionOwner owner)
        => Changed?.Invoke(this, new HpdPackageRuntimeContributionChangedEventArgs(kind, owner));
}

public sealed class HpdPackageRuntimeContributionChangedEventArgs : EventArgs
{
    public HpdPackageRuntimeContributionChangedEventArgs(
        HpdPackageRuntimeContributionChangeKind kind,
        HpdContributionOwner owner)
    {
        Kind = kind;
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public HpdPackageRuntimeContributionChangeKind Kind { get; }

    public HpdContributionOwner Owner { get; }
}

public enum HpdPackageRuntimeContributionChangeKind
{
    Added,
    OwnerRemoved
}

public sealed record HpdPackageDiagnostic(
    HpdPackageDiagnosticSeverity Severity,
    string Message,
    string? Code = null);

public enum HpdPackageDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

internal sealed class HpdPackageBuilder : IHpdPackageBuilder
{
    private readonly HpdPackageContributionStores _stores;
    private readonly List<HpdPackageDiagnostic> _diagnostics = [];
    private readonly HashSet<HpdPackageChangeImpact> _impacts = [];

    public HpdPackageBuilder(
        IServiceCollection services,
        HpdPackageContributionStores stores,
        HpdContributionOwner owner)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        _stores = stores ?? throw new ArgumentNullException(nameof(stores));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public IServiceCollection Services { get; }

    public HpdContributionOwner Owner { get; }

    public IReadOnlyList<HpdPackageDiagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<HpdPackageChangeImpact> Impacts => _impacts.ToArray();

    public void AddAgentContributor(
        string key,
        IAgentBuilderContributor contributor,
        int order = 0)
    {
        _stores.AgentContributors.Add(key, contributor, Owner, order);
        _impacts.Add(HpdPackageChangeImpact.FutureAgentBuilds);
        _impacts.Add(HpdPackageChangeImpact.CachedAgentsStale);
        _diagnostics.Add(new HpdPackageDiagnostic(
            HpdPackageDiagnosticSeverity.Info,
            $"Registered agent contributor '{key}'."));
    }

    public void AddProviderContributor(IProviderContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        contributor.ConfigureProviders(
            new ProviderContributionBuilder(_stores.ProviderContributions, Owner),
            new HpdProviderContributionContext
            {
                Owner = Owner,
                Services = EmptyServiceProvider.Instance
            });
        _impacts.Add(HpdPackageChangeImpact.FutureAgentBuilds);
        _impacts.Add(HpdPackageChangeImpact.CachedAgentsStale);
        _diagnostics.Add(new HpdPackageDiagnostic(
            HpdPackageDiagnosticSeverity.Info,
            "Registered provider contributor."));
    }

    public void AddExternalProcess(
        string key,
        HpdExternalPackageProcessSpec process,
        int order = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(process);
        _stores.ExternalProcesses.Add(new HpdExternalPackageProcessContribution(
            key,
            process,
            Owner,
            order));
        _impacts.Add(HpdPackageChangeImpact.RequiresExternalProcess);
        _diagnostics.Add(new HpdPackageDiagnostic(
            HpdPackageDiagnosticSeverity.Info,
            $"Registered external package process '{key}'."));
    }

    public void AddRuntimeContribution<TContribution>(
        string key,
        TContribution contribution,
        int order = 0,
        HpdPackageChangeImpact impact = HpdPackageChangeImpact.LiveNow)
        where TContribution : notnull
    {
        _stores.RuntimeContributions.Add(key, contribution, Owner, order, impact);
        _impacts.Add(impact);
        _diagnostics.Add(new HpdPackageDiagnostic(
            HpdPackageDiagnosticSeverity.Info,
            $"Registered runtime contribution '{key}'."));
    }
}
