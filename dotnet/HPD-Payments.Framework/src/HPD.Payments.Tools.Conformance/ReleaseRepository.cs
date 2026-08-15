namespace HPD.Payments.Tools.Conformance;

/// <summary>Loads and joins an immutable release manifest with its exact append-only proof repository.</summary>
internal static class ReleaseRepository
{
    internal static ReleaseRepositoryResult ValidateCurrentAuthorized(RegistrySnapshot snapshot, string manifestRoot,
        string receiptRoot, string approvalRoot, IReadOnlyDictionary<string, ReleaseApprovalKey> keys,
        ReleaseAuthorizationPolicy policy, DateTimeOffset evaluatedAtUtc)
    {
        var chain = ReleaseManifestRepository.LoadChain(manifestRoot, snapshot);
        if (chain.Count == 0) throw new InvalidDataException("Release manifest repository is empty.");
        var approvals = ReleaseApprovalRepository.LoadAll(approvalRoot);
        var lineageErrors = ReleaseApprovalRepository.ValidateLineage(chain, approvals, keys, policy, evaluatedAtUtc);
        var tip = chain[^1];
        var tipContext = new ReleaseAuthorizationContext(approvals.Where(approval =>
            StringComparer.Ordinal.Equals(approval.ManifestAddress, tip.ContentAddress())).ToArray(), keys, policy, evaluatedAtUtc);
        var result = Validate(snapshot, manifestRoot, tip.ContentAddress(), receiptRoot, tipContext);
        var errors = result.Errors.Concat(lineageErrors).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return result with { Errors = errors, ReleaseReady = result.ReleaseReady && errors.Length == 0 };
    }

    internal static ReleaseRepositoryResult ValidateCurrent(RegistrySnapshot snapshot, string manifestRoot,
        string receiptRoot, ReleaseAuthorizationContext? authorization = null)
    {
        var chain = ReleaseManifestRepository.LoadChain(manifestRoot, snapshot);
        if (chain.Count == 0) throw new InvalidDataException("Release manifest repository is empty.");
        return Validate(snapshot, manifestRoot, chain[^1].ContentAddress(), receiptRoot, authorization);
    }

    internal static ReleaseRepositoryResult Validate(RegistrySnapshot snapshot, string manifestRoot,
        string manifestAddress, string receiptRoot, ReleaseAuthorizationContext? authorization = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var manifest = ReleaseManifestStore.Load(manifestRoot, manifestAddress);
        var selection = manifest.ValidateAgainst(snapshot);
        var receipts = ProofReceiptRepository.LoadChain(receiptRoot);
        var evidence = ReleaseEvidenceValidator.Validate(snapshot, manifest.Dispositions, receipts);
        var errors = new List<string>();
        if (!selection.InventoryValid) errors.AddRange(selection.Errors);

        var latest = new Dictionary<string, ProofReceipt>(StringComparer.Ordinal);
        foreach (var receipt in receipts) latest[receipt.Cell.ToCanonicalText()] = receipt;
        foreach (var cell in manifest.Dispositions.Where(static x => x.Kind == RouteDispositionKind.Selected)
            .SelectMany(static x => x.SelectedCells))
        {
            if (latest.TryGetValue(cell.ToCanonicalText(), out var receipt) &&
                !StringComparer.Ordinal.Equals(receipt.SourceRevision, manifest.SourceRevision))
                errors.Add("selected-receipt-source-revision-mismatch");
        }
        if (manifest.Lifecycle == ReleaseManifestLifecycle.Published && !evidence.ReleaseReady)
            errors.Add("published-manifest-without-complete-evidence");
        if (manifest.Lifecycle != ReleaseManifestLifecycle.Candidate)
        {
            if (authorization is null) errors.Add("release-manifest-without-authorization");
            else errors.AddRange(ReleaseAuthorizationValidator.Validate(manifest, authorization));
        }

        var distinctErrors = errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(manifest, receipts, evidence, distinctErrors,
            manifest.Lifecycle == ReleaseManifestLifecycle.Published && evidence.ReleaseReady && distinctErrors.Length == 0);
    }
}

/// <summary>Reports the exact durable manifest/receipt join without upgrading either artifact.</summary>
internal sealed record ReleaseRepositoryResult(ReleaseManifest Manifest, IReadOnlyList<ProofReceipt> Receipts,
    ReleaseEvidenceResult Evidence, IReadOnlyList<string> Errors, bool ReleaseReady);
