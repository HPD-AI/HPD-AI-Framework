namespace HPD.Environment.HostProcess;

using System.Runtime.InteropServices;
using HPD.Environment.Contracts;
using HPD.Environment.HostProcess.ProcessIsolation;
using HPD.Environment.Runtime;
using Microsoft.Extensions.Logging;

/// <summary>
/// Registers direct host process execution, operating-system enforcement,
/// content visibility, and capability reporting as one Environment backend.
/// </summary>
public sealed class HostProcessProviderModule : IProviderModule, IAsyncDisposable
{
    private readonly HostProcessProviderState _state = new();
    private readonly SandboxIsolationManager _isolationManager;
    private readonly HostProcessProcessProvider _processProvider;

    public HostProcessProviderModule(
        ILogger<HostProcessProviderModule>? logger = null)
    {
        _isolationManager = new SandboxIsolationManager(logger);
        _processProvider = new HostProcessProcessProvider(
            _state,
            new SandboxIsolationPlanner(),
            new HostSandboxApplicator(_isolationManager));
    }

    public ProviderDescriptor Descriptor { get; } =
        HostProcessProviderDescriptor.Create();

    public IProcessProvider ProcessProvider => _processProvider;

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

    public ValueTask DisposeAsync() =>
        _isolationManager.DisposeAsync();
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
