namespace HPD.Payments.Tools.Conformance;

/// <summary>Rejects grouped or inferred Native AOT claims that do not bind an exact proof cell and artifact.</summary>
internal static class AotClaimValidator
{
    internal static IReadOnlyList<string> Validate(ProofCellKey cell, AotClaimEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(evidence);
        var errors = new List<string>();
        if (!StringComparer.Ordinal.Equals(cell.Graph, evidence.Graph) ||
            !StringComparer.Ordinal.Equals(cell.Rid, evidence.Rid) ||
            !StringComparer.Ordinal.Equals(cell.OperatingSystem, evidence.OperatingSystem) ||
            !StringComparer.Ordinal.Equals(cell.Architecture, evidence.Architecture) ||
            !StringComparer.Ordinal.Equals(cell.Sdk, evidence.Sdk) ||
            !StringComparer.Ordinal.Equals(cell.Runtime, evidence.Runtime) ||
            !StringComparer.Ordinal.Equals(cell.Compiler, evidence.Compiler) ||
            !StringComparer.Ordinal.Equals(cell.Linker, evidence.Linker) ||
            !StringComparer.Ordinal.Equals(cell.NativeAot, "true"))
            errors.Add("aot-cell-toolchain-mismatch");
        if (evidence.PublishExitStatus != 0 || evidence.RunExitStatus != 0) errors.Add("aot-publish-or-run-failed");
        if (evidence.TrimWarningCount != 0 || evidence.AotWarningCount != 0) errors.Add("aot-or-trim-warning");
        if (evidence.ReflectionFallbackCount != 0) errors.Add("static-graph-reflection-fallback");
        if (!IsDigest(evidence.BinaryDigest) || !IsDigest(evidence.PublishLogDigest) ||
            !IsDigest(evidence.RunOutputDigest) || !IsDigest(evidence.SourceTreeDigest))
            errors.Add("incomplete-aot-artifact-binding");
        return errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsDigest(string value)
    {
        if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal)) return false;
        foreach (var character in value.AsSpan(7))
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }
}

internal sealed record AotClaimEvidence(string Graph, string Rid, string OperatingSystem, string Architecture,
    string Sdk, string Runtime, string Compiler, string Linker, int PublishExitStatus, int RunExitStatus,
    int TrimWarningCount, int AotWarningCount, int ReflectionFallbackCount, string BinaryDigest,
    string PublishLogDigest, string RunOutputDigest, string SourceTreeDigest);
