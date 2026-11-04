namespace HPD.Environment.HostProcess;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime.Workspaces;

/// <summary>
/// Opens Host Process workspace operations over one already-authorized host
/// directory. The execution path must equal the source path because Host
/// Process cannot remap content.
/// </summary>
internal sealed class HostProcessWorkspaceFileStoreProvider(
    IProcessProvider processProvider)
    : IWorkspaceFileStoreProvider
{
    public ProviderId ProviderId =>
        HostProcessProviderDescriptor.ProviderId;

    public ValueTask<WorkspaceFileStoreAttachment> OpenAsync(
        WorkspaceFileStoreSpec spec,
        TargetHandle<ExecutionUnit>? unit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();
        if (unit is null ||
            unit.Value.Route.ProviderId is not { } provider ||
            !provider.Equals(ProviderId))
        {
            throw new InvalidOperationException(
                "Host Process workspace access requires an execution unit owned by the Host Process provider.");
        }
        if (spec.Source.Kind != ContentSelectorKind.HostPath ||
            spec.Source.HostPath is not { Kind: HostPathKind.Directory } source)
        {
            throw new NotSupportedException(
                "The initial Host Process workspace profile requires an explicitly authorized HostPath directory source.");
        }
        if (spec.Source.PathPrefix is not null ||
            spec.Source.Filters.Count > 0)
        {
            throw new NotSupportedException(
                "The initial Host Process workspace profile does not support content filters.");
        }
        if (spec.Access is not (AccessMode.ReadOnly or AccessMode.ReadWrite))
        {
            throw new NotSupportedException(
                $"Host Process workspace access mode '{spec.Access}' is not supported.");
        }
        if (spec.PathSemantics.Symlinks !=
            SymlinkEvaluationPolicy.DenySymlinks)
        {
            throw new NotSupportedException(
                "Host Process workspace access currently requires deny-symlink path semantics.");
        }

        string sourcePath = Path.GetFullPath(source.Path.Value);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(
                $"Workspace source '{sourcePath}' does not exist.");
        }
        string executionPath = Path.GetFullPath(spec.ExecutionPath.Value);
        StringComparison comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        if (!string.Equals(sourcePath, executionPath, comparison))
        {
            throw new NotSupportedException(
                "Host Process cannot remap a workspace to a different execution path; source and destination must match.");
        }

        var store = new HostDirectoryWorkspaceFileStore(
            spec.RootId,
            sourcePath,
            spec.Access,
            spec.PathSemantics,
            new ProcessWorkspaceRipgrepExecutor(
                processProvider,
                unit.Value));
        return ValueTask.FromResult(new WorkspaceFileStoreAttachment
        {
            Files = store,
            Paths = store,
            PathSemantics = spec.PathSemantics,
            Lifetime = store,
        });
    }
}
