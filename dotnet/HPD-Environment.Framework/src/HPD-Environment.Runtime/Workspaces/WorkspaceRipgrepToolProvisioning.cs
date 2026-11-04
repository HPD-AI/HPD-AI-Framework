namespace HPD.Environment.Runtime.Workspaces;

/// <summary>
/// Resolves a ripgrep executable in the selected execution environment.
/// Implementations may materialize a verified bundle, use a provider image
/// asset, or return null when no tool is available.
/// </summary>
internal interface IWorkspaceRipgrepToolProvisioner
{
    /// <summary>
    /// Ensures a ripgrep executable is available or returns null.
    /// </summary>
    ValueTask<WorkspaceRipgrepTool?> EnsureAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One provisioned ripgrep executable and its immutable provenance.
/// </summary>
/// <param name="RootPath">Provider/session path containing the tool bundle.</param>
/// <param name="ExecutablePath">Absolute path to the executable or loader.</param>
/// <param name="ArgumentsPrefix">
/// Arguments that must precede the generated ripgrep arguments.
/// </param>
/// <param name="Version">Upstream ripgrep version.</param>
/// <param name="Target">Target runtime identity used for selection.</param>
/// <param name="Digest">Digest of the provisioned bundle.</param>
/// <param name="Source">Provisioning source identifier.</param>
internal sealed record WorkspaceRipgrepTool(
    string RootPath,
    string ExecutablePath,
    IReadOnlyList<string> ArgumentsPrefix,
    string Version,
    string Target,
    string Digest,
    string Source);
