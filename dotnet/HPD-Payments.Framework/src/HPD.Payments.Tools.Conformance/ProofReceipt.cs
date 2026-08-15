using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Names the honest state of one exact conformance claim.</summary>
internal enum ProofState
{
    /// <summary>Source or artifact bytes were inspected; behavior was not executed.</summary>
    Inspected = 0,
    /// <summary>The exact source graph compiled.</summary>
    Compiled,
    /// <summary>The exact generator inputs produced reviewed outputs.</summary>
    Generated,
    /// <summary>The exact graph linked.</summary>
    Linked,
    /// <summary>The exact command and oracle executed successfully.</summary>
    Executed,
    /// <summary>The exact command or oracle failed.</summary>
    Failed,
    /// <summary>The exact claim has not executed.</summary>
    Untested,
}

/// <summary>Names append-only receipt lifecycle independently from executable proof state.</summary>
internal enum ReceiptLifecycle { Active = 0, Supersession, Invalidation }

/// <summary>Uniquely identifies one joined proof-matrix cell; no family grouping is permitted.</summary>
internal sealed record ProofCellKey(
    string CanonicalId, string Owner, string Family, string OwnCell, string ExternalCell, string Profile, string Lane,
    string Adapter, string Provider, string ProviderAccount, string ProviderEnvironment, string ProviderApiVersion,
    string Graph, string Rid, string OperatingSystem, string Architecture, string Sdk, string Runtime, string Compiler,
    string Linker, string NativeAot, string Path, string Workload)
{
    /// <summary>Returns a length-prefixed canonical representation of every key dimension.</summary>
    public string ToCanonicalText() => ProofCanonical.Join(CanonicalId, Owner, Family, OwnCell, ExternalCell, Profile, Lane,
        Adapter, Provider, ProviderAccount, ProviderEnvironment, ProviderApiVersion, Graph, Rid, OperatingSystem,
        Architecture, Sdk, Runtime, Compiler, Linker, NativeAot, Path, Workload);
}

/// <summary>Represents one append-only, content-addressed exact-cell conformance receipt.</summary>
internal sealed record ProofReceipt
{
    /// <summary>Gets the exact joined proof cell.</summary>
    public required ProofCellKey Cell { get; init; }
    /// <summary>Gets the schema version.</summary>
    public required string SchemaVersion { get; init; }
    /// <summary>Gets the unique receipt identity.</summary>
    public required string ReceiptId { get; init; }
    /// <summary>Gets the run identity.</summary>
    public required string RunId { get; init; }
    /// <summary>Gets the route identity.</summary>
    public required string RouteId { get; init; }
    /// <summary>Gets the source revision.</summary>
    public required string SourceRevision { get; init; }
    /// <summary>Gets the whole source-tree digest.</summary>
    public required string WholeTreeDigest { get; init; }
    /// <summary>Gets the exact dirty-state disclosure.</summary>
    public required string DirtyState { get; init; }
    /// <summary>Gets the exact adapter/source-tree digest.</summary>
    public required string AdapterTreeDigest { get; init; }
    /// <summary>Gets the frozen canonical registry digest.</summary>
    public required string CanonicalRegistryDigest { get; init; }
    /// <summary>Gets the frozen claim-matrix digest.</summary>
    public required string ClaimMatrixDigest { get; init; }
    /// <summary>Gets the predecessor receipt content address or GENESIS.</summary>
    public required string PredecessorDigest { get; init; }
    /// <summary>Gets a superseded receipt content address, or none.</summary>
    public string? SupersedesDigest { get; init; }
    /// <summary>Gets a receipt content address invalidated by this record, or none.</summary>
    public string? InvalidatesDigest { get; init; }
    /// <summary>Gets exact upstream receipt addresses whose validity this receipt depends upon.</summary>
    public required IReadOnlyList<string> DependencyDigests { get; init; }
    /// <summary>Gets the exact admitted command identity and arguments digest.</summary>
    public required string CommandBinding { get; init; }
    /// <summary>Gets the exact assertion inventory digest.</summary>
    public required string AssertionsDigest { get; init; }
    /// <summary>Gets the oracle identity and version.</summary>
    public required string OracleBinding { get; init; }
    /// <summary>Gets the code revision.</summary>
    public required string CodeRevision { get; init; }
    /// <summary>Gets the configuration revision.</summary>
    public required string ConfigurationRevision { get; init; }
    /// <summary>Gets the credential revision.</summary>
    public required string CredentialRevision { get; init; }
    /// <summary>Gets the protocol revision.</summary>
    public required string ProtocolRevision { get; init; }
    /// <summary>Gets the policy revision.</summary>
    public required string PolicyRevision { get; init; }
    /// <summary>Gets the corpus digest.</summary>
    public required string CorpusDigest { get; init; }
    /// <summary>Gets the 256-bit root seed.</summary>
    public required string RootSeed { get; init; }
    /// <summary>Gets the exact per-cell derived seed.</summary>
    public required string DerivedSeed { get; init; }
    /// <summary>Gets the exact virtual-time trace digest.</summary>
    public required string VirtualTimeTraceDigest { get; init; }
    /// <summary>Gets the H0-H13/H7 fault-schedule digest.</summary>
    public required string FaultScheduleDigest { get; init; }
    /// <summary>Gets the standard-output digest.</summary>
    public required string StandardOutputDigest { get; init; }
    /// <summary>Gets the standard-error digest.</summary>
    public required string StandardErrorDigest { get; init; }
    /// <summary>Gets the process exit status.</summary>
    public required int ExitStatus { get; init; }
    /// <summary>Gets the UTC start instant.</summary>
    public required DateTimeOffset StartedAtUtc { get; init; }
    /// <summary>Gets the UTC end instant.</summary>
    public required DateTimeOffset EndedAtUtc { get; init; }
    /// <summary>Gets the exact measured duration implied by the retained UTC envelope.</summary>
    public TimeSpan Duration => EndedAtUtc - StartedAtUtc;
    /// <summary>Gets allocation/resource observations and their measurement scope.</summary>
    public required string ResourceObservations { get; init; }
    /// <summary>Gets limitations and external-boundary disclosure.</summary>
    public required string Limitations { get; init; }
    /// <summary>Gets cleanup and provenance attestation.</summary>
    public required string CleanupAttestation { get; init; }
    /// <summary>Gets the independently retained provenance binding.</summary>
    public required string Provenance { get; init; }
    /// <summary>Gets the honest proof state.</summary>
    public required ProofState State { get; init; }
    /// <summary>Gets the append-only lifecycle role without inventing an executable proof state.</summary>
    public required ReceiptLifecycle Lifecycle { get; init; }

    /// <summary>Returns the canonical content used for addressing.</summary>
    public string ToCanonicalText() => ProofCanonical.Join(Cell.ToCanonicalText(), SchemaVersion, ReceiptId, RunId,
        RouteId, SourceRevision, WholeTreeDigest, DirtyState, AdapterTreeDigest, CanonicalRegistryDigest, ClaimMatrixDigest, PredecessorDigest,
        SupersedesDigest ?? "", InvalidatesDigest ?? "", ProofCanonical.Join(DependencyDigests.ToArray()),
        CommandBinding, AssertionsDigest, OracleBinding,
        CodeRevision, ConfigurationRevision, CredentialRevision, ProtocolRevision, PolicyRevision, CorpusDigest, RootSeed,
        DerivedSeed, VirtualTimeTraceDigest, FaultScheduleDigest, StandardOutputDigest, StandardErrorDigest,
        ExitStatus.ToString(System.Globalization.CultureInfo.InvariantCulture),
        StartedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        EndedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        Duration.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceObservations, Limitations,
        CleanupAttestation, Provenance, State.ToString(), Lifecycle.ToString());

    /// <summary>Returns the SHA-256 content address of the canonical receipt.</summary>
    public string ContentAddress() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalText())));
}

internal static partial class ProofCanonical
{
    internal static string Join(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            var bytes = Encoding.UTF8.GetByteCount(value);
            builder.Append(bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(value);
        }
        return builder.ToString();
    }
}
