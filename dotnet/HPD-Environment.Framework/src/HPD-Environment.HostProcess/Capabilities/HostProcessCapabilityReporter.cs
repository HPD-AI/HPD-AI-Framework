namespace HPD.Environment.HostProcess;

using HPD.Environment.Contracts;
using HPD.Environment.HostProcess.Platforms;
using HPD.Environment.HostProcess.Platforms.Linux;
using HPD.Environment.HostProcess.Platforms.MacOS;
using HPD.Environment.HostProcess.Platforms.Windows;
using HPD.Environment.Runtime;

/// <summary>
/// Reports the isolation features that the current Host Process mechanism can
/// enforce. Unsupported features remain explicit so Agent admission fails closed.
/// </summary>
internal sealed class HostProcessCapabilityReporter
    : IProviderCapabilityReporter
{
    public async ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(
        ProviderId providerId,
        CancellationToken cancellationToken = default) =>
        await GetCapabilitiesAsync(
            providerId,
            new ProviderCapabilityQuery(),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<ProviderCapabilityReport> GetCapabilitiesAsync(
        ProviderId providerId,
        ProviderCapabilityQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!providerId.Equals(
                HostProcessProviderDescriptor.ProviderId))
        {
            throw new InvalidOperationException(
                $"Provider '{providerId.Value}' is not the Host Process provider.");
        }

        bool windows = OperatingSystem.IsWindows();
        ProcessIsolationPolicy probePolicy =
            windows
                ? ProcessIsolationPolicy.Default with
                {
                    Degradation =
                        ProcessIsolationDegradationPolicy
                            .FailClosed with
                        {
                            Mode =
                                ProcessIsolationDegradationMode
                                    .AllowProviderDegraded,
                        },
                }
                : ProcessIsolationPolicy.Default;
        SandboxIsolationPlan probePlan =
            SandboxIsolationCompiler.Compile(probePolicy);
        await using ISandboxBackend? backend =
            OperatingSystem.IsMacOS()
                ? new MacOSProcessIsolationBackend(
                    probePlan,
                    httpProxy: null,
                    socksProxy: null)
                : OperatingSystem.IsLinux()
                    ? new LinuxProcessIsolationBackend(
                        probePlan,
                        httpProxy: null,
                        socksProxy: null)
                    : windows
                        ? new WindowsProcessIsolationBackend(
                            probePlan)
                        : null;
        ProcessIsolationDependencyCheck dependency =
            backend is null
                ? ProcessIsolationDependencyCheck.FromIssues(
                [
                    ProcessIsolationDependencyIssue.Error(
                        "hpd.environment.host-process.platform-unsupported",
                        "platform",
                        "No accepted Host Process enforcement mechanism is available on this platform."),
                ])
                : await backend
                    .GetDependencyCheckAsync(cancellationToken)
                    .ConfigureAwait(false);
        bool supported = dependency.IsAvailable;
        CapabilityState isolation = !supported
            ? CapabilityState.Unsupported
            : windows
                ? CapabilityState.RequiresConfiguration
                : CapabilityState.Supported;
        CapabilityState workspace =
            windows
                ? CapabilityState.Supported
                : isolation;
        string platformDetail = supported
            ? "Available through the selected and dependency-checked host enforcement mechanism."
            : string.Join("; ", dependency.Errors);

        CapabilityId[] standardIds =
        [
            StandardEnvironmentCapabilities.ProcessIsolation,
            StandardEnvironmentCapabilities.ContainerIsolation,
            StandardEnvironmentCapabilities.SharedHostKernel,
            StandardEnvironmentCapabilities.HardwareVirtualization,
            StandardEnvironmentCapabilities.GuestAgentBoundary,
            StandardEnvironmentCapabilities.MediatedEngineAuthority,
            StandardEnvironmentCapabilities.HostLocalEndpointPublication,
        ];
        List<CapabilityFact> facts = standardIds
            .Select(id => StandardFact(id, isolation, platformDetail))
            .ToList();

        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.ProcessExecution,
            isolation,
            platformDetail);
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.ProcessOutput,
            isolation,
            "Process standard output and standard error are captured and streamed.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.ProcessCancellation,
            isolation,
            "Process cancellation and process-tree termination are supported.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.FilesystemReadRestrictions,
            isolation,
            platformDetail);
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.FilesystemWriteRestrictions,
            isolation,
            platformDetail);
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.SymlinkHandling,
            isolation,
            "The compiled host isolation plan controls symlink evaluation.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.NetworkBlock,
            isolation,
            "The host enforcement profile blocks or mediates network egress.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.NetworkAllowlist,
            isolation,
            "Filtered network policy is mediated by provider-owned proxies.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.NetworkFiltering,
            isolation,
            "Filtered network policy is mediated by provider-owned proxies.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.NetworkProxyMediation,
            isolation,
            "HTTP and platform-specific proxy mediation are applied to the process.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.UnixSocketRestriction,
            isolation,
            "The host enforcement profile restricts Unix-domain socket access.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.EnvironmentFiltering,
            isolation,
            "The provider strips and injects environment variables according to policy.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.StandardInput,
            isolation,
            "Standard input is supported by the host process handle.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.LocalPortBinding,
            isolation,
            "Local binding is controlled by the compiled network and interactive policy.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.ViolationObservation,
            OperatingSystem.IsMacOS() &&
            File.Exists("/usr/bin/log")
                ? CapabilityState.Supported
                : CapabilityState.Unsupported,
            OperatingSystem.IsMacOS() &&
            File.Exists("/usr/bin/log")
                ? "Seatbelt violation output is observed."
                : "The current Host Process platform does not expose violation events.");
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.ContentProjection,
            workspace,
            "Host Process supports host-path visibility and reports path-remapping limitations.");
        foreach (CapabilityId workspaceCapability in
                 WorkspaceCapabilities())
        {
            facts.Add(new CapabilityFact
            {
                Id = workspaceCapability,
                Category = new CapabilityCategory("workspace"),
                AppliesTo = ProviderContractKind.WorkspaceFileStore,
                State = workspace,
                Detail =
                    "Host Process implements root-scoped file access over the " +
                    "effective host workspace path.",
            });
        }
        AddSandboxFact(
            facts,
            StandardSandboxEnvironmentCapabilities.SessionCleanup,
            isolation,
            "Processes, projections, execution units, and host identity are released in order.");

        foreach (CapabilityId unsupported in UnsupportedSandboxCapabilities())
        {
            AddSandboxFact(
                facts,
                unsupported,
                CapabilityState.Unsupported,
                "The current Host Process provider does not implement this feature.");
        }

        return new ProviderCapabilityReport
        {
            ProviderId = providerId,
            ObservedAt = DateTimeOffset.UtcNow,
            HostPlatform =
                HostProcessProviderDescriptor.CurrentPlatform(),
            Capabilities = facts,
            Conditions = supported
                ? []
                :
                [
                    new Condition(
                        "HostProcessSupported",
                        ConditionStatus.False,
                        "UnsupportedPlatform",
                        platformDetail,
                        DateTimeOffset.UtcNow,
                        default,
                        DiagnosticSeverity.Error),
                ],
        };
    }

    private static CapabilityFact StandardFact(
        CapabilityId id,
        CapabilityState state,
        string detail) =>
        new()
        {
            Id = id,
            Category = new CapabilityCategory("isolation"),
            AppliesTo = ProviderContractKind.ProcessIsolation,
            State = state,
            Detail = detail,
        };

    private static void AddSandboxFact(
        ICollection<CapabilityFact> facts,
        CapabilityId id,
        CapabilityState state,
        string detail)
    {
        facts.Add(new CapabilityFact
        {
            Id = id,
            Category = new CapabilityCategory("sandbox"),
            AppliesTo =
                ProviderContractKind.ProcessInvocation |
                ProviderContractKind.ProcessIsolation,
            State = state,
            Detail = detail,
        });
    }

    private static IEnumerable<CapabilityId>
        UnsupportedSandboxCapabilities()
    {
        yield return
            StandardSandboxEnvironmentCapabilities.RetainedProcessObservation;
        yield return
            StandardSandboxEnvironmentCapabilities.BackgroundProcessOwnership;
        yield return
            StandardSandboxEnvironmentCapabilities.ProcessOutputReplay;
        yield return
            StandardSandboxEnvironmentCapabilities.ProcessIdentity;
        yield return StandardSandboxEnvironmentCapabilities.Pty;
        yield return StandardSandboxEnvironmentCapabilities.ResourceLimits;
    }

    private static IEnumerable<CapabilityId> WorkspaceCapabilities()
    {
        yield return StandardWorkspaceEnvironmentCapabilities.Read;
        yield return StandardWorkspaceEnvironmentCapabilities.Write;
        yield return StandardWorkspaceEnvironmentCapabilities.Delete;
        yield return StandardWorkspaceEnvironmentCapabilities.List;
        yield return StandardWorkspaceEnvironmentCapabilities.Search;
        yield return StandardWorkspaceEnvironmentCapabilities.Binary;
        yield return
            StandardWorkspaceEnvironmentCapabilities.ConditionalWrite;
        yield return
            StandardWorkspaceEnvironmentCapabilities.PathMapping;
        yield return
            StandardWorkspaceEnvironmentCapabilities.CaseSensitivity;
        yield return
            StandardWorkspaceEnvironmentCapabilities.SymlinkControl;
    }
}
