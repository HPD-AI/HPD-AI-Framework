namespace HPD.Payments.Tools.Conformance;

/// <summary>Joins explicit release selection to current exact-cell proof receipts.</summary>
internal static class ReleaseEvidenceValidator
{
    /// <summary>Requires every selected exact cell to have one valid current Executed receipt.</summary>
    internal static ReleaseEvidenceResult Validate(RegistrySnapshot snapshot,
        IReadOnlyCollection<RouteDisposition> dispositions, IReadOnlyList<ProofReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(dispositions);
        ArgumentNullException.ThrowIfNull(receipts);
        var selection = ReleaseSelectionValidator.Validate(snapshot, dispositions);
        var selected = dispositions.Where(static x => x.Kind == RouteDispositionKind.Selected)
            .SelectMany(static x => x.SelectedCells).ToArray();
        var byAddress = new Dictionary<string, ProofReceipt>(StringComparer.Ordinal);
        foreach (var receipt in receipts) byAddress.TryAdd(receipt.ContentAddress(), receipt);
        var unsupportedEvidence = new List<ProofReceipt>();
        var evidenceErrors = new List<string>();
        foreach (var disposition in dispositions.Where(static x => x.Kind == RouteDispositionKind.Unsupported))
            foreach (var digest in disposition.EvidenceReceiptDigests)
            {
                if (!byAddress.TryGetValue(digest, out var receipt)) evidenceErrors.Add("missing-disposition-evidence");
                else if (!StringComparer.Ordinal.Equals(receipt.RouteId, disposition.CanonicalId) || receipt.State != ProofState.Executed)
                    evidenceErrors.Add("invalid-disposition-evidence");
                else unsupportedEvidence.Add(receipt);
            }
        var expected = selected.Concat(unsupportedEvidence.Select(static x => x.Cell)).ToArray();
        var proof = ProofLedgerValidator.Validate(receipts, expected, snapshot.CanonicalDigest, snapshot.ClaimMatrixDigest);
        var current = new Dictionary<string, ProofReceipt>(StringComparer.Ordinal);
        foreach (var receipt in receipts) current[receipt.Cell.ToCanonicalText()] = receipt;
        var selectedEvidenceComplete = selection.InventoryValid && proof.IsValid && selected.All(cell =>
            current.TryGetValue(cell.ToCanonicalText(), out var receipt) && receipt.State == ProofState.Executed);
        var dispositionEvidenceComplete = evidenceErrors.Count == 0;
        return new(selection, proof, selectedEvidenceComplete, dispositionEvidenceComplete,
            evidenceErrors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            selection.ReleaseComplete && selectedEvidenceComplete && dispositionEvidenceComplete);
    }
}

/// <summary>Reports structural selection, proof-chain validity, selected evidence closure and final readiness separately.</summary>
internal sealed record ReleaseEvidenceResult(ReleaseSelectionResult Selection, ProofValidationResult Proof,
    bool SelectedEvidenceComplete, bool DispositionEvidenceComplete, IReadOnlyList<string> EvidenceErrors,
    bool ReleaseReady);
