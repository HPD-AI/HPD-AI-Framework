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
        DebugAdapterLaunchPlan plan,
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
        DebugAdapterLaunchPlan plan,
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
        DebugAdapterLaunchPlan plan,
        CancellationToken cancellationToken)
    {
        var binding = plan.ProcessExecution
            ?? throw new InvalidOperationException("An Environment transport requires the captured runtime process binding.");
        if (plan.ExecutionTarget is not { } target || target != binding.ExecutionTarget)
            throw new InvalidOperationException("The launch plan execution target does not match its captured process binding.");
        if (!string.Equals(plan.EnvironmentId, binding.EnvironmentId, StringComparison.Ordinal) ||
            plan.EnvironmentRevision != binding.EnvironmentRevision)
            throw new InvalidOperationException("The launch plan Environment binding is stale or mismatched.");
        if (string.IsNullOrWhiteSpace(plan.Transport.Command))
            throw new InvalidOperationException("An Environment transport requires a direct executable.");
        if (!string.IsNullOrWhiteSpace(plan.ProcessProviderId) &&
            !string.Equals(plan.ProcessProviderId, binding.ProcessProvider.ProviderId.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("The launch plan process provider does not match its captured binding.");

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
            Isolation = new ProcessIsolationPolicy
            {
                Mode = ProcessIsolationMode.Isolated,
                Network = NetworkEgressPolicy.Blocked,
                Interactive = new ProcessInteractivePolicy { AllowStdin = true, AllowLocalBinding = localBinding },
                Environment = new EnvironmentAccessPolicy
                {
                    AllowedVariables = plan.FilteredEnvironment.Keys.ToArray(),
                    StripUnlistedVariables = true
                },
                Degradation = ProcessIsolationDegradationPolicy.FailClosed
            },
            PersistResource = false,
            ObservationRetention = ObservationRetentionPolicy.ResultAndDiagnostics
        };
        var handle = await binding.ProcessProvider.StartAsync(spec, output: null, cancellationToken).ConfigureAwait(false);
        return new DebugEnvironmentProcessTransport(handle, _limits);
    }

    private ValueTask<IDebugProtocolTransport> ConnectApprovedAsync(
        DebugAdapterLaunchPlan plan,
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
