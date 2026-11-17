namespace HPD.Environment.HostContainers;

using HPD.Environment.Contracts;

public sealed partial class HostContainersProviderModule
{
    public ISandboxProvisioningProvider CreateSessionProvider() => new HostContainersProviderModule(_options);

    public SandboxProvisioningPlan PrepareSandbox(
        SandboxProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string image = _options.SandboxImage is { Length: > 0 } configuredImage
            ? configuredImage
            : throw new InvalidOperationException("A SandboxImage is required for a retained container sandbox.");
        var staged = request.Workspaces.Where(workspace => workspace.CopyInOut).ToArray();
        foreach (var workspace in staged)
        {
            if (workspace.Source is not { Kind: ContentSelectorKind.HostPath, HostPath.Kind: HostPathKind.Directory } ||
                workspace.Root.Access != AccessMode.ReadWrite)
                throw new NotSupportedException("Staged workspace copy-in/out requires a writable host directory.");
        }

        var workspaces = request.Workspaces.Select(workspace => workspace with
        {
            ExecutionPath = new WorkspaceExecutionPath(NormalizeContainerPath(workspace.ExecutionPath.Value)),
            Source = workspace.CopyInOut
                ? new ContentSelector
                {
                    Kind = ContentSelectorKind.ProviderSource,
                    ProviderSource = new ProviderSourceSelection(
                        Descriptor.Id, $"copy-in-out:{workspace.Root.Id.Value}"),
                }
                : workspace.Source,
            Root = workspace.Root with { PathSemantics = new WorkspacePathSemantics(true, SymlinkEvaluationPolicy.DenySymlinks) },
            SecurityPolicy = workspace.SecurityPolicy with { ReadOnlyEnforcement = ReadOnlyEnforcementPolicy.GuestEnforced },
        }).ToArray();

        return new SandboxProvisioningPlan(new RuntimeHostSpec
        {
            PreferredProvider = Descriptor.Id,
            Platform = HostContainersProviderDescriptor.CurrentPlatform(),
            Capacity = request.Resources,
            LifecyclePolicy = request.Lifecycle,
        }, SandboxEnforcementLocation.Container)
        {
            Workspaces = workspaces,
            CreateUnit = host => CreateUnitSpec(request with { Workspaces = workspaces }, host, image),
            InitializeWorkspace = async (files, token) =>
            {
                foreach (var workspace in staged)
                {
                    await CopyHostDirectoryAsync(
                        Path.GetFullPath(workspace.Source.HostPath!.Path.Value),
                        files,
                        new WorkspacePath(workspace.Root.Id, string.Empty),
                        token).ConfigureAwait(false);
                }
            },
            FinalizeWorkspace = async files =>
            {
                foreach (var workspace in staged)
                    await CopyWorkspaceFromEnvironmentAsync(workspace, files).ConfigureAwait(false);
            },
        };
    }

    private static ExecutionUnitSpec CreateUnitSpec(
        SandboxProvisioningRequest request,
        ResourceRef<RuntimeHost> host, string image)
    {
        SandboxWorkspaceRequest? first = request.Workspaces.FirstOrDefault();
        var mounts = request.Workspaces
            .Where(workspace =>
                workspace.Source.Kind ==
                    ContentSelectorKind.HostPath)
            .Select(workspace =>
                new HostContainersSandboxMount
                {
                    HostSource =
                        workspace.Source.HostPath!.Path,
                    ContainerPath = new GuestPath(
                        workspace.ExecutionPath.Value),
                    Access = workspace.Root.Access,
                })
            .ToArray();
        HostContainersSandboxNetworkMode network =
            request.Isolation.Network.Mode switch
            {
                NetworkEgressMode.Blocked =>
                    HostContainersSandboxNetworkMode.None,
                NetworkEgressMode.Unrestricted =>
                    HostContainersSandboxNetworkMode.EngineDefault,
                _ => throw new NotSupportedException(
                    "Filtered container egress is not implemented by the retained Host Containers sandbox."),
            };
        var options = new HostContainersSandboxUnitOptions
        {
            Image = image,
            WorkingDirectory = new GuestPath(
                first?.ExecutionPath.Value ?? "/"),
            Mounts = mounts,
            NetworkMode = network,
            Environment =
                new Dictionary<string, string>(
                    request.Isolation.Environment
                        .InjectedVariables,
                    StringComparer.Ordinal),
            ReadOnlyRootFilesystem = true,
        };
        return new ExecutionUnitSpec
        {
            PreferredHost = host,
            QuotaPolicy = request.Resources,
            LifecyclePolicy = request.Lifecycle,
        }.WithSandbox(options);
    }

    private static async Task CopyHostDirectoryAsync(
        string hostDirectory,
        IWorkspaceFileStore files,
        WorkspacePath logicalDirectory,
        CancellationToken cancellationToken)
    {
        await files.CreateDirectoryAsync(
            logicalDirectory,
            cancellationToken).ConfigureAwait(false);
        foreach (string entry in Directory.EnumerateFileSystemEntries(
                     hostDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException(
                    $"Copy-in workspace symlink '{entry}' is denied.");
            }
            WorkspacePath logical = new(
                logicalDirectory.RootId,
                logicalDirectory.RelativePath.Length == 0
                    ? Path.GetFileName(entry)
                    : logicalDirectory.RelativePath.TrimEnd('/') +
                        "/" + Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                await CopyHostDirectoryAsync(
                    entry,
                    files,
                    logical,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }
            byte[] content = await File.ReadAllBytesAsync(
                entry,
                cancellationToken).ConfigureAwait(false);
            WorkspaceMutationResult write =
                await files.WriteBytesAsync(
                    logical,
                    content,
                    new WorkspaceWriteCondition(),
                    cancellationToken).ConfigureAwait(false);
            if (write.Outcome != WorkspaceMutationOutcome.Succeeded)
            {
                throw new IOException(
                    write.Message ??
                    $"Copy-in failed for '{logical}'.");
            }
        }
    }

    private static async Task CopyWorkspaceFromEnvironmentAsync(
        SandboxWorkspaceRequest workspace,
        IWorkspaceFileStore files)
    {
        string destination = Path.GetFullPath(
            workspace.Source.HostPath?.Path.Value ??
            throw new ArgumentException(
                "A CopyInOut workspace requires Path.",
                nameof(workspace)));
        Directory.CreateDirectory(destination);
        WorkspaceListResultSet listing =
            await files.ListAsync(
                new WorkspacePath(
                    workspace.Root.Id,
                    string.Empty),
                new WorkspaceListOptions
                {
                    Recursive = true,
                    IncludeHidden = true,
                    RespectIgnoreFiles = false,
                    MaxEntries = 100_000,
                },
                CancellationToken.None).ConfigureAwait(false);
        if (listing.Status is
            WorkspaceListStatus.Failed or
            WorkspaceListStatus.TimedOut or
            WorkspaceListStatus.Cancelled)
        {
            throw new IOException(
                listing.Message ??
                "Copy-out workspace enumeration did not complete.");
        }
        foreach (WorkspaceDirectoryEntry entry in listing.Entries)
        {
            string target = Path.GetFullPath(
                Path.Combine(
                    destination,
                    entry.Path.RelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
            string relative = Path.GetRelativePath(
                destination,
                target);
            if (relative == ".." ||
                relative.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
            {
                throw new UnauthorizedAccessException(
                    $"Copy-out path '{entry.Path}' escapes the host workspace.");
            }
            if (entry.Kind == WorkspaceEntryKind.Directory)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(
                Path.GetDirectoryName(target) ?? destination);
            await using FileStream output = new(
                target,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true);
            long offset = 0;
            while (true)
            {
                WorkspaceBinarySnapshot? snapshot =
                    await files.ReadBytesAsync(
                        entry.Path,
                        new WorkspaceByteReadOptions
                        {
                            Offset = offset,
                            MaxBytes = 1024 * 1024,
                        }).ConfigureAwait(false);
                if (snapshot is null)
                {
                    throw new IOException(
                        $"Copy-out source '{entry.Path}' disappeared.");
                }
                await output.WriteAsync(
                    snapshot.Content,
                    CancellationToken.None).ConfigureAwait(false);
                offset += snapshot.Content.Length;
                if (!snapshot.Truncated ||
                    snapshot.Content.Length == 0)
                {
                    break;
                }
            }
        }
    }

    private static string NormalizeContainerPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith('/') ||
            path.Contains('\\') ||
            path.Contains('\0') ||
            path.Split('/').Any(segment =>
                segment is "." or ".."))
        {
            throw new ArgumentException(
                $"Container workspace path '{path}' must be absolute and normalized.",
                nameof(path));
        }
        return path.Length > 1
            ? path.TrimEnd('/')
            : path;
    }
}
