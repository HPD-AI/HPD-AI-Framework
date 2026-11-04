namespace HPD.Environment.HostContainers;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using HPD.Environment.Runtime.Workspaces;

/// <summary>
/// Opens host-side file operations for a verified local-engine bind mount. A
/// remote or opaque engine must provide a different provider implementation.
/// </summary>
internal sealed class HostContainersWorkspaceFileStoreProvider(
    HostContainersProviderState state,
    IProcessProvider processProvider)
    : IWorkspaceFileStoreProvider
{
    public ProviderId ProviderId =>
        HostContainersProviderDescriptor.ProviderId;

    public ValueTask<WorkspaceFileStoreAttachment> OpenAsync(
        WorkspaceFileStoreSpec spec,
        TargetHandle<ExecutionUnit>? unit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();
        if (unit is null)
        {
            throw new InvalidOperationException(
                "HostContainers.WorkspaceUnitRequired: workspace access requires an execution unit owned by this provider.");
        }
        ProviderLedgerLookup<
            ProviderResourceEntry<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>> lookup =
            state.Ledger.TryGet<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus>(unit.Value);
        if (!lookup.Succeeded ||
            lookup.Entry!.Status.UnitPhase is not (
                ExecutionUnitPhase.Ready or
                ExecutionUnitPhase.Running))
        {
            throw new InvalidOperationException(
                lookup.Diagnostic?.Message ??
                "HostContainers.WorkspaceUnitNotReady: the execution unit is not ready.");
        }
        ProviderResourceEntry<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> unitEntry = lookup.Entry;
        HostContainersSandboxContainer container =
            state.GetSandboxContainer(unitEntry.Resource.Id.Value) ??
            throw new InvalidOperationException(
                "HostContainers.WorkspaceContainerMissing: the execution unit does not own a retained sandbox container.");
        if (!spec.PathSemantics.CaseSensitive)
        {
            throw new NotSupportedException(
                "Host Containers Linux workspace paths require case-sensitive semantics.");
        }
        if (spec.Source.Kind == ContentSelectorKind.ProviderSource)
        {
            if (spec.Source.ProviderSource is not
                {
                    Provider: var sourceProvider,
                } providerSource ||
                !sourceProvider.Equals(ProviderId) ||
                string.IsNullOrWhiteSpace(
                    providerSource.SourceId))
            {
                throw new NotSupportedException(
                    "HostContainers.ProviderWorkspaceSourceInvalid: provider-owned workspace sources must be owned by this provider and identify a non-empty source.");
            }
            ProcessWorkspaceFileStore.ValidateExecutionRoot(
                spec.ExecutionPath);
            var guestStore = new ProcessWorkspaceFileStore(
                spec.RootId,
                spec.ExecutionPath.Value,
                spec.Access,
                spec.PathSemantics,
                processProvider,
                unit.Value,
                "HostContainers");
            return ValueTask.FromResult(
                new WorkspaceFileStoreAttachment
                {
                    Files = guestStore,
                    Paths = guestStore,
                    PathSemantics = spec.PathSemantics,
                    Lifetime = guestStore,
                });
        }

        if (spec.Source.Kind != ContentSelectorKind.HostPath ||
            spec.Source.HostPath is not
                { Kind: HostPathKind.Directory } source)
        {
            throw new NotSupportedException(
                "Host Containers workspace access requires a verified HostPath bind mount or a provider-owned source.");
        }
        HostContainersSandboxMount? mount = container.FindMount(
            source.Path.Value,
            spec.ExecutionPath.Value);
        if (mount is null || mount.Access != spec.Access)
        {
            throw new InvalidOperationException(
                "HostContainers.WorkspaceMountMismatch: workspace access must match a validated container bind mount and access mode.");
        }
        string hostSource = Path.GetFullPath(
            source.Path.Value);
        var store = new HostDirectoryWorkspaceFileStore(
            spec.RootId,
            hostSource,
            spec.Access,
            spec.PathSemantics,
            new ProcessWorkspaceRipgrepExecutor(
                processProvider,
                unit.Value));
        return ValueTask.FromResult(new WorkspaceFileStoreAttachment
        {
            Files = store,
            Paths = new HostContainersWorkspacePathMapper(
                spec.RootId,
                new WorkspaceExecutionPath(
                    mount.ContainerPath.Value)),
            PathSemantics = spec.PathSemantics,
            Lifetime = store,
        });
    }

    private sealed class HostContainersWorkspacePathMapper(
        WorkspaceRootId rootId,
        WorkspaceExecutionPath containerRoot)
        : IWorkspacePathMapper
    {
        public WorkspaceExecutionPath ToExecutionPath(
            WorkspacePath path)
        {
            if (!path.RootId.Equals(rootId))
            {
                throw new UnauthorizedAccessException(
                    $"Workspace root '{path.RootId.Value}' is not mapped by this container workspace.");
            }
            string relative = Normalize(path.RelativePath);
            return new WorkspaceExecutionPath(
                relative.Length == 0
                    ? containerRoot.Value
                    : containerRoot.Value.TrimEnd('/') +
                        "/" + relative);
        }

        public WorkspacePath? TryFromExecutionPath(
            WorkspaceExecutionPath path)
        {
            string root = containerRoot.Value.TrimEnd('/');
            if (!path.Value.StartsWith('/') ||
                path.Value.Contains('\\') ||
                path.Value.Contains('\0') ||
                !(string.Equals(
                        path.Value,
                        root,
                        StringComparison.Ordinal) ||
                  path.Value.StartsWith(
                      root + "/",
                      StringComparison.Ordinal)))
            {
                return null;
            }
            string relative = Normalize(
                path.Value[root.Length..].TrimStart('/'));
            return new WorkspacePath(
                rootId,
                relative);
        }

        public Uri ToExecutionFileUri(WorkspacePath path)
        {
            WorkspaceExecutionPath execution =
                ToExecutionPath(path);
            return new UriBuilder
            {
                Scheme = Uri.UriSchemeFile,
                Host = string.Empty,
                Path = execution.Value,
            }.Uri;
        }

        public WorkspacePath? TryFromExecutionFileUri(Uri uri)
        {
            if (!uri.IsAbsoluteUri ||
                !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeFile,
                    StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Length > 0)
            {
                return null;
            }
            string value = Uri.UnescapeDataString(
                uri.AbsolutePath);
            string root = containerRoot.Value.TrimEnd('/');
            if (!string.Equals(
                    value,
                    root,
                    StringComparison.Ordinal) &&
                !value.StartsWith(
                    root + "/",
                    StringComparison.Ordinal))
            {
                return null;
            }
            return new WorkspacePath(
                rootId,
                Normalize(
                    value[root.Length..].TrimStart('/')));
        }

        private static string Normalize(string value)
        {
            var segments = new List<string>();
            foreach (string segment in value.Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                {
                    continue;
                }
                if (segment == "..")
                {
                    throw new UnauthorizedAccessException(
                        "Workspace path traversal is denied.");
                }
                segments.Add(segment);
            }
            return string.Join('/', segments);
        }
    }
}
