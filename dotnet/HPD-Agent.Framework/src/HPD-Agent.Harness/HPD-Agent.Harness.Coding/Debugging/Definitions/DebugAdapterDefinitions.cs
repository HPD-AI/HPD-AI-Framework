using System.Collections.Frozen;
using System.Collections.Immutable;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

[Flags]
public enum DebugTargetKind
{
    None = 0,
    Executable = 1,
    SourceFile = 2,
    ProjectDirectory = 4,
    Module = 8,
    Process = 16,
    RegisteredRemoteEndpoint = 32
}

[Flags]
public enum DebugAdapterProgramKind
{
    None = 0,
    SourceFile = 1,
    ExecutableFile = 2,
    ProjectDirectory = 4
}

public sealed record DebugAdapterProvenance
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public required string AssemblyName { get; init; }
    public string? ClaimedSignatureIdentity { get; init; }
}

public enum DebugAdapterTrustLevel
{
    Denied,
    Untrusted,
    Trusted
}

public sealed record DebugAdapterTrustDecision
{
    public required DebugAdapterTrustLevel TrustLevel { get; init; }
    public string? VerifiedSignatureIdentity { get; init; }
    public required string PolicyRevision { get; init; }
    public required string ReasonCode { get; init; }
}

public sealed record DebugAdapterDescriptor
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Languages { get; init; }
    public required IReadOnlyList<string> FileExtensions { get; init; }
    public required IReadOnlyList<string> RootMarkers { get; init; }
    public required DebugTargetKind TargetKinds { get; init; }
    public DebugAdapterProgramKind ProgramKinds { get; init; }
    public IReadOnlyList<string> CommandHints { get; init; } = [];
    public IReadOnlyList<string> ArgumentHints { get; init; } = [];
    public string? InstallGuidanceId { get; init; }
    public int Priority { get; init; }
    public bool EnabledByDefault { get; init; } = true;
    public bool Experimental { get; init; }
    public required DebugAdapterProvenance Provenance { get; init; }
}

public delegate IDebugAdapterFactory DebugAdapterFactoryResolver(IServiceProvider services);

public sealed record DebugAdapterCatalogEntry
{
    public required DebugAdapterDescriptor Descriptor { get; init; }
    public required DebugAdapterFactoryResolver FactoryResolver { get; init; }
}

public interface IDebugAdapterCatalogProvider
{
    IEnumerable<DebugAdapterCatalogEntry> GetEntries();
}

public enum DebugAdapterCatalogFailureAction
{
    FailStartup,
    DisableExternalEntry
}

public interface IDebugAdapterCatalogFailurePolicy
{
    DebugAdapterCatalogFailureAction OnFactoryResolutionFailure(
        DebugAdapterDescriptor descriptor,
        Exception exception);
}

public sealed class FailStartupDebugAdapterCatalogFailurePolicy : IDebugAdapterCatalogFailurePolicy
{
    public DebugAdapterCatalogFailureAction OnFactoryResolutionFailure(
        DebugAdapterDescriptor descriptor,
        Exception exception) => DebugAdapterCatalogFailureAction.FailStartup;
}

public sealed record DebugAdapterCatalogDiagnostic(
    string AdapterId,
    string PackageId,
    string ReasonCode);

public sealed class DebugAdapterCatalog
{
    private readonly IReadOnlyDictionary<string, DebugAdapterCatalogEntry> _entries;
    private readonly IReadOnlyDictionary<string, IDebugAdapterFactory> _factories;
    private readonly IReadOnlyList<DebugAdapterCatalogEntry> _all;

    private readonly IReadOnlyList<DebugAdapterCatalogDiagnostic> _diagnostics;

    public DebugAdapterCatalog(
        IEnumerable<IDebugAdapterCatalogProvider> providers,
        IServiceProvider services,
        IDebugAdapterCatalogFailurePolicy? failurePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(services);
        var entries = new Dictionary<string, DebugAdapterCatalogEntry>(StringComparer.Ordinal);
        var factories = new Dictionary<string, IDebugAdapterFactory>(StringComparer.Ordinal);
        var diagnostics = new List<DebugAdapterCatalogDiagnostic>();
        failurePolicy ??= new FailStartupDebugAdapterCatalogFailurePolicy();
        var builtInAssemblyName = typeof(DebugAdapterCatalog).Assembly.GetName().Name;
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            foreach (var entry in provider.GetEntries())
            {
                ArgumentNullException.ThrowIfNull(entry);
                var frozenEntry = DebugAdapterCatalogSnapshot.Freeze(entry);
                if (!entries.TryAdd(frozenEntry.Descriptor.Id, frozenEntry))
                {
                    var existing = entries[frozenEntry.Descriptor.Id].Descriptor.Provenance;
                    throw new InvalidOperationException(
                        $"Debug adapter id '{frozenEntry.Descriptor.Id}' is provided by both '{existing.PackageId}' and '{frozenEntry.Descriptor.Provenance.PackageId}'.");
                }

                try
                {
                    factories.Add(frozenEntry.Descriptor.Id, frozenEntry.FactoryResolver(services)
                        ?? throw new InvalidOperationException("The factory resolver returned null."));
                }
                catch (Exception exception)
                {
                    var isBuiltIn = string.Equals(
                        frozenEntry.Descriptor.Provenance.AssemblyName,
                        builtInAssemblyName,
                        StringComparison.Ordinal);
                    if (!isBuiltIn && failurePolicy.OnFactoryResolutionFailure(frozenEntry.Descriptor, exception) == DebugAdapterCatalogFailureAction.DisableExternalEntry)
                    {
                        entries.Remove(frozenEntry.Descriptor.Id);
                        diagnostics.Add(new(
                            frozenEntry.Descriptor.Id,
                            frozenEntry.Descriptor.Provenance.PackageId,
                            "EXTERNAL_FACTORY_RESOLUTION_FAILED"));
                        continue;
                    }
                    throw new InvalidOperationException(
                        $"Debug adapter factory resolution failed for '{frozenEntry.Descriptor.Id}' from package '{frozenEntry.Descriptor.Provenance.PackageId}'.",
                        exception);
                }
            }
        }
        _entries = entries.ToFrozenDictionary(StringComparer.Ordinal);
        _factories = factories.ToFrozenDictionary(StringComparer.Ordinal);
        _all = entries.Values.ToImmutableArray();
        _diagnostics = diagnostics.ToImmutableArray();
    }

    public IReadOnlyList<DebugAdapterCatalogEntry> Entries => _all;
    public IReadOnlyList<DebugAdapterCatalogDiagnostic> Diagnostics => _diagnostics;

    public bool TryGet(string id, out DebugAdapterCatalogEntry entry)
        => _entries.TryGetValue(id, out entry!);

    public IDebugAdapterFactory GetFactory(string id)
        => _factories.TryGetValue(id, out var factory)
            ? factory
            : throw new KeyNotFoundException($"No debug adapter with id '{id}' is registered.");
}

internal static class DebugAdapterCatalogSnapshot
{
    public static DebugAdapterCatalogEntry Freeze(DebugAdapterCatalogEntry entry) => entry with
    {
        Descriptor = entry.Descriptor with
        {
            Languages = entry.Descriptor.Languages.ToImmutableArray(),
            FileExtensions = entry.Descriptor.FileExtensions.ToImmutableArray(),
            RootMarkers = entry.Descriptor.RootMarkers.ToImmutableArray(),
            CommandHints = entry.Descriptor.CommandHints.ToImmutableArray(),
            ArgumentHints = entry.Descriptor.ArgumentHints.ToImmutableArray()
        }
    };
}

public static class DebugAdapterFactoryResolution
{
    public static T GetRequired<T>(IServiceProvider services) where T : class
        => services.GetService(typeof(T)) as T
            ?? throw new InvalidOperationException($"Required debug adapter factory service '{typeof(T).FullName}' is not registered.");
}
