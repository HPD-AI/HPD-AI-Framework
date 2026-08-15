namespace HPD.Payments.Tools.Conformance;

/// <summary>Names an explicit release disposition for one canonical route.</summary>
internal enum RouteDispositionKind
{
    None = 0,
    Selected,
    Unsupported,
    Untested,
    Blocked,
}

/// <summary>Dispositions exactly one canonical route and owns any selected exact proof cells.</summary>
internal sealed record RouteDisposition(string CanonicalId, RouteDispositionKind Kind, string Rationale,
    IReadOnlyList<ProofCellKey> SelectedCells)
{
    internal IReadOnlyList<string> EvidenceReceiptDigests { get; init; } = Array.Empty<string>();
}

/// <summary>Validates complete release inventory separately from actual release completeness.</summary>
internal static class ReleaseSelectionValidator
{
    /// <summary>Validates exactly one disposition per frozen route and fully concrete selected cells.</summary>
    internal static ReleaseSelectionResult Validate(RegistrySnapshot snapshot, IReadOnlyCollection<RouteDisposition> dispositions)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(dispositions);
        var errors = new List<string>();
        if (dispositions.Count != snapshot.Routes.Count) errors.Add("incomplete-route-inventory");
        var byRoute = new Dictionary<string, RouteDisposition>(StringComparer.Ordinal);
        var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var disposition in dispositions)
        {
            if (!byRoute.TryAdd(disposition.CanonicalId, disposition)) errors.Add("duplicate-route-disposition");
            if (disposition.Kind == RouteDispositionKind.None || !Enum.IsDefined(disposition.Kind)) errors.Add("invalid-disposition");
            if (string.IsNullOrWhiteSpace(disposition.Rationale) || disposition.Rationale.Length > 16_384) errors.Add("missing-or-over-bound-rationale");
            if (disposition.Kind == RouteDispositionKind.Selected)
            {
                if (disposition.SelectedCells.Count == 0) errors.Add("selected-route-without-cell");
                foreach (var cell in disposition.SelectedCells)
                {
                    if (!StringComparer.Ordinal.Equals(cell.CanonicalId, disposition.CanonicalId)) errors.Add("cross-route-selected-cell");
                    if (!IsConcrete(cell)) errors.Add("non-concrete-selected-cell");
                    if (!selectedKeys.Add(cell.ToCanonicalText())) errors.Add("duplicate-selected-cell");
                }
                if (disposition.EvidenceReceiptDigests.Count != 0) errors.Add("selected-route-has-disposition-evidence");
            }
            else if (disposition.SelectedCells.Count != 0) errors.Add("unclaimed-route-has-selected-cell");
            if (disposition.Kind == RouteDispositionKind.Unsupported)
            {
                if (disposition.EvidenceReceiptDigests.Count == 0) errors.Add("unsupported-without-negative-evidence");
                if (disposition.EvidenceReceiptDigests.Distinct(StringComparer.Ordinal).Count() != disposition.EvidenceReceiptDigests.Count ||
                    disposition.EvidenceReceiptDigests.Any(static x => !IsAddress(x)))
                    errors.Add("invalid-negative-evidence-address");
            }
            else if (disposition.Kind != RouteDispositionKind.Selected && disposition.EvidenceReceiptDigests.Count != 0)
                errors.Add("non-unsupported-route-has-negative-evidence");
        }

        var expected = snapshot.Routes.Select(static x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(byRoute.Keys)) errors.Add("missing-or-orphan-route-disposition");
        var claims = snapshot.Claims.ToDictionary(static x => x.CanonicalId, StringComparer.Ordinal);
        foreach (var route in snapshot.Routes)
        {
            if (!byRoute.TryGetValue(route.Id, out var disposition)) continue;
            var baselineBlocked = claims[route.Id].Applicability == "Blocked";
            if (baselineBlocked && disposition.Kind != RouteDispositionKind.Blocked) errors.Add("blocked-route-promoted");
            if (!baselineBlocked && disposition.Kind == RouteDispositionKind.Blocked) errors.Add("invented-route-block");
            foreach (var cell in disposition.SelectedCells)
            {
                var ownerMatches = route.AuthorityOwners.Count == 0
                    ? StringComparer.Ordinal.Equals(cell.Owner, route.OwnerOrSupportingConcept)
                    : route.AuthorityOwners.Contains(cell.Owner, StringComparer.Ordinal);
                if (!ownerMatches) errors.Add("selected-cell-owner-mismatch");
                if (!StringComparer.Ordinal.Equals(cell.Family, route.CandidateContractFamily))
                    errors.Add("selected-cell-family-mismatch");
            }
        }
        var inventoryValid = errors.Count == 0;
        var releaseComplete = inventoryValid && dispositions.All(static x => x.Kind is RouteDispositionKind.Selected or RouteDispositionKind.Unsupported) &&
            Enumerable.Range(1, 6).Select(static x => $"TEST-{x:000}").All(id => byRoute[id].Kind == RouteDispositionKind.Selected);
        return new(inventoryValid, releaseComplete, errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    internal static bool IsConcrete(ProofCellKey cell)
    {
        var values = new[] { cell.CanonicalId, cell.Owner, cell.Family, cell.OwnCell, cell.ExternalCell, cell.Profile,
            cell.Lane, cell.Adapter, cell.Provider, cell.ProviderAccount, cell.ProviderEnvironment, cell.ProviderApiVersion,
            cell.Graph, cell.Rid, cell.OperatingSystem, cell.Architecture, cell.Sdk, cell.Runtime, cell.Compiler,
            cell.Linker, cell.NativeAot, cell.Path, cell.Workload };
        return values.All(static x => !string.IsNullOrWhiteSpace(x) &&
            !x.Contains('*', StringComparison.Ordinal) && !StringComparer.OrdinalIgnoreCase.Equals(x, "Unselected"));
    }

    private static bool IsAddress(string value) => value.Length == 64 &&
        value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>Reports whether inventory is structurally valid and whether it is releasable.</summary>
internal sealed record ReleaseSelectionResult(bool InventoryValid, bool ReleaseComplete, IReadOnlyList<string> Errors);
