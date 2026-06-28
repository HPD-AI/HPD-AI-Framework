using System.Net;
using System.Net.Http.Json;
using HPD.Agent;
using HPD.Agent.Packages;
using HPD.Agent.Providers;

namespace HPD.Agent.AspNetCore.Packages;

public sealed class HpdAspNetCorePackageRuntimeClient : IHpdPackageRuntime
{
    private readonly HttpClient _http;
    private readonly string _routePrefix;
    private IReadOnlyList<HpdLoadedPackage> _packages = [];

    public HpdAspNetCorePackageRuntimeClient(
        HttpClient http,
        string routePrefix = "api/hpd-agent/packages")
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _routePrefix = NormalizeRoutePrefix(routePrefix);
    }

    public event EventHandler<HpdPackageChangedEventArgs>? Changed;

    public IReadOnlyList<HpdLoadedPackage> Packages => _packages;

    public async ValueTask<IReadOnlyList<HpdLoadedPackage>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _http.GetFromJsonAsync<HpdPackageListResponse>(
            _routePrefix,
            cancellationToken).ConfigureAwait(false)
            ?? new HpdPackageListResponse([]);
        _packages = response.Packages.Select(ToLoadedPackage).ToArray();
        return _packages;
    }

    public async ValueTask<HpdPackagePrepareResult> PreparePackageChangeAsync(
        HpdPackageChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await _http.PostAsync(
            $"{_routePrefix}/{Uri.EscapeDataString(request.PackageId)}/prepare?scope={Uri.EscapeDataString(request.Scope)}&operation={Uri.EscapeDataString(request.Operation.ToString())}",
            content: null,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var prepare = await response.Content.ReadFromJsonAsync<HpdPackagePrepareResponse>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Package prepare endpoint returned an empty response.");
        return ToPrepareResult(request, prepare);
    }

    public async ValueTask<HpdPackageCommitResult> CommitPackageChangeAsync(
        HpdPackagePreparedChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var response = await _http.PostAsJsonAsync(
            $"{_routePrefix}/commit",
            new HpdPackagePrepareRequest(
                change.Request.PackageId,
                change.Request.Scope,
                change.Request.Operation),
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var action = await response.Content.ReadFromJsonAsync<HpdPackageActionResponse>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Package commit endpoint returned an empty response.");
        var package = ToLoadedPackage(action.Package);
        ApplyLocalPackage(package);
        OnChanged(change.Request.Operation == HpdPackageChangeOperation.Reload
            ? HpdPackageChangeKind.Reloaded
            : HpdPackageChangeKind.Enabled, package);
        return new HpdPackageCommitResult(
            package,
            Committed: package.State == HpdPackageLoadState.Enabled,
            PreviousActiveRetained: package.State != HpdPackageLoadState.Enabled);
    }

    public async ValueTask<HpdLoadedPackage> EnableRegisteredAsync(
        string packageId,
        string scope = HpdPackageScopes.App,
        CancellationToken cancellationToken = default)
    {
        var package = await PostPackageActionAsync(
            $"{Uri.EscapeDataString(packageId)}/enable?scope={Uri.EscapeDataString(scope)}",
            cancellationToken).ConfigureAwait(false);
        ApplyLocalPackage(package);
        OnChanged(HpdPackageChangeKind.Enabled, package);
        return package;
    }

    public async ValueTask<HpdLoadedPackage> ReloadRegisteredAsync(
        string packageId,
        string scope = HpdPackageScopes.App,
        CancellationToken cancellationToken = default)
    {
        var package = await PostPackageActionAsync(
            $"{Uri.EscapeDataString(packageId)}/reload?scope={Uri.EscapeDataString(scope)}",
            cancellationToken).ConfigureAwait(false);
        ApplyLocalPackage(package);
        OnChanged(HpdPackageChangeKind.Reloaded, package);
        return package;
    }

    public async ValueTask<bool> DisableAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        var response = await _http.PostAsync(
            $"{_routePrefix}/{Uri.EscapeDataString(packageId)}/disable",
            content: null,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var disabled = await response.Content.ReadFromJsonAsync<HpdPackageDisableResponse>(
            cancellationToken).ConfigureAwait(false)
            ?? new HpdPackageDisableResponse(packageId, false);

        if (disabled.Disabled)
        {
            var previous = _packages.FirstOrDefault(package =>
                string.Equals(package.Id, packageId, StringComparison.Ordinal));
            _packages = _packages
                .Where(package => !string.Equals(package.Id, packageId, StringComparison.Ordinal))
                .ToArray();
            if (previous != null)
            {
                OnChanged(HpdPackageChangeKind.Disabled, previous);
            }
        }

        return disabled.Disabled;
    }

    private async Task<HpdLoadedPackage> PostPackageActionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var response = await _http.PostAsync(
            $"{_routePrefix}/{path}",
            content: null,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var action = await response.Content.ReadFromJsonAsync<HpdPackageActionResponse>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Package endpoint returned an empty response.");
        return ToLoadedPackage(action.Package);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await response.Content.ReadFromJsonAsync<HpdPackageErrorResponse>(
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException(error?.Error ?? response.ReasonPhrase);
        }

        throw new InvalidOperationException(error?.Error ?? response.ReasonPhrase);
    }

    private void ApplyLocalPackage(HpdLoadedPackage package)
    {
        _packages = _packages
            .Where(candidate => !string.Equals(candidate.Id, package.Id, StringComparison.Ordinal))
            .Append(package)
            .OrderBy(static candidate => candidate.Scope, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static HpdLoadedPackage ToLoadedPackage(HpdPackageResponse package)
    {
        var version = Version.Parse(package.Version);
        var trust = Enum.TryParse<HpdPackageTrust>(package.Trust, out var parsedTrust)
            ? parsedTrust
            : HpdPackageTrust.Unknown;
        var loadMode = Enum.TryParse<HpdPackageLoadMode>(package.LoadMode, out var parsedLoadMode)
            ? parsedLoadMode
            : HpdPackageLoadMode.BuildTimeInProcess;
        var state = Enum.TryParse<HpdPackageLoadState>(package.State, out var parsedState)
            ? parsedState
            : HpdPackageLoadState.Failed;

        return new HpdLoadedPackage(
            package.Id,
            package.DisplayName,
            version,
            package.Scope,
            new HpdPackageManifest(package.Id, package.DisplayName, version)
            {
                Trust = trust,
                LoadMode = loadMode
            },
            new HpdContributionOwner(
                package.Id,
                package.Scope,
                package.Version,
                package.DisplayName),
            state,
            ToContributionSummary(package.Contributions),
            package.Impacts
                .Select(static impact => Enum.TryParse<HpdPackageChangeImpact>(impact, out var parsed)
                    ? parsed
                    : HpdPackageChangeImpact.LiveNow)
                .ToArray(),
            package.Diagnostics.Select(static diagnostic => new HpdPackageDiagnostic(
                Enum.TryParse<HpdPackageDiagnosticSeverity>(diagnostic.Severity, out var parsed)
                    ? parsed
                    : HpdPackageDiagnosticSeverity.Info,
                diagnostic.Message,
                diagnostic.Code)).ToArray());
    }

    private static HpdPackageContributionSummary ToContributionSummary(
        HpdPackageContributionSummaryResponse summary)
        => new(
            summary.AgentContributors,
            summary.ProviderFactories,
            summary.ProviderConfigSerializers,
            summary.SecretAliases,
            summary.ModelCatalogs,
            summary.RuntimeContributions ?? [],
            summary.ExternalProcesses ?? []);

    private static HpdPackagePrepareResult ToPrepareResult(
        HpdPackageChangeRequest request,
        HpdPackagePrepareResponse response)
    {
        var owner = new HpdContributionOwner(
            request.PackageId,
            request.Scope,
            DisplayName: request.PackageId);
        var package = new RemotePreparedPackage(request.PackageId);
        var state = response.CanCommit
            ? HpdPackageLoadState.Enabled
            : HpdPackageLoadState.Failed;
        var diagnostics = response.Diagnostics.Select(static diagnostic => new HpdPackageDiagnostic(
            Enum.TryParse<HpdPackageDiagnosticSeverity>(diagnostic.Severity, out var parsed)
                ? parsed
                : HpdPackageDiagnosticSeverity.Info,
            diagnostic.Message,
            diagnostic.Code)).ToArray();
        var change = new HpdPackagePreparedChange(
            request,
            package,
            owner,
            state,
            new HpdPackageContributionStores(
                new AgentBuilderContributorStore(),
                new ProviderContributionStore(),
                new HpdPackageRuntimeContributionStore(),
                new HpdExternalPackageProcessRuntime()),
            ToContributionSummary(response.Contributions),
            [HpdPackageChangeImpact.RequiresHostCoordination],
            diagnostics);
        return new HpdPackagePrepareResult(change);
    }

    private static string NormalizeRoutePrefix(string routePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routePrefix);
        return routePrefix.Trim('/');
    }

    private void OnChanged(
        HpdPackageChangeKind kind,
        HpdLoadedPackage package)
        => Changed?.Invoke(this, new HpdPackageChangedEventArgs(kind, package));

    private sealed class RemotePreparedPackage : IHpdPackage
    {
        public RemotePreparedPackage(string id)
        {
            Manifest = new HpdPackageManifest(id, id, new Version(0, 0));
        }

        public HpdPackageManifest Manifest { get; }

        public string Id => Manifest.Id;

        public string DisplayName => Manifest.DisplayName;

        public Version Version => Manifest.Version;

        public void Configure(IHpdPackageBuilder builder)
        {
        }
    }
}
