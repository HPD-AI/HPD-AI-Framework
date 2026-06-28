using HPD.Agent.Packages;

namespace HPD.Agent.AspNetCore.Packages;

public sealed record HpdPackageListResponse(
    IReadOnlyList<HpdPackageResponse> Packages);

public sealed record HpdPackageResponse(
    string Id,
    string DisplayName,
    string Version,
    string Scope,
    string State,
    string Trust,
    string LoadMode,
    HpdPackageContributionSummaryResponse Contributions,
    IReadOnlyList<string> Impacts,
    IReadOnlyList<HpdPackageDiagnosticResponse> Diagnostics)
{
    public static HpdPackageResponse From(HpdLoadedPackage package)
        => new(
            package.Id,
            package.DisplayName,
            package.Version.ToString(),
            package.Scope,
            package.State.ToString(),
            package.Manifest.Trust.ToString(),
            package.Manifest.LoadMode.ToString(),
            HpdPackageContributionSummaryResponse.From(package.Contributions),
            package.Impacts.Select(static impact => impact.ToString()).ToArray(),
            package.Diagnostics.Select(HpdPackageDiagnosticResponse.From).ToArray());
}

public sealed record HpdPackageContributionSummaryResponse(
    IReadOnlyList<string> AgentContributors,
    IReadOnlyList<string> ProviderFactories,
    IReadOnlyList<string> ProviderConfigSerializers,
    IReadOnlyList<string> SecretAliases,
    IReadOnlyList<string> ModelCatalogs,
    IReadOnlyList<string>? RuntimeContributions = null,
    IReadOnlyList<string>? ExternalProcesses = null)
{
    public static HpdPackageContributionSummaryResponse From(HpdPackageContributionSummary summary)
        => new(
            summary.AgentContributors,
            summary.ProviderFactories,
            summary.ProviderConfigSerializers,
            summary.SecretAliases,
            summary.ModelCatalogs,
            summary.RuntimeContributions ?? [],
            summary.ExternalProcesses ?? []);
}

public sealed record HpdPackageDiagnosticResponse(
    string Severity,
    string Message,
    string? Code)
{
    public static HpdPackageDiagnosticResponse From(HpdPackageDiagnostic diagnostic)
        => new(
            diagnostic.Severity.ToString(),
            diagnostic.Message,
            diagnostic.Code);
}

public sealed record HpdPackageActionResponse(
    HpdPackageResponse Package);

public sealed record HpdPackagePrepareRequest(
    string PackageId,
    string Scope = HpdPackageScopes.App,
    HpdPackageChangeOperation Operation = HpdPackageChangeOperation.Enable);

public sealed record HpdPackagePrepareResponse(
    bool CanCommit,
    HpdPackageContributionSummaryResponse Contributions,
    IReadOnlyList<HpdPackageDiagnosticResponse> Diagnostics)
{
    public static HpdPackagePrepareResponse From(HpdPackagePrepareResult result)
        => new(
            result.CanCommit,
            HpdPackageContributionSummaryResponse.From(result.Contributions),
            result.Diagnostics.Select(HpdPackageDiagnosticResponse.From).ToArray());
}

public sealed record HpdPackageDisableResponse(
    string Id,
    bool Disabled);

public sealed record HpdPackageErrorResponse(
    string Error);
