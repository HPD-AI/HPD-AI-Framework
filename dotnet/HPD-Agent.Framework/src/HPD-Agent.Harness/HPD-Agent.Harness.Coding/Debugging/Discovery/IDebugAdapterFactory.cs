using System.Text.Json;
using System.Collections.Immutable;
using HPD.Agent.Middleware;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public enum DebugAdapterAvailabilityKind { Available, Unavailable }

public sealed record DebugAdapterAvailability(
    DebugAdapterAvailabilityKind Kind,
    string? Version = null,
    string? SafeReasonCode = null,
    string? InstallGuidanceId = null);

public sealed record DebugAdapterResolutionContext
{
    public required string WorkspaceRoot { get; init; }
    public required string EnvironmentId { get; init; }
    public required long EnvironmentRevision { get; init; }
    public required string TargetPlatform { get; init; }
    public required long PolicyRevision { get; init; }
    public long EndpointCatalogRevision { get; init; }
    public string AuthorizationScope { get; init; } = "debug.adapter.launch";
    public IReadOnlyDictionary<string, string?> FilteredEnvironment { get; init; }
        = ImmutableDictionary<string, string?>.Empty;
    public IReadOnlyList<string>? ProbeArgumentsOverride { get; init; }
    public RuntimeProcessExecutionBinding? ProcessExecution { get; init; }
    public required DebugAdapterTrustDecision TrustDecision { get; init; }
}

public enum DebugAdapterTransportKind
{
    EnvironmentStdio,
    EnvironmentTcpServer,
    ApprovedTcpConnect,
    ApprovedUnixSocket,
    HostCallback
}

public enum DebugDynamicEndpointMode
{
    None,
    AdapterReportsSelectedPort,
    AppendSelectedPortAndLoopbackHost
}

public sealed record DebugAdapterTransportPlan
{
    public required DebugAdapterTransportKind Kind { get; init; }
    public required string Command { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? EndpointId { get; init; }
    public string? AuthorizedAddress { get; init; }
    public string? AuthorityReference { get; init; }
    public bool AllocatesDynamicLoopbackEndpoint { get; init; }
    public DebugDynamicEndpointMode DynamicEndpointMode { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

public sealed record DebugAdapterToolProvenance
{
    public required DebugAdapterToolSearchScope SearchScope { get; init; }
    public required string ProcessProviderId { get; init; }
    public required string LocationIdentity { get; init; }
    public string? PackageId { get; init; }
    public string? PackageVersion { get; init; }
    public string? ContentDigest { get; init; }
}

public sealed record DebugLaunchContext
{
    public required DebugAdapterResolutionContext Resolution { get; init; }
    public required string Target { get; init; }
    public required JsonElement Configuration { get; init; }
}

public sealed record DebugAttachContext
{
    public required DebugAdapterResolutionContext Resolution { get; init; }
    public string? ProcessId { get; init; }
    public string? EndpointId { get; init; }
    public required JsonElement Configuration { get; init; }
}

public sealed record DebugAdapterLaunchPlan
{
    public required string AdapterId { get; init; }
    public required string EnvironmentId { get; init; }
    public required long EnvironmentRevision { get; init; }
    public required long PolicyRevision { get; init; }
    public required long EndpointCatalogRevision { get; init; }
    public required DebugAdapterProvenance PackageProvenance { get; init; }
    public required DebugAdapterTrustDecision TrustDecision { get; init; }
    public RuntimeProcessExecutionBinding? ProcessExecution { get; init; }
    public TargetHandle<ExecutionUnit>? ExecutionTarget { get; init; }
    public required string CanonicalWorkingDirectory { get; init; }
    public required string AuthorizationScope { get; init; }
    public required IReadOnlyDictionary<string, string?> FilteredEnvironment { get; init; }
    public required DebugAdapterTransportPlan Transport { get; init; }
    public string TransportKind => Transport.Kind switch
    {
        DebugAdapterTransportKind.EnvironmentStdio => "stdio",
        DebugAdapterTransportKind.EnvironmentTcpServer => "environment-tcp-server",
        DebugAdapterTransportKind.ApprovedTcpConnect => "approved-tcp-connect",
        DebugAdapterTransportKind.ApprovedUnixSocket => "approved-unix-socket",
        DebugAdapterTransportKind.HostCallback => "host-callback",
        _ => throw new InvalidOperationException("Unknown debug adapter transport kind.")
    };
    public string? ResolvedCommand { get; init; }
    public IReadOnlyList<string> CommandArguments { get; init; } = [];
    public DebugAdapterToolSearchScope? ToolSearchScope { get; init; }
    public string? ProcessProviderId { get; init; }
    public DebugAdapterToolProvenance? ToolProvenance { get; init; }
    public required JsonElement Arguments { get; init; }
}

public interface IDebugAdapterFactory
{
    ValueTask<DebugAdapterAvailability> ProbeAsync(
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext context,
        CancellationToken cancellationToken = default);

    ValueTask<DebugAdapterLaunchPlan> CreateLaunchPlanAsync(
        DebugAdapterDescriptor descriptor,
        DebugLaunchContext context,
        CancellationToken cancellationToken = default);

    ValueTask<DebugAdapterLaunchPlan> CreateAttachPlanAsync(
        DebugAdapterDescriptor descriptor,
        DebugAttachContext context,
        CancellationToken cancellationToken = default);
}

public sealed class StandardDebugAdapterFactory : IDebugAdapterFactory
{
    private readonly IDebugAdapterToolResolver _toolResolver;
    private readonly IDebugWorkspaceCanonicalizer _workspaceCanonicalizer;
    private readonly IDebugEndpointResolver _endpointResolver;

    public StandardDebugAdapterFactory(
        IDebugAdapterToolResolver toolResolver,
        IDebugWorkspaceCanonicalizer? workspaceCanonicalizer = null,
        IDebugEndpointResolver? endpointResolver = null)
    {
        _toolResolver = toolResolver;
        _workspaceCanonicalizer = workspaceCanonicalizer ?? new LexicalDebugWorkspaceCanonicalizer();
        _endpointResolver = endpointResolver ?? new DenyAllDebugEndpointResolver();
    }

    public async ValueTask<DebugAdapterAvailability> ProbeAsync(DebugAdapterDescriptor descriptor, DebugAdapterResolutionContext context, CancellationToken cancellationToken = default)
    {
        var resolution = await _toolResolver.ResolveAsync(descriptor, context, cancellationToken).ConfigureAwait(false);
        return new DebugAdapterAvailability(
            resolution.Available ? DebugAdapterAvailabilityKind.Available : DebugAdapterAvailabilityKind.Unavailable,
            resolution.Version,
            resolution.SafeReasonCode,
            resolution.Available ? null : descriptor.InstallGuidanceId);
    }

    public ValueTask<DebugAdapterLaunchPlan> CreateLaunchPlanAsync(DebugAdapterDescriptor descriptor, DebugLaunchContext context, CancellationToken cancellationToken = default)
        => CreatePlanAsync(descriptor, context.Resolution, context.Configuration, cancellationToken);

    public ValueTask<DebugAdapterLaunchPlan> CreateAttachPlanAsync(DebugAdapterDescriptor descriptor, DebugAttachContext context, CancellationToken cancellationToken = default)
        => context.EndpointId is null
            ? CreatePlanAsync(descriptor, context.Resolution, context.Configuration, cancellationToken)
            : CreateEndpointPlanAsync(descriptor, context, cancellationToken);

    private async ValueTask<DebugAdapterLaunchPlan> CreateEndpointPlanAsync(
        DebugAdapterDescriptor descriptor,
        DebugAttachContext context,
        CancellationToken cancellationToken)
    {
        if (context.Resolution.TrustDecision.TrustLevel != DebugAdapterTrustLevel.Trusted)
            throw new InvalidOperationException("An endpoint attach plan requires a trusted adapter package.");
        var endpoint = await _endpointResolver.ResolveAsync(context.EndpointId!, context.Resolution, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The requested debug endpoint is unavailable or unauthorized.");
        if (!string.Equals(endpoint.EnvironmentId, context.Resolution.EnvironmentId, StringComparison.Ordinal) ||
            endpoint.EndpointCatalogRevision != context.Resolution.EndpointCatalogRevision ||
            endpoint.PolicyRevision != context.Resolution.PolicyRevision)
            throw new InvalidOperationException("The authorized debug endpoint binding is stale or belongs to another Environment.");
        return new()
        {
            AdapterId = descriptor.Id,
            EnvironmentId = context.Resolution.EnvironmentId,
            EnvironmentRevision = context.Resolution.EnvironmentRevision,
            PolicyRevision = context.Resolution.PolicyRevision,
            EndpointCatalogRevision = context.Resolution.EndpointCatalogRevision,
            PackageProvenance = descriptor.Provenance,
            TrustDecision = context.Resolution.TrustDecision,
            CanonicalWorkingDirectory = _workspaceCanonicalizer.Canonicalize(context.Resolution.WorkspaceRoot, context.Resolution.TargetPlatform),
            AuthorizationScope = context.Resolution.AuthorizationScope,
            FilteredEnvironment = FreezeEnvironment(context.Resolution),
            Transport = new()
            {
                Kind = endpoint.TransportKind,
                Command = string.Empty,
                EndpointId = endpoint.EndpointId,
                AuthorizedAddress = endpoint.AuthorizedAddress,
                AuthorityReference = endpoint.AuthorityReference
            },
            Arguments = context.Configuration.Clone()
        };
    }

    private async ValueTask<DebugAdapterLaunchPlan> CreatePlanAsync(
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext context,
        JsonElement configuration,
        CancellationToken cancellationToken)
    {
        if (context.TrustDecision.TrustLevel != DebugAdapterTrustLevel.Trusted)
            throw new InvalidOperationException("A debug adapter launch plan requires a trusted adapter package.");
        var resolution = await _toolResolver.ResolveAsync(descriptor, context, cancellationToken).ConfigureAwait(false);
        if (!resolution.Available || string.IsNullOrWhiteSpace(resolution.Command) || resolution.SearchScope is null || string.IsNullOrWhiteSpace(resolution.ProcessProviderId))
            throw new InvalidOperationException($"Debug adapter '{descriptor.Id}' is unavailable ({resolution.SafeReasonCode ?? "UNKNOWN"}).");
        var processExecution = context.ProcessExecution
            ?? throw new InvalidOperationException("A process-backed adapter requires the captured runtime process binding.");
        var launchArguments = (resolution.LaunchArguments ?? descriptor.ArgumentHints).ToImmutableArray();
        return new()
        {
            AdapterId = descriptor.Id,
            EnvironmentId = context.EnvironmentId,
            EnvironmentRevision = context.EnvironmentRevision,
            PolicyRevision = context.PolicyRevision,
            EndpointCatalogRevision = context.EndpointCatalogRevision,
            PackageProvenance = descriptor.Provenance,
            TrustDecision = context.TrustDecision,
            ProcessExecution = processExecution,
            ExecutionTarget = processExecution.ExecutionTarget,
            CanonicalWorkingDirectory = _workspaceCanonicalizer.Canonicalize(context.WorkspaceRoot, context.TargetPlatform),
            AuthorizationScope = context.AuthorizationScope,
            FilteredEnvironment = FreezeEnvironment(context),
            Transport = new()
            {
                Kind = DebugAdapterTransportKind.EnvironmentStdio,
                Command = resolution.Command,
                Arguments = launchArguments
            },
            ResolvedCommand = resolution.Command,
            CommandArguments = launchArguments,
            ToolSearchScope = resolution.SearchScope.Value,
            ProcessProviderId = resolution.ProcessProviderId,
            ToolProvenance = new()
            {
                SearchScope = resolution.SearchScope.Value,
                ProcessProviderId = resolution.ProcessProviderId,
                LocationIdentity = resolution.LocationIdentity ?? resolution.Command,
                PackageId = resolution.PackageId,
                PackageVersion = resolution.PackageVersion,
                ContentDigest = resolution.ContentDigest
            },
            Arguments = configuration.Clone()
        };
    }

    private static IReadOnlyDictionary<string, string?> FreezeEnvironment(DebugAdapterResolutionContext context)
        => context.FilteredEnvironment.ToImmutableDictionary(
            context.TargetPlatform.StartsWith("win", StringComparison.OrdinalIgnoreCase)
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
}
