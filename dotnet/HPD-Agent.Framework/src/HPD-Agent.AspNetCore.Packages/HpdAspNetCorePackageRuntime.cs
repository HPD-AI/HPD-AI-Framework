using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Hosting.Packages;
using HPD.Agent.Packages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Packages;

public sealed class HpdAspNetCorePackageRuntime : IHpdPackageRuntime, IDisposable
{
    private readonly IServiceCollection _services;
    private readonly IOptionsMonitor<HPDAgentConfig> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _name;
    private readonly Lazy<HpdPackageManager> _packages;
    private IDisposable? _stalenessSubscription;
    private bool _disposed;

    public HpdAspNetCorePackageRuntime(
        IServiceCollection services,
        IOptionsMonitor<HPDAgentConfig> options,
        IServiceProvider serviceProvider,
        string name)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _packages = new Lazy<HpdPackageManager>(CreatePackageManager);
    }

    public HpdPackageManager Packages => _packages.Value;

    IReadOnlyList<HpdLoadedPackage> IHpdPackageRuntime.Packages => Packages.Packages;

    public event EventHandler<HpdPackageChangedEventArgs>? Changed
    {
        add => Packages.Changed += value;
        remove => Packages.Changed -= value;
    }

    public HpdPackageListResponse List()
        => new(Packages.Packages.Select(HpdPackageResponse.From).ToArray());

    public ValueTask<IReadOnlyList<HpdLoadedPackage>> ListAsync(
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Packages.Packages);

    public HpdPackagePrepareResponse PrepareRegistered(
        string id,
        string? scope = null,
        HpdPackageChangeOperation operation = HpdPackageChangeOperation.Enable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var prepared = Packages.Prepare(new HpdPackageChangeRequest(
            operation,
            id,
            NormalizeScope(scope)));
        return HpdPackagePrepareResponse.From(new HpdPackagePrepareResult(prepared));
    }

    public ValueTask<HpdPackagePrepareResult> PreparePackageChangeAsync(
        HpdPackageChangeRequest request,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new HpdPackagePrepareResult(Packages.Prepare(request)));

    public HpdPackageActionResponse CommitRegistered(
        HpdPackagePrepareRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Operation == HpdPackageChangeOperation.Reload
            ? ReloadRegistered(request.PackageId, request.Scope)
            : EnableRegistered(request.PackageId, request.Scope);
    }

    public ValueTask<HpdPackageCommitResult> CommitPackageChangeAsync(
        HpdPackagePreparedChange change,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Packages.CommitPrepared(change));

    public HpdPackageResponse? Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Packages.Packages
            .FirstOrDefault(package => string.Equals(package.Id, id, StringComparison.Ordinal))
            is { } package
                ? HpdPackageResponse.From(package)
                : null;
    }

    public HpdPackageActionResponse EnableRegistered(
        string id,
        string? scope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var package = Packages.EnableRegistered(id, NormalizeScope(scope));
        return new HpdPackageActionResponse(HpdPackageResponse.From(package));
    }

    public ValueTask<HpdLoadedPackage> EnableRegisteredAsync(
        string packageId,
        string scope = HpdPackageScopes.App,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Packages.EnableRegistered(packageId, NormalizeScope(scope)));

    public HpdPackageActionResponse ReloadRegistered(
        string id,
        string? scope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var package = HpdPackageRegistry.Find(id)
            ?? throw new KeyNotFoundException($"Package '{id}' is not registered.");
        var loaded = Packages.Reload(package, NormalizeScope(scope));
        return new HpdPackageActionResponse(HpdPackageResponse.From(loaded));
    }

    public ValueTask<HpdLoadedPackage> ReloadRegisteredAsync(
        string packageId,
        string scope = HpdPackageScopes.App,
        CancellationToken cancellationToken = default)
    {
        var package = HpdPackageRegistry.Find(packageId)
            ?? throw new KeyNotFoundException($"Package '{packageId}' is not registered.");
        return ValueTask.FromResult(Packages.Reload(package, NormalizeScope(scope)));
    }

    public HpdPackageDisableResponse Disable(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new HpdPackageDisableResponse(id, Packages.Disable(id));
    }

    public ValueTask<bool> DisableAsync(
        string packageId,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Packages.Disable(packageId));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _stalenessSubscription?.Dispose();
        _disposed = true;
    }

    private HpdPackageManager CreatePackageManager()
    {
        var config = _options.Get(_name);
        var packages = config.CreatePackageManager(_services);
        var agents = _serviceProvider.GetService<AgentManager>();
        if (agents != null)
        {
            _stalenessSubscription = packages.MarkAgentsStaleOnPackageChanges(agents);
        }

        return packages;
    }

    private static string NormalizeScope(string? scope)
        => string.IsNullOrWhiteSpace(scope)
            ? HpdPackageScopes.App
            : scope;
}
