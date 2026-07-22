using System.Text;
using System.Collections.Frozen;
using System.Collections.Immutable;
using HPD.Environment.Contracts;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public enum DebugAdapterToolSearchScope
{
    WorkspaceLocal,
    PackageManaged,
    ManagedAssembly,
    GlobalCommand
}

public sealed record DebugAdapterToolResolution(
    bool Available,
    string? Command = null,
    string? Version = null,
    string? SafeReasonCode = null,
    DebugAdapterToolSearchScope? SearchScope = null,
    string? ProcessProviderId = null,
    string? LocationIdentity = null,
    string? PackageId = null,
    string? PackageVersion = null,
    string? ContentDigest = null,
    IReadOnlyList<string>? LaunchArguments = null);

public sealed record DebugAdapterToolCandidate
{
    public required string Command { get; init; }
    public required IReadOnlyList<string> ProbeArguments { get; init; }
    public required DebugAdapterToolSearchScope SearchScope { get; init; }
    public required string LocationIdentity { get; init; }
    public string? PackageId { get; init; }
    public string? PackageVersion { get; init; }
    public string? ContentDigest { get; init; }
    public IReadOnlyList<string> LaunchArguments { get; init; } = [];
}

public interface IDebugAdapterToolSearchPolicy
{
    IReadOnlyList<DebugAdapterToolCandidate> GetApprovedCandidates(
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext context);
}

public sealed class CatalogCommandDebugAdapterToolSearchPolicy : IDebugAdapterToolSearchPolicy
{
    public IReadOnlyList<DebugAdapterToolCandidate> GetApprovedCandidates(
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext context) => descriptor.CommandHints
        .Where(command => !string.IsNullOrWhiteSpace(command))
        .Select(command => new DebugAdapterToolCandidate
        {
            Command = command,
            ProbeArguments = context.ProbeArgumentsOverride ?? ProbeArguments(descriptor),
            SearchScope = DebugAdapterToolSearchScope.GlobalCommand,
            LocationIdentity = command,
            PackageId = descriptor.Provenance.PackageId,
            PackageVersion = descriptor.Provenance.PackageVersion,
            LaunchArguments = descriptor.ArgumentHints.ToImmutableArray()
        })
        .ToImmutableArray();

    private static IReadOnlyList<string> ProbeArguments(DebugAdapterDescriptor descriptor)
        => string.Equals(descriptor.Id, "debugpy", StringComparison.Ordinal)
            ? ["-c", "import debugpy; import debugpy.adapter; print(debugpy.__version__)"]
            : ["--version"];
}

public sealed record ConfiguredDebugAdapterToolLocation
{
    public required string AdapterId { get; init; }
    public required string Command { get; init; }
    public required IReadOnlyList<string> ProbeArguments { get; init; }
    public required DebugAdapterToolSearchScope SearchScope { get; init; }
    public required string LocationIdentity { get; init; }
    public string? PackageId { get; init; }
    public string? PackageVersion { get; init; }
    public string? ContentDigest { get; init; }
    public IReadOnlyList<string> LaunchArguments { get; init; } = [];
}

/// <summary>
/// Materializes only locations explicitly approved by the host. It performs no host-filesystem or
/// process-global PATH discovery; the selected HPD Environment remains authoritative during probing.
/// </summary>
public sealed class ConfiguredDebugAdapterToolSearchPolicy : IDebugAdapterToolSearchPolicy
{
    private readonly IReadOnlyDictionary<string, ImmutableArray<DebugAdapterToolCandidate>> _candidates;

    public ConfiguredDebugAdapterToolSearchPolicy(IEnumerable<ConfiguredDebugAdapterToolLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        _candidates = locations
            .Select(Validate)
            .GroupBy(location => location.AdapterId, StringComparer.Ordinal)
            .ToFrozenDictionary(
                group => group.Key,
                group => group.Select(location => new DebugAdapterToolCandidate
                {
                    Command = location.Command,
                    ProbeArguments = location.ProbeArguments.ToImmutableArray(),
                    SearchScope = location.SearchScope,
                    LocationIdentity = location.LocationIdentity,
                    PackageId = location.PackageId,
                    PackageVersion = location.PackageVersion,
                    ContentDigest = location.ContentDigest,
                    LaunchArguments = location.LaunchArguments.ToImmutableArray()
                }).ToImmutableArray(),
                StringComparer.Ordinal);
    }

    public IReadOnlyList<DebugAdapterToolCandidate> GetApprovedCandidates(
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext context)
        => _candidates.TryGetValue(descriptor.Id, out var candidates) ? candidates : [];

    private static ConfiguredDebugAdapterToolLocation Validate(ConfiguredDebugAdapterToolLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (string.IsNullOrWhiteSpace(location.AdapterId) || string.IsNullOrWhiteSpace(location.Command) ||
            string.IsNullOrWhiteSpace(location.LocationIdentity) || location.Command.Contains('\0') ||
            location.LocationIdentity.Contains('\0') || location.ProbeArguments.Any(argument => argument.Contains('\0')))
            throw new ArgumentException("Configured debug adapter locations must contain bounded nonblank identities without NUL characters.", nameof(location));
        if (location.LocationIdentity.Length > 1024 || location.Command.Length > 1024 || location.ProbeArguments.Count > 32)
            throw new ArgumentException("Configured debug adapter location exceeds the supported bounds.", nameof(location));
        if (location.LaunchArguments.Count > 64 || location.LaunchArguments.Any(argument => argument.Contains('\0')))
            throw new ArgumentException("Configured debug adapter launch arguments exceed the supported bounds.", nameof(location));
        return location with
        {
            ProbeArguments = location.ProbeArguments.ToArray(),
            LaunchArguments = location.LaunchArguments.ToArray()
        };
    }
}

public interface IDebugAdapterToolResolver
{
    ValueTask<DebugAdapterToolResolution> ResolveAsync(
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class EnvironmentDebugAdapterToolResolver : IDebugAdapterToolResolver
{
    private const int MaxVersionBytes = 4096;
    private readonly IDebugAdapterToolSearchPolicy _searchPolicy;

    public EnvironmentDebugAdapterToolResolver(IDebugAdapterToolSearchPolicy? searchPolicy = null)
        => _searchPolicy = searchPolicy ?? new CatalogCommandDebugAdapterToolSearchPolicy();

    public async ValueTask<DebugAdapterToolResolution> ResolveAsync(
        DebugAdapterDescriptor descriptor,
        DebugAdapterResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(context);
        if (context.TrustDecision.TrustLevel != DebugAdapterTrustLevel.Trusted)
            return new(false, SafeReasonCode: "ADAPTER_PACKAGE_NOT_TRUSTED");
        if (context.ProcessExecution is null)
            return new(false, SafeReasonCode: "PROCESS_EXECUTION_BINDING_UNAVAILABLE");

        foreach (var candidate in _searchPolicy.GetApprovedCandidates(descriptor, context))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(candidate.Command))
                continue;
            try
            {
                var result = await context.ProcessExecution.ProcessProvider.RunAsync(
                    CreateProbeSpec(context.ProcessExecution.ExecutionTarget, candidate, context.WorkspaceRoot),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.Violations.Count != 0)
                    return new(false, SafeReasonCode: "ADAPTER_PROBE_DENIED_BY_ENVIRONMENT_POLICY");
                if (result.ExitCode != 0 || result.CompletionKind is ProcessCompletionKind.FailedToStart or ProcessCompletionKind.TimedOut or ProcessCompletionKind.Faulted)
                    continue;
                return new(
                    true,
                    candidate.Command,
                    ReadSafeVersion(result.Output),
                    SearchScope: candidate.SearchScope,
                    ProcessProviderId: context.ProcessExecution.ProcessProvider.ProviderId.Value,
                    LocationIdentity: candidate.LocationIdentity,
                    PackageId: candidate.PackageId,
                    PackageVersion: candidate.PackageVersion,
                    ContentDigest: candidate.ContentDigest,
                    LaunchArguments: candidate.LaunchArguments);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Missing and broken candidates are ordinary negative availability.
            }
        }
        return new(false, SafeReasonCode: "ADAPTER_COMMAND_NOT_AVAILABLE");
    }

    private static ProcessInvocationSpec CreateProbeSpec(
        TargetHandle<ExecutionUnit> target,
        DebugAdapterToolCandidate candidate,
        string workspaceRoot) => new()
    {
        Target = target,
        Command = new ProcessCommandSpec
        {
            FileName = candidate.Command,
            Arguments = candidate.ProbeArguments,
            WorkingDirectory = workspaceRoot
        },
        Io = new ProcessIoSpec
        {
            StandardOutput = new ProcessOutputSpec { Capture = true, Stream = false, MaxCapturedBytes = MaxVersionBytes },
            StandardError = new ProcessOutputSpec { Capture = true, Stream = false, MaxCapturedBytes = MaxVersionBytes }
        },
        Policy = ProcessInvocationPolicy.Default with { Timeout = TimeSpan.FromSeconds(3), OutputDrainTimeout = TimeSpan.FromSeconds(1) },
        Isolation = ProcessIsolationPolicy.Default with
        {
            Network = NetworkEgressPolicy.Blocked,
            Interactive = ProcessInteractivePolicy.Default with { AllowStdin = false }
        },
        ObservationRetention = ObservationRetentionPolicy.ResultAndDiagnostics
    };

    private static string? ReadSafeVersion(ProcessCapturedOutput output)
    {
        var bytes = output.Stdout.CapturedBytes.IsEmpty ? output.Stderr.CapturedBytes : output.Stdout.CapturedBytes;
        if (bytes.IsEmpty)
            return null;
        var firstLine = Encoding.UTF8.GetString(bytes.Span).Trim()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstLine is null ? null : firstLine[..Math.Min(firstLine.Length, 256)];
    }
}
