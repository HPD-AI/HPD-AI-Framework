using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Defines one graph-installed authority-only semantic definition migration.</summary>
public sealed record BaseSemanticActivationMigrationDefinition
{
    /// <summary>Gets the stable migration identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive migration version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the exact source definition.</summary>
    public required BaseSemanticActivationDefinitionKey From { get; init; }
    /// <summary>Gets the exact destination definition.</summary>
    public required BaseSemanticActivationDefinitionKey To { get; init; }
    /// <summary>Gets the canonical migration checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Creates and validates graph-owned semantic definition migrations.</summary>
public static class BaseSemanticActivationMigrationContract
{
    /// <summary>Returns a deeply owned migration with its canonical checksum.</summary>
    public static BaseSemanticActivationMigrationDefinition Seal(BaseSemanticActivationMigrationDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value.Id) || value.Version <= 0 || !Valid(value.From) || !Valid(value.To)
            || !string.Equals(value.From.Id, value.To.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("base.semanticActivation.migrationInvalid");
        var sealedValue = value with
        {
            Id = new string(value.Id.AsSpan()), From = Clone(value.From), To = Clone(value.To), Checksum = [],
        };
        ImmutableArray<byte> checksum = Checksum(sealedValue);
        if (!value.Checksum.IsDefaultOrEmpty && !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            value.Checksum.AsSpan(), checksum.AsSpan()))
            throw new InvalidOperationException("base.semanticActivation.migrationInvalid");
        return sealedValue with { Checksum = checksum };
    }

    /// <summary>Computes the canonical migration checksum.</summary>
    public static ImmutableArray<byte> Checksum(BaseSemanticActivationMigrationDefinition value)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.definitionMigration.v1\0"u8);
        AddText(value.Id); AddInt(value.Version); AddText(value.From.Id); AddInt(value.From.Version); hash.AppendData(value.From.Checksum.AsSpan());
        AddText(value.To.Id); AddInt(value.To.Version); hash.AppendData(value.To.Checksum.AsSpan());
        return hash.GetHashAndReset().ToImmutableArray();
        void AddText(string text) { byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text); AddInt(bytes.Length); hash.AppendData(bytes); }
        void AddInt(int number) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, number); hash.AppendData(bytes); }
    }

    private static bool Valid(BaseSemanticActivationDefinitionKey value) => !string.IsNullOrWhiteSpace(value.Id)
        && value.Version > 0 && value.Checksum.Length == 32;
    private static BaseSemanticActivationDefinitionKey Clone(BaseSemanticActivationDefinitionKey value) => value with
    { Id = new string(value.Id.AsSpan()), Checksum = value.Checksum.ToArray().ToImmutableArray() };
}

/// <summary>Contains immutable published semantic definition-migration authority.</summary>
public sealed record BaseSemanticActivationDefinitionMigrationAuthority
{
    /// <summary>Gets the stable migration identifier.</summary>
    public required string MigrationId { get; init; }
    /// <summary>Gets the migration version.</summary>
    public required int MigrationVersion { get; init; }
    /// <summary>Gets the exact source definition.</summary>
    public required BaseSemanticActivationDefinitionKey From { get; init; }
    /// <summary>Gets the exact destination definition.</summary>
    public required BaseSemanticActivationDefinitionKey To { get; init; }
    /// <summary>Gets the captured live count.</summary>
    public required long ExpectedLiveCount { get; init; }
    /// <summary>Gets the captured retired count.</summary>
    public required long ExpectedRetiredCount { get; init; }
    /// <summary>Gets the captured compacted-absence count.</summary>
    public required long ExpectedAbsenceCount { get; init; }
    /// <summary>Gets the ordered negative-authority checksum.</summary>
    public required ImmutableArray<byte> OrderedNegativeAuthorityChecksum { get; init; }
    /// <summary>Gets the publication generation.</summary>
    public required long PublicationGeneration { get; init; }
    /// <summary>Gets the identified maintenance receipt checksum.</summary>
    public required ImmutableArray<byte> ReceiptChecksum { get; init; }
    /// <summary>Gets the canonical authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Computes canonical semantic migration-publication authority.</summary>
public static class BaseSemanticActivationMigrationAuthorityContract
{
    /// <summary>Computes the exact authority checksum.</summary>
    public static ImmutableArray<byte> Checksum(BaseSemanticActivationDefinitionMigrationAuthority value)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.migrationAuthority.v1\0"u8);
        Text(value.MigrationId); Int(value.MigrationVersion); Definition(value.From); Definition(value.To);
        Long(value.ExpectedLiveCount); Long(value.ExpectedRetiredCount); Long(value.ExpectedAbsenceCount);
        Bytes(value.OrderedNegativeAuthorityChecksum.AsSpan()); Long(value.PublicationGeneration); Bytes(value.ReceiptChecksum.AsSpan());
        return hash.GetHashAndReset().ToImmutableArray();
        void Text(string text) => Bytes(System.Text.Encoding.UTF8.GetBytes(text));
        void Bytes(ReadOnlySpan<byte> bytes) { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes); }
        void Int(int number) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, number); hash.AppendData(bytes); }
        void Long(long number) { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, number); hash.AppendData(bytes); }
        void Definition(BaseSemanticActivationDefinitionKey definition) { Text(definition.Id); Int(definition.Version); Bytes(definition.Checksum.AsSpan()); }
    }
}

/// <summary>Executes provider-owned semantic maintenance and private inspection.</summary>
public interface IBaseSemanticActivationAdministration
{
    /// <summary>Reads one bounded provider-private inspection page.</summary>
    ValueTask<BaseResult<BaseSemanticActivationProviderInspectionPage>> InspectAsync(
        BaseSemanticActivationProviderInspectionRequest request, CancellationToken cancellationToken);
    /// <summary>Executes or resumes one identified maintenance operation.</summary>
    ValueTask<BaseResult<BaseSemanticActivationMaintenanceResult>> ExecuteAsync(
        BaseSemanticActivationMaintenanceRequest request, CancellationToken cancellationToken);
    /// <summary>Resolves an indeterminate identified maintenance operation.</summary>
    ValueTask<BaseResult<BaseSemanticActivationMaintenanceResult>> ResolveAsync(
        BaseSemanticActivationMaintenanceResolutionRequest request, CancellationToken cancellationToken);
}

/// <summary>Base type for the closed semantic maintenance request union.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BaseSemanticActivationCompactRequest), "compact")]
[JsonDerivedType(typeof(BaseSemanticActivationMigrateRequest), "migrate")]
[JsonDerivedType(typeof(BaseSemanticActivationRemoveRequest), "remove")]
public abstract record BaseSemanticActivationMaintenanceRequest
{
    /// <summary>Gets the durable operation identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the exact target definition.</summary>
    public required BaseSemanticActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the expected semantic authority generation.</summary>
    public required long ExpectedSemanticAuthorityGeneration { get; init; }
    /// <summary>Gets effective bounded maintenance limits.</summary>
    public required BaseSemanticActivationMaintenanceLimits Limits { get; init; }
}

/// <summary>Requests bounded eligible retired-slot compaction.</summary>
public sealed record BaseSemanticActivationCompactRequest : BaseSemanticActivationMaintenanceRequest
{
    /// <summary>Gets the exact expected retired-row count.</summary>
    public required long ExpectedRetiredCount { get; init; }
    /// <summary>Gets the exact ordered retired-authority checksum.</summary>
    public required ImmutableArray<byte> ExpectedRetiredChecksum { get; init; }
}

/// <summary>Requests one graph-installed authority-only definition migration.</summary>
public sealed record BaseSemanticActivationMigrateRequest : BaseSemanticActivationMaintenanceRequest
{
    /// <summary>Gets the exact installed migration.</summary>
    public required BaseSemanticActivationMigrationDefinition Migration { get; init; }
}

/// <summary>Requests removal of one empty semantic definition.</summary>
public sealed record BaseSemanticActivationRemoveRequest : BaseSemanticActivationMaintenanceRequest
{
    /// <summary>Gets the expected live-row count.</summary>
    public required long ExpectedLiveCount { get; init; }
    /// <summary>Gets the expected retired-row count.</summary>
    public required long ExpectedRetiredCount { get; init; }
    /// <summary>Gets the expected compacted-absence count.</summary>
    public required long ExpectedAbsenceCount { get; init; }
    /// <summary>Gets the exact ordered definition-state checksum.</summary>
    public required ImmutableArray<byte> ExpectedDefinitionStateChecksum { get; init; }
}

/// <summary>Bounds one semantic maintenance execution.</summary>
public sealed record BaseSemanticActivationMaintenanceLimits
{
    /// <summary>Gets the page size, at most 256.</summary>
    public required int PageSize { get; init; }
    /// <summary>Gets the maximum pages.</summary>
    public required int MaximumPages { get; init; }
    /// <summary>Gets the maximum examined rows.</summary>
    public required long MaximumRows { get; init; }
    /// <summary>Gets the maximum canonical bytes.</summary>
    public required long MaximumBytes { get; init; }
    /// <summary>Gets the cooperative operation deadline.</summary>
    public required TimeSpan Deadline { get; init; }
}

/// <summary>Identifies one private semantic recovery ordering boundary.</summary>
public sealed record BaseSemanticActivationRecoveryBoundary
{
    /// <summary>Gets the stable definition identifier.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the stable scope-binding identifier.</summary>
    public required ImmutableArray<byte> ScopeBindingId { get; init; }
    /// <summary>Gets the semantic key digest.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
}

/// <summary>Contains resumable provider-owned maintenance evidence.</summary>
public sealed record BaseSemanticActivationMaintenanceCheckpoint
{
    /// <summary>Gets the provider maintenance identifier.</summary>
    public required string MaintenanceId { get; init; }
    /// <summary>Gets the closed operation kind.</summary>
    public required string OperationKind { get; init; }
    /// <summary>Gets the exact target definition.</summary>
    public required BaseSemanticActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the captured authority generation.</summary>
    public required long ExpectedAuthorityGeneration { get; init; }
    /// <summary>Gets the last completed boundary.</summary>
    public required BaseSemanticActivationRecoveryBoundary? After { get; init; }
    /// <summary>Gets completed pages.</summary>
    public required int CompletedPages { get; init; }
    /// <summary>Gets completed rows.</summary>
    public required long CompletedRows { get; init; }
    /// <summary>Gets completed canonical bytes.</summary>
    public required long CompletedBytes { get; init; }
    /// <summary>Gets the rolling checksum.</summary>
    public required ImmutableArray<byte> RollingChecksum { get; init; }
    /// <summary>Gets the request fingerprint.</summary>
    public required ImmutableArray<byte> RequestFingerprint { get; init; }
    /// <summary>Gets the checkpoint checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Classifies an identified semantic maintenance outcome.</summary>
public enum BaseSemanticActivationMaintenanceDisposition
{
    /// <summary>The operation completed.</summary>
    Completed = 1,
    /// <summary>The stored operation result was replayed.</summary>
    Duplicate = 2,
    /// <summary>More bounded pages remain.</summary>
    InProgress = 3,
    /// <summary>The current attempt was confirmed rolled back.</summary>
    ConfirmedRolledBack = 4,
    /// <summary>The outcome requires identified resolution.</summary>
    Indeterminate = 5,
}

/// <summary>Reports one semantic maintenance result.</summary>
public sealed record BaseSemanticActivationMaintenanceResult
{
    /// <summary>Gets the maintenance disposition.</summary>
    public required BaseSemanticActivationMaintenanceDisposition Disposition { get; init; }
    /// <summary>Gets the previous authority generation.</summary>
    public required long PreviousAuthorityGeneration { get; init; }
    /// <summary>Gets the resulting authority generation.</summary>
    public required long ResultingAuthorityGeneration { get; init; }
    /// <summary>Gets examined rows.</summary>
    public required long ExaminedRows { get; init; }
    /// <summary>Gets changed rows.</summary>
    public required long ChangedRows { get; init; }
    /// <summary>Gets canonical bytes.</summary>
    public required long CanonicalBytes { get; init; }
    /// <summary>Gets the result checksum.</summary>
    public required ImmutableArray<byte> ResultChecksum { get; init; }
    /// <summary>Gets resumable progress when more work remains.</summary>
    public BaseSemanticActivationMaintenanceCheckpoint? Checkpoint { get; init; }
    /// <summary>Gets the durable receipt disposition.</summary>
    public required BaseMutationRequestDisposition ReceiptDisposition { get; init; }
    /// <summary>Gets the commit-observation checksum.</summary>
    public required ImmutableArray<byte> CommitObservationChecksum { get; init; }
}

/// <summary>Requests identified resolution of semantic maintenance.</summary>
public sealed record BaseSemanticActivationMaintenanceResolutionRequest
{
    /// <summary>Gets the original operation identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the provider maintenance identifier.</summary>
    public required string MaintenanceId { get; init; }
    /// <summary>Gets the exact original request fingerprint.</summary>
    public required ImmutableArray<byte> RequestFingerprint { get; init; }
    /// <summary>Gets the resolution deadline.</summary>
    public required TimeSpan Deadline { get; init; }
}

/// <summary>Contains one decoded private provider inspection boundary.</summary>
public sealed record BaseSemanticActivationProviderInspectionBoundary
{
    /// <summary>Gets the stable definition identifier.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the stable scope-binding identifier.</summary>
    public required ImmutableArray<byte> ScopeBindingId { get; init; }
    /// <summary>Gets the semantic key digest.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets the captured authority generation.</summary>
    public required long CapturedAuthorityGeneration { get; init; }
    /// <summary>Gets the Runtime-authored boundary checksum.</summary>
    public required ImmutableArray<byte> RuntimeBoundaryChecksum { get; init; }
}

/// <summary>Requests one provider-private semantic inspection page.</summary>
public sealed record BaseSemanticActivationProviderInspectionRequest
{
    /// <summary>Gets the exact definition.</summary>
    public required BaseSemanticActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets an optional state filter.</summary>
    public required BaseSemanticActivationSlotState? State { get; init; }
    /// <summary>Gets the decoded continuation boundary.</summary>
    public required BaseSemanticActivationProviderInspectionBoundary? After { get; init; }
    /// <summary>Gets the requested page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets effective execution limits.</summary>
    public required BaseSemanticActivationExecutionLimits Limits { get; init; }
    /// <summary>Gets Runtime request authority.</summary>
    public required ImmutableArray<byte> RuntimeRequestAuthorityChecksum { get; init; }
}

/// <summary>Contains one private provider inspection item.</summary>
public sealed record BaseSemanticActivationProviderInspectionItem
{
    /// <summary>Gets the current state.</summary>
    public required BaseSemanticActivationSlotState State { get; init; }
    /// <summary>Gets the current slot generation.</summary>
    public required long SlotGeneration { get; init; }
    /// <summary>Gets the private ordering boundary.</summary>
    public required BaseSemanticActivationProviderInspectionBoundary Boundary { get; init; }
    /// <summary>Gets the retirement position when terminal.</summary>
    public required long? RetirementPosition { get; init; }
    /// <summary>Gets the exact stored-state checksum.</summary>
    public required ImmutableArray<byte> StateChecksum { get; init; }
}

/// <summary>Contains one private provider inspection page.</summary>
public sealed record BaseSemanticActivationProviderInspectionPage
{
    /// <summary>Gets ordered private items.</summary>
    public required ImmutableArray<BaseSemanticActivationProviderInspectionItem> Items { get; init; }
    /// <summary>Gets the next private boundary.</summary>
    public required BaseSemanticActivationProviderInspectionBoundary? Next { get; init; }
    /// <summary>Gets the captured semantic authority generation.</summary>
    public required long CapturedAuthorityGeneration { get; init; }
    /// <summary>Gets exact read-interval evidence.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets canonical accounting.</summary>
    public required BaseSemanticActivationAccounting Accounting { get; init; }
    /// <summary>Gets the page checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}
