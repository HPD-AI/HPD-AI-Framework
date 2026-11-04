namespace HPD.Agent.Sandbox.HostContainers;

using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using HPD.Environment.HostContainers;
using HPD.Environment.Runtime;
using Microsoft.Extensions.Logging;

/// <summary>
/// Adapts retained Host Containers execution units into one Agent sandbox
/// session with a shared process and workspace binding.
/// </summary>
public sealed class HostContainersSandboxMiddleware
    : IAgentMiddleware, IAsyncDisposable
{
    private readonly ILogger<HostContainersSandboxMiddleware>? _logger;
    private readonly HostContainersSandboxOptions _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private EnvironmentSandboxBackendFactory? _factory;
    private ISandboxSession? _session;
    private int _disposed;
    private bool _initialized;

    /// <summary>Creates the Host Containers Agent adapter.</summary>
    public HostContainersSandboxMiddleware(
        HostContainersSandboxOptions options,
        ILogger<HostContainersSandboxMiddleware>? logger = null)
    {
        _options = options ??
            throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <summary>Gets whether the sandbox session is initialized.</summary>
    public bool IsInitialized => _initialized;

    /// <summary>Gets the initialized Environment registry.</summary>
    public EnvironmentProviderRegistry? Registry => _factory?.Registry;

    /// <summary>Gets the Environment runtime used by the session.</summary>
    public IEnvironmentRuntime? Runtime => _factory?.Runtime;

    /// <summary>Gets the one session owned by this middleware.</summary>
    public ISandboxSession? Session => _session;

    /// <summary>Initializes the retained container session.</summary>
    public Task InitializeAsync(
        CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(cancellationToken);

    /// <inheritdoc />
    public async Task BeforeStartAsync(
        BeforeStartContext context,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);
        PublishCapabilities(context.RuntimeCapabilities);
        context.RegisterAsyncDisposable(this);
    }

    /// <inheritdoc />
    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        if (context.RuntimeCapabilities.TryGet<ISandboxSession>(out _))
        {
            return;
        }
        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);
        PublishCapabilities(context.RuntimeCapabilities);
    }

    /// <inheritdoc />
    public async Task BeforeStopAsync(
        BeforeStopContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DisposeRuntimeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        await DisposeRuntimeAsync().ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        if (_initialized)
        {
            return;
        }
        await _initLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            if (_initialized)
            {
                return;
            }
            string? stateRoot = _options.WorkloadStateRoot;
            _factory = new EnvironmentSandboxBackendFactory(
                new EnvironmentSandboxBackendDefinition
                {
                    Kind = SandboxBackendKind.HostContainers,
                    Module = new HostContainersProviderModule(
                        new HostContainersProviderOptions
                        {
                            EngineSocketPath =
                                Path.GetFullPath(
                                    _options.EngineSocketPath),
                            DockerCliPath =
                                _options.DockerCliPath,
                            WorkloadStateRoot = stateRoot,
                        }),
                    EnforcementLocation =
                        SandboxEnforcementLocation.Container,
                    HostSpecFactory = request =>
                        new RuntimeHostSpec
                        {
                            PreferredProvider =
                                HostContainersProviderDescriptor
                                    .ProviderId,
                            Platform =
                                HostContainersProviderDescriptor
                                    .CurrentPlatform(),
                            Capacity = request.Resources,
                            LifecyclePolicy =
                                request.Lifecycle,
                        },
                    UnitSpecFactory = (request, host) =>
                        CreateUnitSpec(request, host),
                });
            _session = await _factory.CreateSessionAsync(
                CreateSessionRequest(),
                cancellationToken).ConfigureAwait(false);
            foreach (HostContainersWorkspaceOptions workspace in
                     _options.Workspaces.Where(workspace =>
                         workspace.SourceKind ==
                         HostContainersWorkspaceSourceKind.CopyInOut))
            {
                await CopyWorkspaceToEnvironmentAsync(
                    workspace,
                    _session.Workspace.Files,
                    cancellationToken).ConfigureAwait(false);
            }
            _initialized = true;
            _logger?.LogInformation(
                "Host Containers sandbox session initialized.");
        }
        catch
        {
            if (_session is not null)
            {
                await _session.DisposeAsync().ConfigureAwait(false);
                _session = null;
            }
            if (_factory is not null)
            {
                await _factory.DisposeAsync().ConfigureAwait(false);
                _factory = null;
            }
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private SandboxSessionRequest CreateSessionRequest() =>
        new()
        {
            Isolation = _options.Isolation,
            Resources = _options.Resources,
            Workspaces = _options.Workspaces
                .Select(CreateWorkspaceRequest)
                .ToArray(),
        };

    private static SandboxWorkspaceRequest CreateWorkspaceRequest(
        HostContainersWorkspaceOptions workspace)
    {
        string containerPath =
            NormalizeContainerPath(workspace.ContainerPath);
        var rootId = new WorkspaceRootId(workspace.RootId);
        bool hostPath =
            workspace.SourceKind ==
            HostContainersWorkspaceSourceKind.HostPath;
        bool copyInOut =
            workspace.SourceKind ==
            HostContainersWorkspaceSourceKind.CopyInOut;
        if (copyInOut &&
            workspace.Access == AccessMode.ReadOnly)
        {
            throw new NotSupportedException(
                "HostContainers.CopyInOutReadOnlyUnsupported: copy-in/out currently requires a writable staging workspace so host content can be materialized before the model-facing session opens.");
        }
        bool hasHostSource = hostPath || copyInOut;
        string? rootPath = hasHostSource
            ? Path.GetFullPath(
                workspace.Path ??
                throw new ArgumentException(
                    "A HostPath workspace requires Path.",
                    nameof(workspace)))
            : null;
        string providerSourceId =
            workspace.ProviderSourceId?.Trim() ??
            string.Empty;
        if (!hasHostSource &&
            providerSourceId.Length == 0)
        {
            throw new ArgumentException(
                "A provider-owned workspace requires ProviderSourceId.",
                nameof(workspace));
        }
        return new SandboxWorkspaceRequest
        {
            Source = new ContentSelector
            {
                Kind = hostPath
                    ? ContentSelectorKind.HostPath
                    : ContentSelectorKind.ProviderSource,
                HostPath = hostPath
                    ? new HostPathSelection(
                        new HostPath(rootPath!),
                        HostPathKind.Directory)
                    : null,
                ProviderSource = hostPath
                    ? null
                    : new ProviderSourceSelection(
                        HostContainersProviderDescriptor.ProviderId,
                        copyInOut
                            ? $"copy-in-out:{rootId.Value}"
                            : providerSourceId),
            },
            Root = new WorkspaceRootBinding
            {
                Id = rootId,
                DisplayName = workspace.DisplayName ??
                    (hasHostSource
                        ? Path.GetFileName(
                            Path.TrimEndingDirectorySeparator(
                                rootPath!))
                        : providerSourceId),
                Access = workspace.Access,
                DefaultExecutionPath =
                    new WorkspacePath(rootId, string.Empty),
                Content = new WorkspaceContentIdentity(
                    workspace.SourceKind switch
                    {
                        HostContainersWorkspaceSourceKind.HostPath =>
                            $"host-containers:bind:{rootPath}",
                        HostContainersWorkspaceSourceKind.CopyInOut =>
                            $"host-containers:copy-in-out:{rootPath}",
                        _ =>
                            $"host-containers:provider:{providerSourceId}",
                    }),
                PathSemantics = new WorkspacePathSemantics(
                    CaseSensitive: true,
                    Symlinks:
                        SymlinkEvaluationPolicy.DenySymlinks),
            },
            ExecutionPath =
                new WorkspaceExecutionPath(containerPath),
            Role = ContentProjectionRole.Workspace,
            SyncPolicy = SyncPolicy.None,
            FinalizationPolicy = FinalizationPolicy.Explicit,
            SecurityPolicy =
                ContentProjectionSecurityPolicy.Default with
                {
                    AllowHostPathSource = hostPath,
                    AllowDirectSourceMutation =
                        workspace.Access != AccessMode.ReadOnly,
                    ReadOnlyEnforcement =
                        ReadOnlyEnforcementPolicy.GuestEnforced,
                },
        };
    }

    private ExecutionUnitSpec CreateUnitSpec(
        SandboxSessionRequest request,
        ResourceRef<RuntimeHost> host)
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
            Image = _options.Image,
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

    private void PublishCapabilities(
        IRuntimeCapabilityRegistry capabilities)
    {
        capabilities.Set(_factory!.Registry!);
        capabilities.Set(_factory.Runtime!);
        capabilities.Set(_session!);
        capabilities.Set(_session!.Execution);
        capabilities.Set(_session.Workspace);
    }

    private async Task DisposeRuntimeAsync()
    {
        ISandboxSession? session;
        EnvironmentSandboxBackendFactory? factory;
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            session = _session;
            factory = _factory;
            _session = null;
            _factory = null;
            _initialized = false;
        }
        finally
        {
            _initLock.Release();
        }
        if (session is not null)
        {
            try
            {
                foreach (HostContainersWorkspaceOptions workspace in
                         _options.Workspaces.Where(workspace =>
                             workspace.SourceKind ==
                                 HostContainersWorkspaceSourceKind
                                     .CopyInOut &&
                             workspace.Access != AccessMode.ReadOnly))
                {
                    await CopyWorkspaceFromEnvironmentAsync(
                        workspace,
                        session.Workspace.Files).ConfigureAwait(false);
                }
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
        if (factory is not null)
        {
            await factory.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task CopyWorkspaceToEnvironmentAsync(
        HostContainersWorkspaceOptions workspace,
        IWorkspaceFileStore files,
        CancellationToken cancellationToken)
    {
        string source = Path.GetFullPath(
            workspace.Path ??
            throw new ArgumentException(
                "A CopyInOut workspace requires Path.",
                nameof(workspace)));
        var root = new WorkspacePath(
            new WorkspaceRootId(workspace.RootId),
            string.Empty);
        await CopyHostDirectoryAsync(
            source,
            files,
            root,
            cancellationToken).ConfigureAwait(false);
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
        HostContainersWorkspaceOptions workspace,
        IWorkspaceFileStore files)
    {
        string destination = Path.GetFullPath(
            workspace.Path ??
            throw new ArgumentException(
                "A CopyInOut workspace requires Path.",
                nameof(workspace)));
        Directory.CreateDirectory(destination);
        WorkspaceListResultSet listing =
            await files.ListAsync(
                new WorkspacePath(
                    new WorkspaceRootId(workspace.RootId),
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
