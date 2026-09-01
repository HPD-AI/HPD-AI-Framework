namespace HPD.Base.Studio;

internal static class BaseStudioDiagnosticsContracts
{
    internal static bool IsDiagnosticsView(string id) => id.StartsWith("base.diagnostics.", StringComparison.Ordinal) || id.StartsWith("base.health.detail.", StringComparison.Ordinal) || id.StartsWith("base.diagnostic.detail.", StringComparison.Ordinal);
    internal static string ItemDescriptor(string id) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', Fields(id).Order(StringComparer.Ordinal).Select(Property))}],\"additionalProperties\":false}}";
    internal static string[] Fields(string id) => id switch
    {
        "base.diagnostics.incidents.list" => ["contributorId", "entryId", "code", "severity", "category", "emittedAtUtc", "entryChecksum"],
        "base.diagnostics.health.list" => ["contributorId", "entryId", "scope", "status", "checkedAtUtc", "entryChecksum"],
        "base.diagnostics.accounting.detail" => ["healthContributorCount", "diagnosticContributorCount", "healthEntryCount", "diagnosticEntryCount", "capturedAtUtc", "entryChecksum"],
        "base.health.detail.summary.detail" => ["contributorId", "entryId", "scope", "status", "targetRef", "checkedAtUtc", "entryChecksum"],
        "base.health.detail.dependencies.list" => ["dependencyId", "dependencyKind", "dependencyStatus", "entryChecksum"],
        "base.health.detail.history.list" => ["contributorId", "entryId", "status", "checkedAtUtc", "historyClass", "entryChecksum"],
        "base.health.detail.remediation.detail" => ["contributorId", "entryId", "remediationClass", "typedActionAvailable", "entryChecksum"],
        "base.diagnostic.detail.summary.detail" => ["contributorId", "entryId", "code", "severity", "category", "emittedAtUtc", "entryChecksum"],
        "base.diagnostic.detail.correlation.detail" => ["contributorId", "entryId", "targetRef", "targetPath", "correlationClass", "entryChecksum"],
        "base.diagnostic.detail.affectedResources.list" => ["featureId", "relation", "entryChecksum"],
        "base.diagnostic.detail.accounting.detail" => ["contributorReads", "aggregateReads", "projectedFields", "nativeMessageFields", "entryChecksum"],
        "base.diagnostic.detail.evidence.detail" => ["contributorId", "entryId", "code", "emittedAtUtc", "visibility", "entryChecksum"],
        _ => throw new InvalidOperationException("base.studio.diagnosticsViewUnknown"),
    };
    private static string Property(string name)
    {
        string type = name.EndsWith("Checksum", StringComparison.Ordinal) ? "base.studio.sha256" : name.EndsWith("Count", StringComparison.Ordinal) || name is "contributorReads" or "aggregateReads" or "projectedFields" or "nativeMessageFields" ? "base.studio.nonnegative-long" : "base.studio.text";
        return $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
    }
}
