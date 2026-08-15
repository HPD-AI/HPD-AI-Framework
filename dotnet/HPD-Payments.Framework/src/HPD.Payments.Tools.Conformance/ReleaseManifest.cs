using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Separates a non-gating candidate from a published claim or explicit withdrawal.</summary>
internal enum ReleaseManifestLifecycle
{
    Candidate = 0,
    Published,
    Withdrawal,
}

/// <summary>Persists one complete explicit release-selection inventory without implying readiness.</summary>
internal sealed record ReleaseManifest
{
    internal required string SchemaVersion { get; init; }
    internal required string CanonicalRegistryDigest { get; init; }
    internal required string ClaimMatrixDigest { get; init; }
    internal required string SourceRevision { get; init; }
    internal required DateTimeOffset CreatedAtUtc { get; init; }
    internal required string PredecessorManifestDigest { get; init; }
    internal string? SupersedesManifestDigest { get; init; }
    internal required ReleaseManifestLifecycle Lifecycle { get; init; }
    internal required IReadOnlyList<RouteDisposition> Dispositions { get; init; }

    internal string ToCanonicalText() => ProofCanonical.Join(SchemaVersion, CanonicalRegistryDigest, ClaimMatrixDigest,
        SourceRevision, CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        PredecessorManifestDigest, SupersedesManifestDigest ?? string.Empty, Lifecycle.ToString(),
        ProofCanonical.Join(Dispositions.Select(ToCanonicalDisposition).ToArray()));

    internal string ContentAddress() => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalText())));

    internal ReleaseSelectionResult ValidateAgainst(RegistrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (SchemaVersion != "hpd.payments.release-manifest.v1")
            throw new InvalidDataException("Release manifest schema is unsupported.");
        if (CanonicalRegistryDigest != snapshot.CanonicalDigest || ClaimMatrixDigest != snapshot.ClaimMatrixDigest)
            throw new InvalidDataException("Release manifest registry binding is stale or mismatched.");
        if (SourceRevision.Length == 0 || CreatedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidDataException("Release manifest provenance is incomplete.");
        if (!IsAddressOrGenesis(PredecessorManifestDigest) ||
            (SupersedesManifestDigest is not null && !IsAddress(SupersedesManifestDigest)))
            throw new InvalidDataException("Release manifest lineage is malformed.");
        if (!Enum.IsDefined(Lifecycle)) throw new InvalidDataException("Release manifest lifecycle is invalid.");
        var selection = ReleaseSelectionValidator.Validate(snapshot, Dispositions);
        if (Lifecycle == ReleaseManifestLifecycle.Published && !selection.ReleaseComplete)
            throw new InvalidDataException("An incomplete release manifest cannot be published.");
        if (Lifecycle == ReleaseManifestLifecycle.Withdrawal &&
            (SupersedesManifestDigest is null || Dispositions.Any(static x => x.Kind == RouteDispositionKind.Selected)))
            throw new InvalidDataException("A withdrawal must name its target and cannot retain selected claims.");
        return selection;
    }

    internal static ReleaseManifest Parse(string canonical)
    {
        var fields = ProofCanonical.Split(canonical, 9);
        if (!DateTimeOffset.TryParseExact(fields[4], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) ||
            created.Offset != TimeSpan.Zero) throw new InvalidDataException("Release manifest creation time is invalid.");
        if (!Enum.TryParse<ReleaseManifestLifecycle>(fields[7], ignoreCase: false, out var lifecycle) || !Enum.IsDefined(lifecycle))
            throw new InvalidDataException("Release manifest lifecycle is invalid.");
        var encoded = fields[8].Length == 0 ? [] : ProofCanonical.Split(fields[8], 4096, requireExact: false);
        var dispositions = encoded.Select(ParseDisposition).ToArray();
        return new() { SchemaVersion = fields[0], CanonicalRegistryDigest = fields[1], ClaimMatrixDigest = fields[2],
            SourceRevision = fields[3], CreatedAtUtc = created, PredecessorManifestDigest = fields[5],
            SupersedesManifestDigest = fields[6].Length == 0 ? null : fields[6], Lifecycle = lifecycle,
            Dispositions = dispositions };
    }

    private static string ToCanonicalDisposition(RouteDisposition disposition) => ProofCanonical.Join(
        disposition.CanonicalId, disposition.Kind.ToString(), disposition.Rationale,
        ProofCanonical.Join(disposition.SelectedCells.Select(static x => x.ToCanonicalText()).ToArray()),
        ProofCanonical.Join(disposition.EvidenceReceiptDigests.ToArray()));

    private static RouteDisposition ParseDisposition(string canonical)
    {
        var fields = ProofCanonical.Split(canonical, 5);
        if (!Enum.TryParse<RouteDispositionKind>(fields[1], ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
            throw new InvalidDataException("Release disposition kind is invalid.");
        var encodedCells = fields[3].Length == 0 ? [] : ProofCanonical.Split(fields[3], 4096, requireExact: false);
        var evidence = fields[4].Length == 0 ? [] : ProofCanonical.Split(fields[4], 4096, requireExact: false);
        return new(fields[0], kind, fields[2], encodedCells.Select(ProofCellKeyCodec.Parse).ToArray())
            { EvidenceReceiptDigests = evidence };
    }

    private static bool IsAddressOrGenesis(string value) => value == "GENESIS" || IsAddress(value);

    private static bool IsAddress(string value) => value.Length == 64 &&
        value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
