namespace HPD.Environment.HostProcess;

using System.Runtime.InteropServices;
using HPD.Environment.Contracts;
using HPD.Environment.HostProcess.ProcessIsolation;
using HPD.Environment.HostProcess.Security;
using HPD.Environment.Runtime;
using HPD.Environment.HostProcess.Platforms.Windows;
using Microsoft.Extensions.Logging;

/// <summary>
/// Registers direct host process execution, operating-system enforcement,
/// content visibility, and capability reporting as one Environment backend.
/// </summary>
public sealed class HostProcessProviderModule : ISandboxProvisioningProvider, IAsyncDisposable
{
    private readonly HostProcessProviderState _state = new();
    private readonly ILogger<HostProcessProviderModule>? _logger;
    private readonly SandboxIsolationManager _isolationManager;
    private readonly HostProcessProcessProvider _processProvider;
    private List<IDisposable>? _workspaces;
    private IDisposable? _profile;

    public HostProcessProviderModule(
        ILogger<HostProcessProviderModule>? logger = null)
    {
        _logger = logger;
        _isolationManager = new SandboxIsolationManager(logger);
        _processProvider = new HostProcessProcessProvider(
            _state,
            new SandboxIsolationPlanner(),
            new HostSandboxApplicator(_isolationManager));
    }

    public ProviderDescriptor Descriptor { get; } =
        HostProcessProviderDescriptor.Create();

    public IProcessProvider ProcessProvider => _processProvider;

    public ISandboxProvisioningProvider CreateSessionProvider() => new HostProcessProviderModule(_logger);

    public SandboxProvisioningPlan PrepareSandbox(
        SandboxProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Workspaces.Any(workspace => workspace.CopyInOut))
            throw new NotSupportedException("Host Process requires live workspace projection.");

        var readOnlyRoots = request.Workspaces.Where(workspace => workspace.Root.Access == AccessMode.ReadOnly).ToArray();
        var writableRoots = request.Workspaces.Where(workspace => workspace.Root.Access == AccessMode.ReadWrite).ToArray();
        foreach (var readOnly in readOnlyRoots)
        {
            foreach (var writable in writableRoots)
            {
                string readOnlyPath = readOnly.ExecutionPath.Value;
                string writablePath = writable.ExecutionPath.Value;
                if (PathNormalizer.IsWithinPath(readOnlyPath, writablePath) ||
                    PathNormalizer.IsWithinPath(writablePath, readOnlyPath))
                    throw new ArgumentException("Read-only and writable host workspace roots must be disjoint.", nameof(request));
            }
        }

        var isolation = request.Isolation;
        if (isolation.Mode == ProcessIsolationMode.Disabled && readOnlyRoots.Length != 0)
            throw new InvalidOperationException("Read-only host workspaces require process isolation.");
        if (request.Workspaces.Count > 0 && isolation.Mode == ProcessIsolationMode.ProviderDefault)
            isolation = isolation with { Mode = ProcessIsolationMode.Isolated };

        if (!OperatingSystem.IsWindows() && isolation.Mode == ProcessIsolationMode.Isolated)
        {
            var workspaceRules = request.Workspaces.SelectMany(static workspace => new[]
            {
                new PathAccessRule { Kind = PathAccessRuleKind.AllowRead, Path = new HostPath(workspace.ExecutionPath.Value) },
                new PathAccessRule
                {
                    Kind = workspace.Root.Access == AccessMode.ReadWrite ? PathAccessRuleKind.AllowWrite : PathAccessRuleKind.DenyWrite,
                    Path = new HostPath(workspace.ExecutionPath.Value),
                },
            });
            isolation = isolation with
            {
                Filesystem = isolation.Filesystem with
                {
                    Rules = [.. isolation.Filesystem.Rules, .. workspaceRules],
                },
            };
        }
        bool liveWorkspace = OperatingSystem.IsWindows() && request.Workspaces.Count > 0 &&
            request.Workspaces.All(workspace =>
                workspace.Source is { Kind: ContentSelectorKind.HostPath, HostPath.Kind: HostPathKind.Directory } &&
                workspace.Root.Access is AccessMode.ReadWrite or AccessMode.ReadOnly) &&
            isolation.Filesystem.Rules.Count == 0 &&
            isolation.Network.Mode == NetworkEgressMode.Blocked &&
            isolation.Mode != ProcessIsolationMode.Disabled;

        if (liveWorkspace && OperatingSystem.IsWindows())
        {
            BindWindowsWorkspaces(
                request.Workspaces.Select(workspace =>
                    (workspace.Source.HostPath!.Path.Value, workspace.Root.Access)).ToArray(),
                cancellationToken);
        }

        return new SandboxProvisioningPlan(
            new RuntimeHostSpec
            {
                PreferredProvider = Descriptor.Id,
                Platform = HostProcessProviderDescriptor.CurrentPlatform(),
                Capacity = request.Resources,
                LifecyclePolicy = request.Lifecycle,
            },
            SandboxEnforcementLocation.Host,
            report => RefineSandboxCapabilities(
                report, isolation, request.Workspaces.Count != 0 && !liveWorkspace))
        {
            Isolation = isolation,
        };
    }

    /// <summary>Grants access to a live Windows workspace for the lifetime of this provider module.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void BindWindowsWorkspaces(
        IReadOnlyList<(string Path, AccessMode Access)> roots, CancellationToken cancellationToken)
    {
        if (roots.Count == 0 || roots.Any(root =>
            root.Access is not (AccessMode.ReadOnly or AccessMode.ReadWrite) || !Path.IsPathFullyQualified(root.Path)))
            throw new ArgumentException("Live Windows workspaces require absolute paths and read-only or read/write access.", nameof(roots));
        if (_workspaces is not null)
            throw new InvalidOperationException("The module already owns Windows workspace grants.");

        var paths = roots.Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Path))).ToArray();
        for (int i = 0; i < paths.Length; i++)
        {
            for (int j = 0; j < i; j++)
            {
                if (WindowsSandboxWorkspace.ContainsDirectory(paths[i], paths[j]) ||
                    WindowsSandboxWorkspace.ContainsDirectory(paths[j], paths[i]))
                    throw new ArgumentException("Windows workspace roots must be disjoint.", nameof(roots));
            }
        }

        var profile = new WindowsAppContainerProfile();
        List<IDisposable> grants = [];
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                grants.Add(new WindowsWorkspaceGrant(paths[i], profile.Sid, roots[i].Access == AccessMode.ReadWrite));
            }
            _processProvider.SetWindowsWorkspaces(profile, paths);
            _workspaces = grants;
            _profile = profile;
        }
        catch
        {
            try { ReleaseWorkspaceGrants(grants); }
            finally { profile.Dispose(); }
            throw;
        }
    }

    /// <summary>Refines Windows capability facts for a specific process policy.</summary>
    public static ProviderCapabilityReport RefineSandboxCapabilities(
        ProviderCapabilityReport report, ProcessIsolationPolicy policy, bool hasWorkspaceBindings)
    {
        if (!OperatingSystem.IsWindows())
            return report;

        bool supported = !hasWorkspaceBindings &&
            WindowsProcessIsolationBackend.CanUseAppContainer(SandboxIsolationCompiler.Compile(policy)) &&
            policy.Mode != ProcessIsolationMode.Disabled;
        var appContainerFacts = new HashSet<CapabilityId>
        {
            StandardSandboxEnvironmentCapabilities.ProcessExecution,
            StandardSandboxEnvironmentCapabilities.ProcessOutput,
            StandardSandboxEnvironmentCapabilities.ProcessCancellation,
            StandardSandboxEnvironmentCapabilities.FilesystemReadRestrictions,
            StandardSandboxEnvironmentCapabilities.FilesystemWriteRestrictions,
            StandardSandboxEnvironmentCapabilities.SymlinkHandling,
            StandardSandboxEnvironmentCapabilities.SessionCleanup,
            StandardSandboxEnvironmentCapabilities.NetworkBlock,
            StandardSandboxEnvironmentCapabilities.StandardInput,
            StandardSandboxEnvironmentCapabilities.EnvironmentFiltering,
        };
        return report with
        {
            Capabilities = report.Capabilities.Select(fact => !appContainerFacts.Contains(fact.Id) ? fact : fact with
            {
                State = supported ? CapabilityState.Supported : CapabilityState.Unsupported,
                Detail = supported
                    ? "Supported by the Windows AppContainer command launcher."
                    : hasWorkspaceBindings
                        ? "Windows AppContainer sessions with shared workspace bindings require a supported live workspace grant."
                        : "The requested process policy cannot be enforced by the Windows AppContainer launcher.",
            }).ToArray(),
        };
    }

    public void Register(IProviderRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddProviderCapabilityReporter(
            new HostProcessCapabilityReporter());
        builder.AddRuntimeHostProvider(
            new HostProcessRuntimeHostProvider(_state));
        builder.AddExecutionUnitProvider(
            new HostProcessExecutionUnitProvider(_state));
        builder.AddProcessProvider(_processProvider);
        builder.AddContentProjectionProvider(
            new HostProcessContentProjectionProvider(_state));
        builder.AddWorkspaceFileStoreProvider(
            new HostProcessWorkspaceFileStoreProvider(
                _processProvider));
    }

    public void RegisterJsonTypes(IProviderJsonTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _isolationManager.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { ReleaseWorkspaceGrants(Interlocked.Exchange(ref _workspaces, null)); }
            finally { Interlocked.Exchange(ref _profile, null)?.Dispose(); }
        }
    }

    private static void ReleaseWorkspaceGrants(List<IDisposable>? grants)
    {
        if (!OperatingSystem.IsWindows() || grants is null)
            return;
        List<Exception> failures = [];
        foreach (var grant in grants)
        {
            try { grant.Dispose(); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count > 0)
            throw new AggregateException("Windows workspace grant cleanup failed.", failures);
    }
}

/// <summary>Stable Host Process provider identity and platform information.</summary>
public static class HostProcessProviderDescriptor
{
    public static ProviderId ProviderId { get; } =
        new("hpd.execution.host-process");

    public static PlatformSpec CurrentPlatform() =>
        new(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "windows"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "macos"
                    : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                        ? "linux"
                        : "unknown",
            RuntimeInformation.ProcessArchitecture
                .ToString()
                .ToLowerInvariant());

    internal static ProviderDescriptor Create() =>
        new()
        {
            Id = ProviderId,
            DisplayName = "HPD Host Process Environment",
            ContractVersion = new SemanticVersion(1, 0, 0),
            ProviderVersion = new SemanticVersion(0, 1, 0, "preview"),
            ContractKinds =
                ProviderContractKind.RuntimeHost |
                ProviderContractKind.ExecutionUnit |
                ProviderContractKind.ProcessInvocation |
                ProviderContractKind.ProcessIsolation |
                ProviderContractKind.ContentProjection |
                ProviderContractKind.WorkspaceFileStore,
            TrustLevel = ProviderTrustLevel.BuiltIn,
            DefaultActivationScope = ProviderActivationScope.Runtime,
            SupportedActivationScopes = [ProviderActivationScope.Runtime],
            ActivationModels =
            [
                new ProviderActivationModel(
                    ProviderActivationKind.InProcess,
                    ProviderActivationScope.Runtime,
                    ProviderTransportKind.None),
            ],
            HostPlatforms =
            [
                new PlatformSpec("macos", "arm64"),
                new PlatformSpec("macos", "x64"),
                new PlatformSpec("linux", "arm64"),
                new PlatformSpec("linux", "x64"),
                new PlatformSpec("windows", "arm64"),
                new PlatformSpec("windows", "x64"),
            ],
            Discovery =
            [
                new ProviderDiscoveryDescriptor(
                    ProviderDiscoveryKind.StaticModule),
            ],
        };
}

public static class HostProcessRegistrationExtensions
{
    /// <summary>Registers the Host Process Environment backend.</summary>
    public static EnvironmentProviderRegistry RegisterHostProcess(
        this EnvironmentProviderRegistry registry,
        HostProcessProviderModule? module = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.RegisterModule(module ?? new HostProcessProviderModule());
        return registry;
    }
}
