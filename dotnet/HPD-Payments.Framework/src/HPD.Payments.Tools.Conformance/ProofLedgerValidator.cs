namespace HPD.Payments.Tools.Conformance;

/// <summary>Validates append-only receipt chains without promoting preparatory evidence into release proof.</summary>
internal static class ProofLedgerValidator
{
    /// <summary>Validates exact-cell uniqueness, chain continuity, proof honesty, and expected-cell completeness.</summary>
    public static ProofValidationResult Validate(IReadOnlyList<ProofReceipt> receipts,
        IReadOnlyCollection<ProofCellKey> expectedCells, string canonicalRegistryDigest, string claimMatrixDigest)
    {
        ArgumentNullException.ThrowIfNull(receipts); ArgumentNullException.ThrowIfNull(expectedCells);
        var errors = new List<string>();
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        var receiptIds = new HashSet<string>(StringComparer.Ordinal);
        var byAddress = new Dictionary<string, ProofReceipt>(StringComparer.Ordinal);
        var explicitlyInvalidated = new HashSet<string>(StringComparer.Ordinal);
        var current = new Dictionary<string, ProofReceipt>(StringComparer.Ordinal);
        var predecessor = "GENESIS";
        foreach (var receipt in receipts)
        {
            errors.AddRange(ProofReceiptContractValidator.Validate(receipt));
            var address = receipt.ContentAddress();
            if (!StringComparer.Ordinal.Equals(receipt.SchemaVersion, "hpd.payments.proof.v1")) errors.Add("unsupported-proof-schema");
            if (!receiptIds.Add(receipt.ReceiptId)) errors.Add("duplicate-receipt-id");
            if (!StringComparer.Ordinal.Equals(receipt.RouteId, receipt.Cell.CanonicalId)) errors.Add("route-cell-mismatch");
            if (!ReleaseSelectionValidator.IsConcrete(receipt.Cell)) errors.Add("non-concrete-receipt-cell");
            if (!Enum.IsDefined(receipt.State) || !Enum.IsDefined(receipt.Lifecycle)) errors.Add("invalid-proof-vocabulary");
            if (!addresses.Add(address)) errors.Add("duplicate-content-address");
            if (!StringComparer.Ordinal.Equals(receipt.PredecessorDigest, predecessor)) errors.Add("broken-predecessor-chain");
            predecessor = address;
            if (!StringComparer.Ordinal.Equals(receipt.CanonicalRegistryDigest, canonicalRegistryDigest) ||
                !StringComparer.Ordinal.Equals(receipt.ClaimMatrixDigest, claimMatrixDigest)) errors.Add("stale-registry-binding");
            if (receipt.StartedAtUtc.Offset != TimeSpan.Zero || receipt.EndedAtUtc.Offset != TimeSpan.Zero || receipt.EndedAtUtc < receipt.StartedAtUtc)
                errors.Add("invalid-time-envelope");
            if (receipt.State == ProofState.Executed && (receipt.ExitStatus != 0 ||
                !receipt.CleanupAttestation.StartsWith("clean:sha256:", StringComparison.Ordinal)))
                errors.Add("false-pass");
            if (receipt.State == ProofState.Executed && (receipt.AssertionsDigest.Length == 0 ||
                receipt.StandardOutputDigest.Length == 0 || receipt.StandardErrorDigest.Length == 0))
                errors.Add("missing-executed-evidence");
            var key = receipt.Cell.ToCanonicalText();
            if (receipt.SupersedesDigest is not null && receipt.InvalidatesDigest is not null)
                errors.Add("ambiguous-replacement");
            var replaces = receipt.SupersedesDigest ?? receipt.InvalidatesDigest;
            if (current.ContainsKey(key) && replaces is null) errors.Add("duplicate-current-cell");
            if (replaces is not null)
            {
                if (!byAddress.TryGetValue(replaces, out var replaced)) errors.Add("missing-replacement-target");
                else if (!StringComparer.Ordinal.Equals(replaced.Cell.ToCanonicalText(), key)) errors.Add("cross-cell-replacement");
                if (receipt.InvalidatesDigest is not null && receipt.Lifecycle != ReceiptLifecycle.Invalidation)
                    errors.Add("invalid-invalidation-lifecycle");
                if (receipt.InvalidatesDigest is not null) explicitlyInvalidated.Add(receipt.InvalidatesDigest);
            }
            if (receipt.SupersedesDigest is not null && receipt.Lifecycle != ReceiptLifecycle.Supersession)
                errors.Add("invalid-supersession-lifecycle");
            if (receipt.SupersedesDigest is null && receipt.InvalidatesDigest is null && receipt.Lifecycle != ReceiptLifecycle.Active)
                errors.Add("orphan-receipt-lifecycle");
            if (receipt.DependencyDigests.Distinct(StringComparer.Ordinal).Count() != receipt.DependencyDigests.Count)
                errors.Add("duplicate-receipt-dependency");
            foreach (var dependency in receipt.DependencyDigests)
                if (!byAddress.ContainsKey(dependency)) errors.Add("missing-or-forward-dependency");
            current[key] = receipt;
            byAddress[address] = receipt;
        }
        var invalidated = new HashSet<string>(explicitlyInvalidated, StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pair in byAddress)
                if (!invalidated.Contains(pair.Key) && pair.Value.DependencyDigests.Any(invalidated.Contains))
                    changed |= invalidated.Add(pair.Key);
        }
        foreach (var receipt in current.Values)
            if (invalidated.Contains(receipt.ContentAddress())) errors.Add("stale-dependent-receipt");
        var expectedCanonical = expectedCells.Select(static x => x.ToCanonicalText()).ToArray();
        if (expectedCanonical.Distinct(StringComparer.Ordinal).Count() != expectedCanonical.Length)
            errors.Add("duplicate-expected-cell");
        var expected = expectedCanonical.ToHashSet(StringComparer.Ordinal);
        foreach (var key in expected) if (!current.ContainsKey(key)) errors.Add("missing-cell");
        foreach (var key in current.Keys) if (!expected.Contains(key)) errors.Add("orphan-cell");
        return new(errors.Count == 0, errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), predecessor);
    }
}

/// <summary>Reports deterministic validation errors and the terminal receipt address.</summary>
internal sealed record ProofValidationResult(bool IsValid, IReadOnlyList<string> Errors, string TerminalDigest);
