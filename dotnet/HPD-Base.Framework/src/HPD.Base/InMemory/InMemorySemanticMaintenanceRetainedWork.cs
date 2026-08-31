using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.Base;

internal static class InMemorySemanticMaintenanceRetainedWork
{
    internal static long MeasureDefinition(BaseSemanticActivationDefinitionKey value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddString(value.Id);
        counter.AddInteger();
        counter.AddBytes(value.Checksum.Length);
        return counter.Bytes;
    }

    internal static long MeasureBoundary(BaseSemanticActivationRecoveryBoundary? value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddBoolean();
        if (value is null) return counter.Bytes;
        counter.AddContainer();
        counter.AddString(value.DefinitionId);
        counter.AddBytes(value.ScopeBindingId.Length);
        counter.AddBytes(32);
        return counter.Bytes;
    }

    internal static long MeasureCheckpoint(BaseSemanticActivationMaintenanceCheckpoint? value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddBoolean();
        if (value is null) return counter.Bytes;
        counter.AddContainer();
        counter.AddString(value.MaintenanceId);
        counter.AddBytes(value.ProviderIncarnation.Length);
        counter.AddInteger();
        counter.AddInteger();
        counter.AddBytes(value.FenceToken.Length);
        counter.AddString(value.OperationKind);
        counter.Add(MeasureDefinition(value.Definition));
        counter.AddInteger();
        counter.Add(MeasureBoundary(value.After));
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.AddBytes(value.RollingChecksum.Length);
        counter.AddBytes(value.RequestFingerprint.Length);
        counter.AddBytes(value.Checksum.Length);
        return counter.Bytes;
    }

    internal static long MeasureResult(BaseSemanticActivationMaintenanceResult value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddBytes(value.ProviderIncarnation.Length);
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.AddBytes(value.AuthorityChecksum.Length);
        counter.AddBytes(value.ResultChecksum.Length);
        counter.Add(MeasureCheckpoint(value.Checkpoint));
        counter.AddNullableInteger(value.ReceiptDisposition.HasValue);
        counter.AddBytes(value.CommitObservationChecksum.Length);
        return counter.Bytes;
    }

    internal static long MeasureAccounting(InMemorySemanticMaintenanceAccounting value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        for (int index = 0; index < 8; index++) counter.AddInteger();
        return counter.Bytes;
    }

    internal static long MeasureEntry(string dictionaryKey, InMemorySemanticMaintenanceEntry value,
        bool includeStaging = true)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddString(dictionaryKey);
        counter.AddBytes(value.Fingerprint.LongLength);
        counter.AddString(value.Kind);
        counter.Add(MeasureDefinition(value.Definition));
        counter.AddBoolean();
        if (value.TargetDefinition is not null)
            counter.Add(MeasureDefinition(value.TargetDefinition));
        counter.Add(MeasureResult(value.Result));
        counter.AddContainer();
        if (includeStaging)
            foreach ((string key, InMemorySemanticActivationSlot slot) in value.StagedSlots)
                counter.Add(MeasureSlotEntry(key, slot));
        counter.AddSequence(value.ProcessedAuthorities.Count);
        foreach (byte[] authority in value.ProcessedAuthorities)
            counter.AddBytes(authority.LongLength);
        counter.AddSequence(value.ProcessedCanonicalBytes.Count);
        foreach (long _ in value.ProcessedCanonicalBytes)
            counter.AddInteger();
        counter.Add(MeasureAccounting(value.Accounting));
        return counter.Bytes;
    }

    internal static long MeasureSlotEntry(string dictionaryKey, InMemorySemanticActivationSlot value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddString(dictionaryKey);
        counter.AddBytes(value.CanonicalKey.LongLength);
        counter.AddContainer();
        counter.AddInteger();
        counter.AddBytes(value.ScopeBinding.BindingId.Length);
        counter.AddBytes(value.ScopeBinding.ProtectedCanonicalScope.Length);
        counter.AddBytes(value.ScopeBinding.SeekDigest.Length);
        counter.AddString(value.ScopeBinding.ProtectionKeyId);
        counter.AddInteger();
        counter.AddBytes(value.ScopeBinding.Checksum.Length);
        counter.AddInteger();
        counter.AddBytes(AuthorityBytes(value).Length);
        return counter.Bytes;
    }

    internal static long MeasureMaintenanceDictionary(
        IReadOnlyDictionary<string, InMemorySemanticMaintenanceEntry> values)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        foreach ((string key, InMemorySemanticMaintenanceEntry entry) in values)
            counter.Add(MeasureEntry(key, entry));
        return counter.Bytes;
    }

    internal static long MeasureSlotDictionary(
        IReadOnlyDictionary<string, InMemorySemanticActivationSlot> values)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        foreach ((string key, InMemorySemanticActivationSlot slot) in values)
            counter.Add(MeasureSlotEntry(key, slot));
        return counter.Bytes;
    }

    internal static long MeasureStoreAuthority(BaseSemanticActivationStoreAuthorityRequirement? value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddBoolean();
        if (value is null) return counter.Bytes;
        counter.AddContainer();
        counter.AddString(value.ApplicationId);
        counter.AddString(value.LogicalStoreId);
        counter.AddString(value.StoreInstanceId);
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.AddBytes(value.DefinitionSetChecksum.Length);
        return counter.Bytes;
    }

    internal static long MeasureHistoricalAuthority(InMemorySemanticHistoricalAuthority value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddBytes(value.ScopeBindingId.LongLength);
        counter.AddBytes(value.KeyDigest.LongLength);
        counter.AddInteger();
        counter.AddBytes(value.CanonicalAuthority.LongLength);
        return counter.Bytes;
    }

    internal static long MeasureHistoricalAuthorityDictionary(
        IReadOnlyDictionary<string, ImmutableArray<InMemorySemanticHistoricalAuthority>> values)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        foreach ((string key, ImmutableArray<InMemorySemanticHistoricalAuthority> history) in values)
        {
            counter.AddContainer();
            counter.AddString(key);
            counter.AddSequence(history.Length);
            foreach (InMemorySemanticHistoricalAuthority value in history)
                counter.Add(MeasureHistoricalAuthority(value));
        }
        return counter.Bytes;
    }

    internal static long MeasureHistoricalAuthoritySequence(
        ImmutableArray<InMemorySemanticHistoricalAuthority> values)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddSequence(values.Length);
        foreach (InMemorySemanticHistoricalAuthority value in values)
            counter.Add(MeasureHistoricalAuthority(value));
        return counter.Bytes;
    }

    internal static long MeasureMigrationDefinition(BaseSemanticActivationMigrationDefinition? value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddBoolean();
        if (value is null) return counter.Bytes;
        counter.AddContainer();
        counter.AddString(value.Id);
        counter.AddInteger();
        counter.Add(MeasureDefinition(value.From));
        counter.Add(MeasureDefinition(value.To));
        counter.AddBytes(value.Checksum.Length);
        return counter.Bytes;
    }

    internal static long MeasureMigrationAuthority(BaseSemanticActivationDefinitionMigrationAuthority value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddString(value.MigrationId);
        counter.AddInteger();
        counter.Add(MeasureDefinition(value.From));
        counter.Add(MeasureDefinition(value.To));
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.AddBytes(value.OrderedNegativeAuthorityChecksum.Length);
        counter.AddInteger();
        counter.AddBytes(value.ReceiptChecksum.Length);
        counter.AddBytes(value.Checksum.Length);
        return counter.Bytes;
    }

    internal static long MeasureMigrationAuthorityDictionary(
        IReadOnlyDictionary<string, BaseSemanticActivationDefinitionMigrationAuthority> values)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        foreach ((string key, BaseSemanticActivationDefinitionMigrationAuthority value) in values)
        {
            counter.AddContainer();
            counter.AddString(key);
            counter.Add(MeasureMigrationAuthority(value));
        }
        return counter.Bytes;
    }

    internal static long MeasureRemovedDefinitions(IReadOnlyCollection<string> values)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddSequence(values.Count);
        foreach (string value in values.Order(StringComparer.Ordinal)) counter.AddString(value);
        return counter.Bytes;
    }

    internal static long MeasureRemovedDefinitionAuthority(InMemorySemanticRemovedDefinitionAuthority value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        counter.Add(MeasureDefinition(value.Definition));
        counter.AddBytes(JsonSerializer.SerializeToUtf8Bytes(
            value.Removal, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRemovalAuthority).LongLength);
        counter.AddInteger();
        counter.AddBytes(value.AbsenceChecksum.LongLength);
        counter.AddInteger();
        counter.AddBytes(value.ReceiptChecksum.LongLength);
        counter.AddBytes(value.Checksum.LongLength);
        return counter.Bytes;
    }

    internal static long MeasureRemovedDefinitionAuthorityDictionary(
        IReadOnlyDictionary<string, InMemorySemanticRemovedDefinitionAuthority> values)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        foreach ((string key, InMemorySemanticRemovedDefinitionAuthority value) in values)
        {
            counter.AddContainer();
            counter.AddString(key);
            counter.Add(MeasureRemovedDefinitionAuthority(value));
        }
        return counter.Bytes;
    }

    internal static long MeasurePlan(InMemorySemanticMaintenancePlan value)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddContainer();
        counter.Add(MeasureEntry(string.Empty, value.Entry));
        counter.AddBoolean();
        if (value.ReplacementDefinitionSetChecksum is { } checksum) counter.AddBytes(checksum.Length);
        counter.AddBoolean();
        counter.Add(MeasureMigrationDefinition(value.Migration));
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.Add(MeasureHistoricalAuthoritySequence(value.HistoricalAuthority));
        counter.AddBoolean();
        if (value.MigrationAuthority is { } migrationAuthority)
            counter.Add(MeasureMigrationAuthority(migrationAuthority));
        counter.AddBoolean();
        if (value.RemovalAuthority is { } removalAuthority)
            counter.Add(MeasureRemovedDefinitionAuthority(removalAuthority));
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.Add(MeasureBoundary(value.ReadLowerBoundary));
        counter.Add(MeasureBoundary(value.ReadUpperBoundary));
        counter.AddInteger();
        counter.AddInteger();
        return counter.Bytes;
    }

    internal static ImmutableArray<byte> AuthorityBytes(InMemorySemanticActivationSlot slot) => slot switch
    {
        { Live: { } value } => JsonSerializer.SerializeToUtf8Bytes(
            value, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority).ToImmutableArray(),
        { Retired: { } value } => JsonSerializer.SerializeToUtf8Bytes(
            value, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority).ToImmutableArray(),
        { Absent: { } value } => JsonSerializer.SerializeToUtf8Bytes(
            value, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority).ToImmutableArray(),
        _ => throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt),
    };
}
