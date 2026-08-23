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
    /// <summary>Gets the immutable certified semantic activation capability implemented by this administration seam.</summary>
    BaseSemanticActivationCapability SemanticActivationCapability { get; }
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
    /// <summary>Gets the exact graph-replacement authority that retired executable definition authority.</summary>
    public required BaseSemanticActivationRemovalAuthority RemovalAuthority { get; init; }
    /// <summary>Gets the expected live-row count.</summary>
    public required long ExpectedLiveCount { get; init; }
    /// <summary>Gets the expected retired-row count.</summary>
    public required long ExpectedRetiredCount { get; init; }
    /// <summary>Gets the expected compacted-absence count.</summary>
    public required long ExpectedAbsenceCount { get; init; }
    /// <summary>Gets the exact ordered definition-state checksum.</summary>
    public required ImmutableArray<byte> ExpectedDefinitionStateChecksum { get; init; }
    /// <summary>Gets the exact ordered byte-exact absence authority checksum retained after removal.</summary>
    public required ImmutableArray<byte> ExpectedAbsenceAuthorityChecksum { get; init; }
}

/// <summary>Graph-installed authority to retire one definition's executable surface without deleting historical authority.</summary>
public sealed record BaseSemanticActivationRemovalAuthority
{
    /// <summary>Gets the stable identified removal transition.</summary>
    public required string Id { get; init; }
    /// <summary>Gets its positive version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the complete retained source definition.</summary>
    public required BaseSemanticActivationKeyDefinition From { get; init; }
    /// <summary>Gets the checksum of the replacement executable definition set.</summary>
    public required ImmutableArray<byte> ResultingDefinitionSetChecksum { get; init; }
    /// <summary>Gets the canonical graph-owned authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Seals graph-owned semantic executable-authority retirement.</summary>
public static class BaseSemanticActivationRemovalAuthorityContract
{
    /// <summary>Returns a deeply owned, canonically checksummed removal authority.</summary>
    public static BaseSemanticActivationRemovalAuthority Seal(BaseSemanticActivationRemovalAuthority value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value.Id) || value.Version <= 0 || value.From.Checksum.Length != 32
            || value.ResultingDefinitionSetChecksum.Length != 32)
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        BaseSemanticActivationRemovalAuthority owned = value with
        {
            Id = new string(value.Id.AsSpan()), From = BaseSemanticActivationDefinitionContract.Seal(value.From),
            ResultingDefinitionSetChecksum = value.ResultingDefinitionSetChecksum.ToArray().ToImmutableArray(), Checksum = [],
        };
        ImmutableArray<byte> checksum = ComputeChecksum(owned);
        if (!value.Checksum.IsDefaultOrEmpty && !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(), checksum.AsSpan()))
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        return owned with { Checksum = checksum };
    }

    /// <summary>Computes the exact canonical authority checksum.</summary>
    public static ImmutableArray<byte> ComputeChecksum(BaseSemanticActivationRemovalAuthority value)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.removalAuthority.v1\0"u8);
        Add(System.Text.Encoding.UTF8.GetBytes(value.Id)); Int(value.Version); Add(System.Text.Encoding.UTF8.GetBytes(value.From.Id));
        Int(value.From.Version); Add(value.From.Checksum.AsSpan()); Add(value.ResultingDefinitionSetChecksum.AsSpan());
        return hash.GetHashAndReset().ToImmutableArray();
        void Add(ReadOnlySpan<byte> bytes) { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes); }
        void Int(int number) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, number); hash.AppendData(bytes); }
    }
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
    /// <summary>Gets the exact ordered authority checksum over the processed rows.</summary>
    public required ImmutableArray<byte> AuthorityChecksum { get; init; }
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
    /// <summary>Gets the exact installed definition whose maintenance receipt is resolved.</summary>
    public required BaseSemanticActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the original operation identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the provider maintenance identifier.</summary>
    public required string MaintenanceId { get; init; }
    /// <summary>Gets the exact original request fingerprint.</summary>
    public required ImmutableArray<byte> RequestFingerprint { get; init; }
    /// <summary>Gets the resolution deadline.</summary>
    public required TimeSpan Deadline { get; init; }
}

/// <summary>Owns canonical semantic-maintenance request, checkpoint, result, and receipt authority.</summary>
public static class BaseSemanticActivationMaintenanceContract
{
    /// <summary>Computes the exact structural request fingerprint.</summary>
    public static ImmutableArray<byte> RequestFingerprint(BaseSemanticActivationMaintenanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var writer = new CanonicalWriter("base.semanticActivation.maintenanceRequest.v1\0");
        writer.Text(request.Identity.Scope); writer.Text(request.Identity.Operation); writer.Text(request.Identity.IdempotencyKey);
        writer.Bytes(request.Identity.Fingerprint.ToArray());
        writer.I32(request switch { BaseSemanticActivationCompactRequest => 1, BaseSemanticActivationMigrateRequest => 2, BaseSemanticActivationRemoveRequest => 3, _ => 0 });
        writer.Definition(request.Definition); writer.I64(request.ExpectedSemanticAuthorityGeneration);
        writer.I32(request.Limits.PageSize); writer.I32(request.Limits.MaximumPages); writer.I64(request.Limits.MaximumRows);
        writer.I64(request.Limits.MaximumBytes); writer.I64(request.Limits.Deadline.Ticks);
        switch (request)
        {
            case BaseSemanticActivationCompactRequest compact:
                writer.I64(compact.ExpectedRetiredCount); writer.Bytes(compact.ExpectedRetiredChecksum.AsSpan()); break;
            case BaseSemanticActivationMigrateRequest migrate:
                writer.Text(migrate.Migration.Id); writer.I32(migrate.Migration.Version); writer.Bytes(migrate.Migration.Checksum.AsSpan());
                writer.Definition(migrate.Migration.From); writer.Definition(migrate.Migration.To); break;
            case BaseSemanticActivationRemoveRequest remove:
                writer.I64(remove.ExpectedLiveCount); writer.I64(remove.ExpectedRetiredCount); writer.I64(remove.ExpectedAbsenceCount);
                writer.Bytes(remove.ExpectedDefinitionStateChecksum.AsSpan()); writer.Bytes(remove.ExpectedAbsenceAuthorityChecksum.AsSpan()); writer.Text(remove.RemovalAuthority.Id);
                writer.I32(remove.RemovalAuthority.Version); writer.Bytes(remove.RemovalAuthority.Checksum.AsSpan());
                writer.Bytes(remove.RemovalAuthority.ResultingDefinitionSetChecksum.AsSpan()); break;
        }
        return writer.Finish();
    }

    /// <summary>Computes the exact completed-result checksum.</summary>
    public static ImmutableArray<byte> ResultChecksum(BaseSemanticActivationMaintenanceResult result, ReadOnlySpan<byte> authority)
    {
        using var writer = new CanonicalWriter("base.semanticActivation.maintenanceResult.v1\0");
        writer.I64(result.PreviousAuthorityGeneration); writer.I64(result.ResultingAuthorityGeneration);
        writer.I64(result.ExaminedRows); writer.I64(result.ChangedRows); writer.I64(result.CanonicalBytes); writer.Bytes(authority);
        return writer.Finish();
    }

    /// <summary>Computes the exact commit-observation checksum.</summary>
    public static ImmutableArray<byte> CommitObservationChecksum(ReadOnlySpan<byte> resultChecksum) =>
        System.Security.Cryptography.SHA256.HashData(resultChecksum).ToImmutableArray();

    /// <summary>Computes the exact resumable-checkpoint checksum.</summary>
    public static ImmutableArray<byte> CheckpointChecksum(BaseSemanticActivationMaintenanceCheckpoint value)
    {
        using var writer = new CanonicalWriter("base.semanticActivation.maintenanceCheckpoint.v1\0");
        writer.Text(value.MaintenanceId); writer.Text(value.OperationKind); writer.Definition(value.Definition);
        writer.I64(value.ExpectedAuthorityGeneration); writer.Bytes(value.After is null ? [] : value.After.ScopeBindingId.AsSpan());
        writer.Bytes(value.After is null ? [] : value.After.Key.ToArray()); writer.I32(value.CompletedPages);
        writer.I64(value.CompletedRows); writer.I64(value.CompletedBytes); writer.Bytes(value.RollingChecksum.AsSpan());
        writer.Bytes(value.RequestFingerprint.AsSpan()); return writer.Finish();
    }

    /// <summary>Validates one provider result against the exact request authority.</summary>
    public static bool IsValid(BaseSemanticActivationMaintenanceRequest request, BaseSemanticActivationMaintenanceResult result)
    {
        if (!Enum.IsDefined(result.Disposition) || result.PreviousAuthorityGeneration != request.ExpectedSemanticAuthorityGeneration
            || result.ResultingAuthorityGeneration < result.PreviousAuthorityGeneration || result.ExaminedRows < 0
            || result.ChangedRows < 0 || result.ChangedRows > result.ExaminedRows || result.CanonicalBytes < 0
            || result.ExaminedRows > request.Limits.MaximumRows || result.CanonicalBytes > request.Limits.MaximumBytes) return false;
        if (result.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress)
        {
            BaseSemanticActivationMaintenanceCheckpoint? checkpoint = result.Checkpoint;
            return checkpoint is not null && checkpoint.ExpectedAuthorityGeneration == request.ExpectedSemanticAuthorityGeneration
                && checkpoint.CompletedPages is > 0 && checkpoint.CompletedPages <= request.Limits.MaximumPages
                && checkpoint.CompletedRows == result.ExaminedRows && checkpoint.CompletedBytes == result.CanonicalBytes
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(checkpoint.RequestFingerprint.AsSpan(), RequestFingerprint(request).AsSpan())
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(checkpoint.Checksum.AsSpan(), CheckpointChecksum(checkpoint).AsSpan());
        }
        return result.Checkpoint is null && result.AuthorityChecksum.Length == 32 && result.ResultChecksum.Length == 32 && result.CommitObservationChecksum.Length == 32
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(result.ResultChecksum.AsSpan(), ResultChecksum(result, result.AuthorityChecksum.AsSpan()).AsSpan())
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(result.CommitObservationChecksum.AsSpan(), CommitObservationChecksum(result.ResultChecksum.AsSpan()).AsSpan());
    }

    private sealed class CanonicalWriter : IDisposable
    {
        private readonly System.Security.Cryptography.IncrementalHash hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        internal CanonicalWriter(string purpose) => hash.AppendData(System.Text.Encoding.UTF8.GetBytes(purpose));
        internal void Bytes(ReadOnlySpan<byte> value) { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length); hash.AppendData(value); }
        internal void Text(string value) => Bytes(System.Text.Encoding.UTF8.GetBytes(value));
        internal void I32(int value) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); hash.AppendData(bytes); }
        internal void I64(long value) { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes); }
        internal void Definition(BaseSemanticActivationDefinitionKey value) { Text(value.Id); I32(value.Version); Bytes(value.Checksum.AsSpan()); }
        internal ImmutableArray<byte> Finish() => hash.GetHashAndReset().ToImmutableArray();
        public void Dispose() => hash.Dispose();
    }
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
    /// <summary>Gets the exact application authority.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the exact logical store authority.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the captured restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
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
    /// <summary>Gets the exact provider-private canonical state authority for Runtime validation.</summary>
    public required ImmutableArray<byte> CanonicalStateAuthority { get; init; }
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

/// <summary>Opaque ControlPlane-only semantic activation inspection continuation.</summary>
public sealed class BaseSemanticActivationInspectionToken
{
    internal BaseSemanticActivationInspectionToken(string value) => Value = value;
    internal static BaseSemanticActivationInspectionToken FromWire(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > 2048 || value.Any(static character =>
                character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z')
                    and not (>= '0' and <= '9') and not '-' and not '_'))
            throw new FormatException(BaseSemanticActivationErrorCodes.Invalid);
        return new BaseSemanticActivationInspectionToken(new string(value.AsSpan()));
    }
    /// <summary>Gets the canonical unpadded base64url token text.</summary>
    public string Value { get; }
}

/// <summary>Requests one sanitized semantic activation inspection page.</summary>
public sealed record BaseSemanticActivationInspectionRequest
{
    /// <summary>Gets the exact installed definition.</summary>
    public required BaseSemanticActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the optional exact state filter.</summary>
    public required BaseSemanticActivationSlotState? State { get; init; }
    /// <summary>Gets the opaque continuation from the preceding page.</summary>
    public required BaseSemanticActivationInspectionToken? After { get; init; }
    /// <summary>Gets the requested page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the effective bounded execution limits.</summary>
    public required BaseSemanticActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains one sanitized semantic activation inspection item.</summary>
public sealed record BaseSemanticActivationInspectionItem
{
    /// <summary>Gets the current closed slot state.</summary>
    public required BaseSemanticActivationSlotState State { get; init; }
    /// <summary>Gets the positive slot generation.</summary>
    public required long SlotGeneration { get; init; }
    /// <summary>Gets an opaque item identity usable only for ControlPlane correlation.</summary>
    public required BaseSemanticActivationInspectionToken ItemToken { get; init; }
    /// <summary>Gets the retirement position for terminal states.</summary>
    public required long? RetirementPosition { get; init; }
    /// <summary>Gets the sanitized state checksum.</summary>
    public required ImmutableArray<byte> SanitizedChecksum { get; init; }
}

/// <summary>Contains one sanitized semantic activation inspection page.</summary>
public sealed record BaseSemanticActivationInspectionPage
{
    /// <summary>Gets ordered sanitized items.</summary>
    public required ImmutableArray<BaseSemanticActivationInspectionItem> Items { get; init; }
    /// <summary>Gets the next opaque continuation.</summary>
    public required BaseSemanticActivationInspectionToken? Next { get; init; }
    /// <summary>Gets the captured semantic authority generation.</summary>
    public required long CapturedAuthorityGeneration { get; init; }
    /// <summary>Gets normalized covering read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseSemanticActivationAccounting Accounting { get; init; }
    /// <summary>Gets the Runtime-authored sanitized page checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Owns canonical semantic inspection request and provider-evidence checksums.</summary>
public static class BaseSemanticActivationInspectionContract
{
    /// <summary>Computes the exact Runtime request authority checksum.</summary>
    public static ImmutableArray<byte> RequestChecksum(BaseSemanticActivationProviderInspectionRequest value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var writer = new CanonicalWriter("base.semanticActivation.inspectionRequest.v1\0");
        writer.Text(value.ApplicationId); writer.Text(value.LogicalStoreId); writer.I64(value.RestoreEpoch);
        writer.Definition(value.Definition); writer.OptionalEnum(value.State); writer.I32(value.Take); writer.Limits(value.Limits);
        return writer.Finish();
    }

    /// <summary>Computes one private provider boundary checksum.</summary>
    public static ImmutableArray<byte> BoundaryChecksum(BaseSemanticActivationProviderInspectionRequest request,
        ReadOnlySpan<byte> bindingId, BaseSemanticActivationKeyDigest key, long generation)
    {
        Span<byte> keyBytes = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; key.CopyTo(keyBytes);
        using var writer = new CanonicalWriter("base.semanticActivation.inspectionBoundary.v1\0");
        writer.Bytes(request.RuntimeRequestAuthorityChecksum.AsSpan()); writer.Text(request.Definition.Id);
        writer.Bytes(bindingId); writer.Bytes(keyBytes); writer.I64(generation); return writer.Finish();
    }

    /// <summary>Computes the exact provider page checksum.</summary>
    public static ImmutableArray<byte> PageChecksum(BaseSemanticActivationProviderInspectionRequest request,
        BaseSemanticActivationProviderInspectionPage page)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.inspectionPage.v1\0"u8); hash.AppendData(request.RuntimeRequestAuthorityChecksum.AsSpan());
        Span<byte> number = stackalloc byte[8];
        foreach (BaseSemanticActivationProviderInspectionItem item in page.Items)
        {
            hash.AppendData(item.Boundary.RuntimeBoundaryChecksum.AsSpan()); hash.AppendData(item.StateChecksum.AsSpan());
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(number, item.SlotGeneration); hash.AppendData(number);
            hash.AppendData([(byte)item.State]);
        }
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private sealed class CanonicalWriter : IDisposable
    {
        private readonly System.Security.Cryptography.IncrementalHash hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        internal CanonicalWriter(string purpose) => hash.AppendData(System.Text.Encoding.UTF8.GetBytes(purpose));
        internal void Bytes(ReadOnlySpan<byte> value) { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length); hash.AppendData(value); }
        internal void Text(string value) => Bytes(System.Text.Encoding.UTF8.GetBytes(value));
        internal void I32(int value) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); hash.AppendData(bytes); }
        internal void I64(long value) { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes); }
        internal void OptionalEnum(BaseSemanticActivationSlotState? value) { hash.AppendData(value is null ? [0] : [1]); if (value is not null) I32((int)value.Value); }
        internal void Definition(BaseSemanticActivationDefinitionKey value) { Text(value.Id); I32(value.Version); Bytes(value.Checksum.AsSpan()); }
        internal void Limits(BaseSemanticActivationExecutionLimits value)
        {
            I32(value.MaximumOperations); I32(value.MaximumScopeDirectoryReads); I32(value.MaximumSlotReads);
            I32(value.MaximumActivationReads); I32(value.MaximumReadIntervals); I32(value.MaximumIndexOperations);
            I64(value.MaximumActivationBytes); I64(value.MaximumScopeDirectoryBytes); I64(value.MaximumEvidenceBytes);
            I64(value.MaximumReceiptBytes); I64(value.MaximumTransientBytes);
        }
        internal ImmutableArray<byte> Finish() => hash.GetHashAndReset().ToImmutableArray();
        public void Dispose() => hash.Dispose();
    }
}
