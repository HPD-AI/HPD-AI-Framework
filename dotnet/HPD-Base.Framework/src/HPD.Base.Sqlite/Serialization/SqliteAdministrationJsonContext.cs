using System.Text.Json.Serialization;

namespace HPD.Base.Sqlite;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(BaseBackupManifest))]
[JsonSerializable(typeof(SqliteRestoreMarker))]
internal sealed partial class SqliteAdministrationJsonContext : JsonSerializerContext;

internal sealed record SqliteRestoreMarker
{
    public required int Version { get; init; }
    public required string State { get; init; }
    public required string StagingName { get; init; }
    public required string RecoveryName { get; init; }
    public required string CurrentIdentityDigest { get; init; }
    public required string ArtifactIdentityDigest { get; init; }
    public required string Checksum { get; init; }
}
