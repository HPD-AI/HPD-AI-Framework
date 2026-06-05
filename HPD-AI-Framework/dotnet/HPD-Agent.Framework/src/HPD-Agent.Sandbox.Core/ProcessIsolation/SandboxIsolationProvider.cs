namespace HPD.Agent.Sandbox.ProcessIsolation;

using System.Runtime.InteropServices;
using System.Text;
using HPD.Execution.Contracts;
using HPD.Execution.Runtime;

public sealed class SandboxIsolationProvider : IProcessIsolationProvider
{
    public static ProviderId SandboxProviderId { get; } = new("hpd.agent.sandbox");
    private readonly SandboxIsolationManager? _isolationManager;

    public ProviderId ProviderId => SandboxProviderId;

    internal SandboxIsolationPlan? LastPreparedPlan { get; private set; }

    public SandboxIsolationProvider()
    {
    }

    public SandboxIsolationProvider(SandboxIsolationManager isolationManager)
    {
        _isolationManager = isolationManager ?? throw new ArgumentNullException(nameof(isolationManager));
    }

    public ValueTask<ProcessIsolationPlan> PlanIsolationAsync(
        ProcessInvocationSpec invocation,
        ProcessIsolationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SandboxIsolationPlan plan = SandboxIsolationCompiler.Compile(policy);

        return ValueTask.FromResult(new ProcessIsolationPlan
        {
            Diagnostics =
            [
                $"sandbox isolation prepared with filesystem-rules={plan.Filesystem.Rules.Count}, network={plan.Network.Mode}, sockets={plan.UnixSockets.AllowedSockets.Count}",
            ],
        });
    }

    public async ValueTask<IsolatedProcessCommand> PrepareAsync(
        ProcessInvocationSpec invocation,
        ProcessIsolationPolicy policy,
        ProcessIsolationPlan? plan = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SandboxIsolationPlan localPlan = SandboxIsolationCompiler.Compile(policy);
        LastPreparedPlan = localPlan;

        ProviderExtensionData marker = new(
            ProviderId,
            new SchemaId("hpd.agent.sandbox.plan"),
            new ContentType("text/plain"),
            Encoding.UTF8.GetBytes("sandbox-isolation-plan-prepared"));

        ProcessInvocationSpec wrappedInvocation = invocation;
        if (_isolationManager is not null)
        {
            var command = CommandInvocation.From(
                invocation.Command.FileName,
                invocation.Command.Arguments);
            PreparedSandboxCommand wrapped = await _isolationManager.WrapCommandAsync(
                command,
                localPlan,
                cancellationToken);

            wrappedInvocation = invocation with
            {
                Command = new ProcessCommandSpec
                {
                    FileName = wrapped.FileName,
                    Arguments = wrapped.ArgumentList,
                    WorkingDirectory = invocation.Command.WorkingDirectory,
                    Environment = MergeEnvironment(invocation.Command.Environment, wrapped.Environment),
                },
            };
        }

        ProcessInvocationSpec prepared = invocation with
        {
            Command = wrappedInvocation.Command,
            ProviderExtensions = invocation.ProviderExtensions.Concat([marker]).ToArray(),
        };

        return new IsolatedProcessCommand
        {
            Invocation = prepared,
            Plan = plan ?? ProcessIsolationPlan.Empty,
            ProviderExtensions = [marker],
        };
    }

    private static IReadOnlyDictionary<string, string?> MergeEnvironment(
        IReadOnlyDictionary<string, string?> invocation,
        IReadOnlyDictionary<string, string>? wrapped)
    {
        if (wrapped is null || wrapped.Count == 0)
            return invocation;

        var merged = new Dictionary<string, string?>(invocation, StringComparer.Ordinal);
        foreach (var (key, value) in wrapped)
            merged[key] = value;

        return merged;
    }
}

public sealed class SandboxIsolationProviderModule : IProviderModule
{
    private readonly SandboxIsolationProvider _provider;

    public SandboxIsolationProviderModule()
        : this(new SandboxIsolationProvider())
    {
    }

    public SandboxIsolationProviderModule(SandboxIsolationProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public ProviderDescriptor Descriptor { get; } = new()
    {
        Id = SandboxIsolationProvider.SandboxProviderId,
        DisplayName = "HPD Sandbox Isolation Provider",
        ContractVersion = new SemanticVersion(1, 0, 0),
        ProviderVersion = new SemanticVersion(1, 0, 0),
        ContractKinds = ProviderContractKind.ProcessIsolation,
        TrustLevel = ProviderTrustLevel.BuiltIn,
        DefaultActivationScope = ProviderActivationScope.Runtime,
        ActivationModels =
        [
            new ProviderActivationModel(ProviderActivationKind.InProcess, ProviderActivationScope.Runtime, ProviderTransportKind.None),
        ],
        HostPlatforms = [CurrentPlatform()],
        HostDependencies =
        [
            new HostDependencyRequirement(new HostDependencyRef(HostDependencyKind.ProviderDefined, "sandbox-backend"), Required: true, Detail: "Linux bwrap/seccomp, macOS sandbox-exec, or another OS-family sandbox backend."),
        ],
    };

    public void Register(IProviderRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddProcessIsolationProvider(_provider);
    }

    public void RegisterJsonTypes(IProviderJsonTypeRegistry registry)
    {
    }

    private static PlatformSpec CurrentPlatform() =>
        new(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "unknown",
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
}

public static class SandboxIsolationRegistrationExtensions
{
    public static ExecutionProviderRegistry RegisterSandboxIsolation(
        this ExecutionProviderRegistry registry,
        SandboxIsolationManager? isolationManager = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var provider = isolationManager is null
            ? new SandboxIsolationProvider()
            : new SandboxIsolationProvider(isolationManager);
        registry.RegisterModule(new SandboxIsolationProviderModule(provider));
        return registry;
    }
}
