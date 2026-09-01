using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class BaseStudioInfrastructureInventoryContract
{
    internal const string SchemaPath = "base.studio.infrastructure.schema.v1";
    internal const string MigrationPath = "base.studio.infrastructure.migration.v1";
    internal const string BackupPath = "base.studio.infrastructure.backup.v1";
    internal const string RestorePath = "base.studio.infrastructure.restore.v1";
    internal const string MaintenancePath = "base.studio.infrastructure.maintenance.v1";
    internal static readonly ImmutableArray<BaseStudioInfrastructureInventoryKind> AllKinds =
        [BaseStudioInfrastructureInventoryKind.SchemaGeneration, BaseStudioInfrastructureInventoryKind.Migration,
         BaseStudioInfrastructureInventoryKind.Backup, BaseStudioInfrastructureInventoryKind.Restore,
         BaseStudioInfrastructureInventoryKind.Maintenance];

    internal static BaseStudioInfrastructureInventoryCapability Capability(bool durable) => new()
    {
        SupportedKinds = AllKinds, MaximumItems = 256, MaximumRowsRead = 257,
        MaximumEvidenceBytes = 1_048_576, MaximumTransientBytes = 2_097_152,
        AcquisitionDeadline = TimeSpan.FromSeconds(2), SessionDeadline = TimeSpan.FromSeconds(30),
        PageDeadline = TimeSpan.FromSeconds(2), DurableThroughBackupRestore = durable,
        CertificationChecksum = Hash(writer => { writer.Write("base.studio.infrastructure.capability.v1"); writer.Write(durable); }),
    };

    internal static bool Valid(BaseStudioInfrastructureInventoryRequirement value, BaseStudioInfrastructureInventoryCapability capability) =>
        value is not null && !string.IsNullOrWhiteSpace(value.ApplicationId) && !string.IsNullOrWhiteSpace(value.StoreId) &&
        !string.IsNullOrWhiteSpace(value.StoreInstanceId) && value.RestoreEpoch >= 0 && value.SchemaGeneration >= 0 &&
        Enum.IsDefined(value.Kind) && capability.SupportedKinds.Contains(value.Kind) && Valid(value.Limits) &&
        value.Limits.MaximumItems <= capability.MaximumItems && value.Limits.MaximumRowsRead <= capability.MaximumRowsRead &&
        value.Limits.MaximumEvidenceBytes <= capability.MaximumEvidenceBytes && value.Limits.MaximumTransientBytes <= capability.MaximumTransientBytes &&
        value.Limits.AcquisitionDeadline <= capability.AcquisitionDeadline && value.Limits.SessionDeadline <= capability.SessionDeadline &&
        value.Limits.PageDeadline <= capability.PageDeadline;

    private static bool Valid(BaseStudioInfrastructureInventoryLimits value) => value is not null && value.MaximumItems > 0 &&
        value.MaximumRowsRead > 0 && value.MaximumEvidenceBytes > 0 && value.MaximumTransientBytes > 0 &&
        value.AcquisitionDeadline > TimeSpan.Zero && value.SessionDeadline > TimeSpan.Zero && value.PageDeadline > TimeSpan.Zero;

    internal static string Path(BaseStudioInfrastructureInventoryKind kind) => kind switch
    {
        BaseStudioInfrastructureInventoryKind.SchemaGeneration => SchemaPath,
        BaseStudioInfrastructureInventoryKind.Migration => MigrationPath,
        BaseStudioInfrastructureInventoryKind.Backup => BackupPath,
        BaseStudioInfrastructureInventoryKind.Restore => RestorePath,
        BaseStudioInfrastructureInventoryKind.Maintenance => MaintenancePath,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static ImmutableArray<byte> AuthorityChecksum(BaseStudioInfrastructureInventoryRequirement request,
        long inventoryGeneration, string path) => Hash(writer =>
    {
        writer.Write("base.studio.infrastructure.authority.v1"); Writer(writer, request.ApplicationId); Writer(writer, request.StoreId);
        writer.Write((byte)request.Kind); Writer(writer, request.StoreInstanceId); writer.Write(request.RestoreEpoch);
        writer.Write(request.SchemaGeneration); writer.Write(inventoryGeneration); Writer(writer, path);
        writer.Write(request.Limits.MaximumItems); writer.Write(request.Limits.MaximumRowsRead);
        writer.Write(request.Limits.MaximumEvidenceBytes); writer.Write(request.Limits.MaximumTransientBytes);
        writer.Write(request.Limits.AcquisitionDeadline.Ticks); writer.Write(request.Limits.SessionDeadline.Ticks); writer.Write(request.Limits.PageDeadline.Ticks);
    });

    internal static BaseStudioInfrastructureBoundary Boundary(BaseStudioInfrastructureInventoryKind kind, long sequence) => new()
    { Kind = kind, Sequence = sequence, Checksum = Hash(writer => { writer.Write("base.studio.infrastructure.boundary.v1"); writer.Write((byte)kind); writer.Write(sequence); }) };

    internal static bool Position(BaseStudioInfrastructureInventoryKind kind, BaseStudioInfrastructureBoundary? value, out long sequence)
    {
        sequence = 0; if (value is null) return true; if (value.Kind != kind || value.Sequence <= 0 || value.Checksum.Length != 32) return false;
        BaseStudioInfrastructureBoundary expected = Boundary(kind, value.Sequence);
        if (!CryptographicOperations.FixedTimeEquals(expected.Checksum.AsSpan(), value.Checksum.AsSpan())) return false;
        sequence = value.Sequence; return true;
    }

    internal static ImmutableArray<byte> ItemChecksum(BaseStudioInfrastructureItem item) => Hash(writer =>
    {
        writer.Write("base.studio.infrastructure.item.v1"); writer.Write((byte)item.Kind); writer.Write(item.Sequence); Writer(writer, item.StoreId);
        writer.Write(item.RestoreEpoch); writer.Write(item.SchemaGeneration); writer.Write(item.ObservedAtUtc.UtcTicks); writer.Write((byte)item.State);
        switch (item)
        {
            case BaseStudioSchemaGenerationItem x: Writer(writer, x.BaselineId); Bytes(writer, x.SchemaChecksum); writer.Write(x.DriftDetected); break;
            case BaseStudioMigrationItem x: Writer(writer, x.MigrationId); writer.Write(x.FromSchemaGeneration); writer.Write(x.ToSchemaGeneration); Bytes(writer, x.PlanChecksum); break;
            case BaseStudioBackupItem x: Writer(writer, x.ArtifactId); Bytes(writer, x.ArtifactDigest); writer.Write(x.ArtifactBytes); break;
            case BaseStudioRestoreItem x: Writer(writer, x.RestoreRequestIdentity); Bytes(writer, x.ArtifactDigest); writer.Write(x.ResultRestoreEpoch); break;
            case BaseStudioMaintenanceItem x: Writer(writer, x.MaintenanceKind); Writer(writer, x.OperationIdentity); writer.Write(x.ProgressBasisPoints); break;
            default: throw new ArgumentOutOfRangeException(nameof(item));
        }
    });

    internal static long Measure(BaseStudioInfrastructureItem item) => 96 + Encoding.UTF8.GetByteCount(item.StoreId) + (item switch
    { BaseStudioSchemaGenerationItem x => Encoding.UTF8.GetByteCount(x.BaselineId) + x.SchemaChecksum.Length,
      BaseStudioMigrationItem x => Encoding.UTF8.GetByteCount(x.MigrationId) + x.PlanChecksum.Length,
      BaseStudioBackupItem x => Encoding.UTF8.GetByteCount(x.ArtifactId) + x.ArtifactDigest.Length,
      BaseStudioRestoreItem x => Encoding.UTF8.GetByteCount(x.RestoreRequestIdentity) + x.ArtifactDigest.Length,
      BaseStudioMaintenanceItem x => Encoding.UTF8.GetByteCount(x.MaintenanceKind) + Encoding.UTF8.GetByteCount(x.OperationIdentity), _ => 0 });

    internal static ImmutableArray<byte> PageChecksum(ImmutableArray<BaseStudioInfrastructureItem> items, long generation,
        BaseStudioInfrastructureBoundary? next, BaseStudioInfrastructureProviderAccounting accounting) => Hash(writer =>
    {
        writer.Write("base.studio.infrastructure.page.v1"); writer.Write(generation); writer.Write(items.Length);
        foreach (BaseStudioInfrastructureItem item in items) Bytes(writer, item.Checksum);
        writer.Write(next is not null); if (next is not null) Bytes(writer, next.Checksum);
        writer.Write(accounting.RowsRead); writer.Write(accounting.EvidenceBytes); writer.Write(accounting.TransientBytes);
    });

    internal static ImmutableArray<byte> Hash(Action<BinaryWriter> write)
    { using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) write(writer); return [.. SHA256.HashData(stream.ToArray())]; }
    private static void Writer(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
    private static void Bytes(BinaryWriter writer, ImmutableArray<byte> value) { writer.Write(value.Length); writer.Write(value.AsSpan()); }
}
