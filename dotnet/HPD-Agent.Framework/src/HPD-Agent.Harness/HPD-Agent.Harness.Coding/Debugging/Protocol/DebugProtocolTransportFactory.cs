using HPD.Environment.Contracts;
using HPD.Agent.ToolHarness.Coding.Debugging;

namespace HPD.Agent.ToolHarness.Coding.Debugging.Protocol;

/// <summary>Host-owned connector. It receives only factory-produced, authorized endpoint plans.</summary>
public interface IDebugApprovedTransportConnector
{
    ValueTask<IDebugProtocolTransport> ConnectAsync(
        DebugAdapterTransportPlan authorizedPlan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to an adapter server started through HPD Environment. The returned transport must
    /// own and dispose <paramref name="startedProcess"/> together with the socket connection.
    /// </summary>
    ValueTask<IDebugProtocolTransport> ConnectEnvironmentServerAsync(
        DebugAdapterTransportPlan authorizedPlan,
        IDebugProtocolTransport startedProcess,
        CancellationToken cancellationToken = default);
}

public sealed class DebugProtocolTransportFactory
{
    private readonly IDebugApprovedTransportConnector? _connector;
    private readonly DebugProtocolTransportLimits _limits;

    public DebugProtocolTransportFactory(
        IDebugApprovedTransportConnector? connector = null,
        DebugProtocolTransportLimits? limits = null)
    {
        _connector = connector;
        _limits = limits ?? new();
        _limits.Validate();
    }

    public async ValueTask<IDebugProtocolTransport> CreateAsync(
        DebugAdapterStartPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Transport.Kind switch
        {
            DebugAdapterTransportKind.EnvironmentStdio
                => await StartEnvironmentProcessAsync(plan, cancellationToken).ConfigureAwait(false),
            DebugAdapterTransportKind.EnvironmentTcpServer
                => await StartEnvironmentServerAsync(plan, cancellationToken).ConfigureAwait(false),
            DebugAdapterTransportKind.ApprovedTcpConnect or DebugAdapterTransportKind.ApprovedUnixSocket or DebugAdapterTransportKind.HostCallback
                => await ConnectApprovedAsync(plan, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("The debug transport kind is unsupported.")
        };
    }

    private async ValueTask<IDebugProtocolTransport> StartEnvironmentServerAsync(
        DebugAdapterStartPlan plan,
        CancellationToken cancellationToken)
    {
        if (_connector is null)
            throw new InvalidOperationException("No approved debug endpoint connector is registered.");
        var process = await StartEnvironmentProcessAsync(plan, cancellationToken).ConfigureAwait(false);
        try
        {
            return await _connector.ConnectEnvironmentServerAsync(plan.Transport, process, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await process.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<IDebugProtocolTransport> StartEnvironmentProcessAsync(
        DebugAdapterStartPlan plan,
        CancellationToken cancellationToken)
    {
        var binding = plan.ProcessExecution
            ?? throw new InvalidOperationException("An Environment transport requires the captured runtime process binding.");
        if (plan.ExecutionTarget is not { } target || target != binding.ExecutionTarget)
            throw new InvalidOperationException("The adapter start plan execution target does not match its captured process binding.");
        if (!string.Equals(plan.EnvironmentId, binding.EnvironmentId, StringComparison.Ordinal) ||
            plan.EnvironmentRevision != binding.EnvironmentRevision)
            throw new InvalidOperationException("The adapter start plan Environment binding is stale or mismatched.");
        if (string.IsNullOrWhiteSpace(plan.Transport.Command))
            throw new InvalidOperationException("An Environment transport requires a direct executable.");
        if (!string.IsNullOrWhiteSpace(plan.ProcessProviderId) &&
            !string.Equals(plan.ProcessProviderId, binding.ProcessProvider.ProviderId.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("The adapter start plan process provider does not match its captured binding.");

        var localBinding = plan.Transport.Kind == DebugAdapterTransportKind.EnvironmentTcpServer;
        var spec = new ProcessInvocationSpec
        {
            Target = binding.ExecutionTarget,
            Role = ProcessRole.Sidecar,
            Command = new ProcessCommandSpec
            {
                FileName = plan.Transport.Command,
                Arguments = plan.Transport.Arguments.ToArray(),
                WorkingDirectory = plan.CanonicalWorkingDirectory,
                Environment = new Dictionary<string, string?>(plan.FilteredEnvironment, StringComparer.Ordinal)
            },
            Io = new ProcessIoSpec
            {
                StandardInput = new ProcessInputSpec { Kind = ProcessInputKind.Stream },
                StandardOutput = new ProcessOutputSpec { Capture = false, Stream = true },
                StandardError = new ProcessOutputSpec { Capture = false, Stream = true },
                MergeStandardError = false,
                LogPolicy = new ProcessLogPolicy { RetainOutputEvents = false }
            },
            Policy = new ProcessInvocationPolicy
            {
                AllowBackground = true,
                StopProcessTree = true,
                StopOnRunCancellation = false,
                OutputDrainTimeout = TimeSpan.FromSeconds(2)
            },
            Isolation = AdapterIsolation(plan, localBinding),
            PersistResource = false,
            ObservationRetention = ObservationRetentionPolicy.ResultAndDiagnostics
        };
        var handle = await binding.ProcessProvider.StartAsync(spec, output: null, cancellationToken).ConfigureAwait(false);
        return new DebugEnvironmentProcessTransport(handle, _limits);
    }

    private static ProcessIsolationPolicy AdapterIsolation(
        DebugAdapterStartPlan plan,
        bool localBinding)
    {
        if (plan.ProcessSandbox.Mode == AgentProcessIsolationMode.Disabled)
            return plan.ProcessSandbox.ToProcessIsolationPolicy(
                plan.CanonicalWorkingDirectory);

        // A launch adapter creates its debuggee inside the same sandbox and remains
        // fail-closed isolated. An attach adapter must inspect an already-running
        // process outside that sandbox. The Environment contract cannot currently
        // express an enforceable per-PID debug grant, so attach runs unsandboxed
        // only after the trusted planner, permission middleware, ownership checks,
        // adapter trust policy, and activation revalidation have all succeeded.
        if (plan.Method == DebugAdapterStartMethod.Attach)
            return ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Disabled
            };

        return new ProcessIsolationPolicy
        {
            Mode = ProcessIsolationMode.Isolated,
            Network = NetworkEgressPolicy.Blocked,
            Interactive = new ProcessInteractivePolicy
            {
                AllowStdin = true,
                AllowLocalBinding = localBinding
            },
            Environment = new EnvironmentAccessPolicy
            {
                AllowedVariables = plan.FilteredEnvironment.Keys.ToArray(),
                StripUnlistedVariables = true
            },
            Degradation = ProcessIsolationDegradationPolicy.FailClosed
        };
    }

    private ValueTask<IDebugProtocolTransport> ConnectApprovedAsync(
        DebugAdapterStartPlan plan,
        CancellationToken cancellationToken)
    {
        if (_connector is null)
            throw new InvalidOperationException("No approved debug endpoint connector is registered.");
        if (string.IsNullOrWhiteSpace(plan.Transport.EndpointId) ||
            string.IsNullOrWhiteSpace(plan.Transport.AuthorizedAddress) ||
            string.IsNullOrWhiteSpace(plan.Transport.AuthorityReference))
            throw new InvalidOperationException("A remote debug transport requires an authorized endpoint identity, address, and authority reference.");
        return _connector.ConnectAsync(plan.Transport, cancellationToken);
    }
}
