namespace HPD.Base.Studio;

internal static class BaseStudioInfrastructureContracts
{
    internal static bool IsInventoryView(string id) => id.StartsWith("base.infrastructure.", StringComparison.Ordinal) ||
        id.StartsWith("base.store.detail.", StringComparison.Ordinal) || id.StartsWith("base.provider.detail.", StringComparison.Ordinal) ||
        id.StartsWith("base.schema.detail.", StringComparison.Ordinal) || id.StartsWith("base.migration.detail.", StringComparison.Ordinal) ||
        id.StartsWith("base.backup.detail.", StringComparison.Ordinal) || id.StartsWith("base.restore.detail.", StringComparison.Ordinal) ||
        id.StartsWith("base.maintenance.detail.", StringComparison.Ordinal);

    internal static string ItemDescriptor(string id) => $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', Fields(id).Order(StringComparer.Ordinal).Select(Property))}],\"additionalProperties\":false}}";

    internal static string[] Fields(string id)
    {
        if (id == "base.infrastructure.stores.list") return ["storeIdentity", "storeInstanceId", "restoreEpoch", "schemaGeneration", "authorityChecksum"];
        if (id == "base.infrastructure.schemas.list") return Schema("summary");
        if (id == "base.infrastructure.backups.list") return Backup("summary");
        if (id is "base.infrastructure.maintenance.list" or "base.infrastructure.attention.list") return Maintenance("summary");
        if (id.StartsWith("base.store.detail.", StringComparison.Ordinal)) return Store(id.Split('.')[^2]);
        if (id.StartsWith("base.provider.detail.", StringComparison.Ordinal)) return Provider(id.Split('.')[^2]);
        string section = id.Split('.')[^2];
        if (id.StartsWith("base.schema.detail.", StringComparison.Ordinal)) return Schema(section);
        if (id.StartsWith("base.migration.detail.", StringComparison.Ordinal)) return Migration(section);
        if (id.StartsWith("base.backup.detail.", StringComparison.Ordinal)) return Backup(section);
        if (id.StartsWith("base.restore.detail.", StringComparison.Ordinal)) return Restore(section);
        if (id.StartsWith("base.maintenance.detail.", StringComparison.Ordinal)) return Maintenance(section);
        throw new InvalidOperationException("base.studio.infrastructureViewUnknown");
    }

    private static string[] Store(string section) => section switch
    {
        "summary" => ["storeIdentity", "storeInstanceId", "restoreEpoch", "schemaGeneration", "authorityChecksum"],
        "capabilities" => ["providerId", "providerVersion", "providerGeneration", "capabilityChecksum", "authorityChecksum"],
        "certification" => ["providerId", "providerVersion", "capabilityChecksum", "inventoryCertificationChecksum", "durableThroughBackupRestore"],
        "assets" => ["recordStoreRegistrationId", "schemaDigest", "storeInstanceId", "schemaGeneration", "authorityChecksum"],
        "health" => ["entryId", "status", "checkedAtUtc", "entryChecksum"],
        "retainedWork" => ["retainedQuarantineCount", "retentionClass", "capturedAtUtc", "authorityChecksum"],
        "quarantine" => ["quarantineIdentity", "quarantineKind", "operationId", "quarantinedAt", "itemChecksum"],
        "maintenance" => Maintenance("summary"),
        "recovery" => ["restoreEpoch", "latestRestoreIdentity", "latestRestoreState", "recoveryAuthorityClass", "authorityChecksum"],
        "diagnostics" => ["diagnosticCount", "highestSeverity", "capturedAtUtc", "nativeMessagesExposed", "authorityChecksum"],
        _ => throw new InvalidOperationException("base.studio.storeSectionUnknown"),
    };
    private static string[] Provider(string section) => section switch
    {
        "summary" => ["providerId", "providerVersion", "providerGeneration", "storeIdentity", "authorityChecksum"],
        "capability" => ["providerId", "providerVersion", "capabilityChecksum", "supportedInventoryKinds", "authorityChecksum"],
        "certification" => ["providerId", "providerVersion", "capabilityChecksum", "inventoryCertificationChecksum", "durableThroughBackupRestore"],
        "health" => ["entryId", "status", "checkedAtUtc", "entryChecksum"],
        "diagnostics" => ["diagnosticCount", "highestSeverity", "capturedAtUtc", "nativeMessagesExposed", "authorityChecksum"],
        _ => throw new InvalidOperationException("base.studio.providerSectionUnknown"),
    };

    private static string[] Schema(string section) => section switch
    {
        "summary" => ["baselineId", "schemaGeneration", "state", "observedAtUtc", "itemChecksum"],
        "graph" => ["baselineId", "schemaChecksum", "schemaGeneration", "itemChecksum"],
        "drift" => ["baselineId", "driftDetected", "schemaGeneration", "itemChecksum"],
        "plans" => ["baselineId", "schemaGeneration", "planAuthority", "itemChecksum"],
        "history" => ["baselineId", "sequence", "state", "observedAtUtc", "itemChecksum"],
        "evidence" => ["baselineId", "storeId", "restoreEpoch", "schemaGeneration", "itemChecksum"],
        _ => throw new InvalidOperationException("base.studio.schemaSectionUnknown"),
    };
    private static string[] Migration(string section) => section switch
    {
        "summary" => ["migrationId", "fromSchemaGeneration", "toSchemaGeneration", "state", "itemChecksum"],
        "plan" => ["migrationId", "planChecksum", "fromSchemaGeneration", "toSchemaGeneration", "itemChecksum"],
        "compatibility" => ["migrationId", "fromSchemaGeneration", "toSchemaGeneration", "compatibilityClass", "itemChecksum"],
        "progress" => ["migrationId", "state", "observedAtUtc", "sequence", "itemChecksum"],
        "history" => ["migrationId", "state", "observedAtUtc", "sequence", "itemChecksum"],
        _ => throw new InvalidOperationException("base.studio.migrationSectionUnknown"),
    };
    private static string[] Backup(string section) => section switch
    {
        "summary" => ["artifactId", "state", "observedAtUtc", "artifactAvailable", "itemChecksum"],
        "authentication" => ["artifactId", "artifactDigest", "artifactAvailable", "itemChecksum"],
        "contents" => ["artifactId", "artifactBytes", "artifactAvailable", "itemChecksum"],
        "compatibility" => ["artifactId", "storeId", "restoreEpoch", "schemaGeneration", "itemChecksum"],
        "history" => ["artifactId", "state", "sequence", "observedAtUtc", "itemChecksum"],
        _ => throw new InvalidOperationException("base.studio.backupSectionUnknown"),
    };
    private static string[] Restore(string section) => section switch
    {
        "summary" => ["restoreRequestIdentity", "state", "observedAtUtc", "itemChecksum"],
        "artifact" => ["restoreRequestIdentity", "artifactDigest", "artifactAvailable", "itemChecksum"],
        "consequences" => ["restoreRequestIdentity", "sourceRestoreEpoch", "resultRestoreEpoch", "authorityChanged", "itemChecksum"],
        "progress" => ["restoreRequestIdentity", "state", "sequence", "observedAtUtc", "itemChecksum"],
        "newAuthority" => ["restoreRequestIdentity", "storeId", "resultRestoreEpoch", "schemaGeneration", "itemChecksum"],
        "reconciliation" => ["restoreRequestIdentity", "state", "reconciliationRequired", "itemChecksum"],
        _ => throw new InvalidOperationException("base.studio.restoreSectionUnknown"),
    };
    private static string[] Maintenance(string section) => section switch
    {
        "summary" => ["maintenanceKind", "operationIdentity", "state", "observedAtUtc", "itemChecksum"],
        "scope" => ["maintenanceKind", "operationIdentity", "storeId", "restoreEpoch", "itemChecksum"],
        "progress" => ["maintenanceKind", "operationIdentity", "progressBasisPoints", "state", "itemChecksum"],
        "retainedWork" => ["maintenanceKind", "operationIdentity", "retentionAuthority", "itemChecksum"],
        "history" => ["maintenanceKind", "operationIdentity", "sequence", "observedAtUtc", "state", "itemChecksum"],
        "evidence" => ["maintenanceKind", "operationIdentity", "schemaGeneration", "restoreEpoch", "itemChecksum"],
        _ => throw new InvalidOperationException("base.studio.maintenanceSectionUnknown"),
    };
    private static string Property(string name)
    {
        string type = name.EndsWith("Checksum", StringComparison.Ordinal) || name == "authorityChecksum" || name == "artifactDigest" || name == "capabilityChecksum" ? "base.studio.sha256" :
            name is "restoreEpoch" or "sourceRestoreEpoch" or "resultRestoreEpoch" or "schemaGeneration" or "fromSchemaGeneration" or "toSchemaGeneration" or "sequence" or "artifactBytes" or "progressBasisPoints" or "providerVersion" or "providerGeneration" or "retainedQuarantineCount" or "diagnosticCount" ? "base.studio.nonnegative-long" : "base.studio.text";
        return $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
    }
}
