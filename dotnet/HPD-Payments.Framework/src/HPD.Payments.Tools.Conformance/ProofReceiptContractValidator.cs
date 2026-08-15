using System.Buffers;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Validates one receipt's bounded scalar, digest, seed, address, and state contract.</summary>
internal static class ProofReceiptContractValidator
{
    internal static IReadOnlyList<string> Validate(ProofReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var errors = new List<string>();
        var required = new[]
        {
            receipt.ReceiptId, receipt.RunId, receipt.RouteId, receipt.SourceRevision, receipt.DirtyState,
            receipt.CommandBinding, receipt.OracleBinding, receipt.CodeRevision, receipt.ConfigurationRevision,
            receipt.CredentialRevision, receipt.ProtocolRevision, receipt.PolicyRevision,
            receipt.ResourceObservations, receipt.Limitations, receipt.CleanupAttestation, receipt.Provenance,
        };
        if (required.Any(static x => string.IsNullOrWhiteSpace(x) || x.Length > 16_384))
            errors.Add("missing-or-over-bound-receipt-field");

        var digests = new[]
        {
            receipt.WholeTreeDigest, receipt.AdapterTreeDigest, receipt.CanonicalRegistryDigest,
            receipt.ClaimMatrixDigest, receipt.AssertionsDigest, receipt.CorpusDigest,
            receipt.VirtualTimeTraceDigest, receipt.FaultScheduleDigest, receipt.StandardOutputDigest,
            receipt.StandardErrorDigest,
        };
        if (digests.Any(static x => !IsPrefixedDigest(x))) errors.Add("malformed-receipt-digest");
        if (!IsSeed(receipt.RootSeed) || !IsSeed(receipt.DerivedSeed)) errors.Add("malformed-proof-seed");
        if (!IsAddressOrGenesis(receipt.PredecessorDigest) ||
            (receipt.SupersedesDigest is not null && !IsAddress(receipt.SupersedesDigest)) ||
            (receipt.InvalidatesDigest is not null && !IsAddress(receipt.InvalidatesDigest)) ||
            receipt.DependencyDigests.Any(static x => !IsAddress(x)))
            errors.Add("malformed-receipt-address");

        var cellValues = new[]
        {
            receipt.Cell.CanonicalId, receipt.Cell.Owner, receipt.Cell.Family, receipt.Cell.OwnCell,
            receipt.Cell.ExternalCell, receipt.Cell.Profile, receipt.Cell.Lane, receipt.Cell.Adapter,
            receipt.Cell.Provider, receipt.Cell.ProviderAccount, receipt.Cell.ProviderEnvironment,
            receipt.Cell.ProviderApiVersion, receipt.Cell.Graph, receipt.Cell.Rid, receipt.Cell.OperatingSystem,
            receipt.Cell.Architecture, receipt.Cell.Sdk, receipt.Cell.Runtime, receipt.Cell.Compiler,
            receipt.Cell.Linker, receipt.Cell.NativeAot, receipt.Cell.Path, receipt.Cell.Workload,
        };
        if (cellValues.Any(static x => x.Length > 4_096)) errors.Add("over-bound-proof-cell-dimension");
        return errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsPrefixedDigest(string value) => value.Length == 71 && value.StartsWith("sha256:", StringComparison.Ordinal) &&
        !value.AsSpan(7).ContainsAnyExcept(LowerHex);

    private static bool IsSeed(string value) => value.Length == 64 &&
        !value.AsSpan().ContainsAnyExcept(LowerHex);

    private static bool IsAddressOrGenesis(string value) => value == "GENESIS" || IsAddress(value);

    private static bool IsAddress(string value) => IsSeed(value);

    private static readonly SearchValues<char> LowerHex = SearchValues.Create("0123456789abcdef");
}
