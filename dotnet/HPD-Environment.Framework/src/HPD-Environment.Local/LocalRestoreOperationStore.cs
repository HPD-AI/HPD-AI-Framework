namespace HPD.Environment.Local;

using System.Globalization;
using System.Text;
using HPD.Environment.Contracts;

internal enum LocalRestoreCheckpoint
{
    Staging = 1,
    Staged = 2,
    PreviousMoved = 3,
    Selected = 4,
    IdentityAdvanced = 5,
    Verified = 6,
}

internal sealed record LocalRestoreOperation(
    string RestoreId,
    string RestoreScope,
    long RestoreGeneration,
    string BackupId,
    string BackupScope,
    long BackupGeneration,
    string TargetResourceId,
    string TargetScope,
    long TargetResourceGeneration,
    string LogicalVolumeId,
    long PreviousVolumeGeneration,
    long RestoredVolumeGeneration,
    string ExpectedDigest,
    bool PreservePrevious,
    LocalRestoreCheckpoint Checkpoint);

internal sealed class LocalRestoreOperationStore
{
    private const string Schema =
        "hpd.environment.local-restore-operation/v1";
    private const int MaximumRecordBytes = 64 * 1024;
    private const int MaximumRecords = 1024;
    private const int FieldCount = 16;
    private readonly string _root;

    public LocalRestoreOperationStore(string storageRoot)
    {
        _root = Path.Combine(storageRoot, "restore-operations");
        Directory.CreateDirectory(_root);
    }

    public void Write(LocalRestoreOperation operation)
    {
        ValidateComponent(operation.RestoreId);
        string[] fields =
        [
            Schema,
            ((int)operation.Checkpoint).ToString(
                CultureInfo.InvariantCulture),
            Encode(operation.RestoreId),
            Encode(operation.RestoreScope),
            Number(operation.RestoreGeneration),
            Encode(operation.BackupId),
            Encode(operation.BackupScope),
            Number(operation.BackupGeneration),
            Encode(operation.TargetResourceId),
            Encode(operation.TargetScope),
            Number(operation.TargetResourceGeneration),
            Encode(operation.LogicalVolumeId),
            Number(operation.PreviousVolumeGeneration),
            Number(operation.RestoredVolumeGeneration),
            Encode(operation.ExpectedDigest),
            operation.PreservePrevious ? "1" : "0",
        ];
        byte[] bytes = Encoding.UTF8.GetBytes(
            string.Join('\n', fields));
        if (bytes.Length > MaximumRecordBytes)
            throw Invalid("restore operation record exceeds its bound");
        string path = PathFor(operation.RestoreId);
        string temporary = Path.Combine(
            _root,
            $".{operation.RestoreId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public LocalRestoreOperation ReadAndValidate(
        ResourceMetadata<VolumeRestore> metadata,
        VolumeRestoreSpec spec,
        string logicalVolumeId,
        string expectedDigest,
        ResourceGeneration previousVolumeGeneration)
    {
        LocalRestoreOperation operation = Read(metadata.Id.Value);
        bool matches =
            operation.RestoreId == metadata.Id.Value &&
            operation.RestoreScope == metadata.Scope.Value &&
            operation.RestoreGeneration == metadata.Generation.Value &&
            operation.BackupId == spec.Backup.Id.Value &&
            operation.BackupScope == spec.Backup.Scope.Value &&
            operation.BackupGeneration == (spec.Backup.Generation?.Value ?? 0) &&
            operation.TargetResourceId == spec.TargetVolume.Id.Value &&
            operation.TargetScope == spec.TargetVolume.Scope.Value &&
            operation.TargetResourceGeneration == (spec.TargetVolume.Generation?.Value ?? 0) &&
            operation.LogicalVolumeId == logicalVolumeId &&
            operation.PreviousVolumeGeneration == previousVolumeGeneration.Value &&
            operation.RestoredVolumeGeneration == checked(previousVolumeGeneration.Value + 1) &&
            operation.ExpectedDigest == expectedDigest &&
            operation.PreservePrevious == spec.PreservePreviousGenerationUntilVerified;
        if (!matches)
            throw Invalid(
                "restore operation record does not match authoritative restore ownership");
        return operation;
    }

    public IReadOnlyList<LocalRestoreOperation> FindForVolume(
        string logicalVolumeId)
    {
        string[] paths = Directory.EnumerateFiles(
                _root,
                "*.restore",
                SearchOption.TopDirectoryOnly)
            .Take(MaximumRecords + 1)
            .ToArray();
        if (paths.Length > MaximumRecords)
            throw Invalid("restore operation count exceeds its bound");
        return paths
            .Select(path => Read(
                Path.GetFileNameWithoutExtension(path)))
            .Where(operation =>
                operation.LogicalVolumeId == logicalVolumeId)
            .OrderBy(operation => operation.RestoreId, StringComparer.Ordinal)
            .ToArray();
    }

    public void Delete(string restoreId)
    {
        string path = PathFor(restoreId);
        if (File.Exists(path))
            File.Delete(path);
    }

    public bool Exists(string restoreId) =>
        File.Exists(PathFor(restoreId));

    private LocalRestoreOperation Read(string restoreId)
    {
        string path = PathFor(restoreId);
        var info = new FileInfo(path);
        if (!info.Exists ||
            info.LinkTarget is not null ||
            info.Length <= 0 ||
            info.Length > MaximumRecordBytes)
            throw Invalid("restore operation record is missing or malformed");
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(
                File.ReadAllBytes(path));
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid(
                "restore operation record is not valid UTF-8",
                exception);
        }
        string[] fields = text.Split('\n');
        if (fields.Length != FieldCount || fields[0] != Schema)
            throw Invalid("restore operation schema is unsupported");
        int checkpointValue = checked((int)Parse(fields[1]));
        if (!Enum.IsDefined((LocalRestoreCheckpoint)checkpointValue))
            throw Invalid("restore operation checkpoint is invalid");
        bool preserve = fields[15] switch
        {
            "0" => false,
            "1" => true,
            _ => throw Invalid(
                "restore operation retention value is invalid"),
        };
        var operation = new LocalRestoreOperation(
            Decode(fields[2]),
            Decode(fields[3]),
            Parse(fields[4]),
            Decode(fields[5]),
            Decode(fields[6]),
            Parse(fields[7]),
            Decode(fields[8]),
            Decode(fields[9]),
            Parse(fields[10]),
            Decode(fields[11]),
            Parse(fields[12]),
            Parse(fields[13]),
            Decode(fields[14]),
            preserve,
            (LocalRestoreCheckpoint)checkpointValue);
        if (operation.RestoredVolumeGeneration !=
            checked(operation.PreviousVolumeGeneration + 1))
            throw Invalid("restore operation generations are not monotonic");
        return operation;
    }

    private string PathFor(string restoreId)
    {
        ValidateComponent(restoreId);
        return Path.Combine(_root, restoreId + ".restore");
    }

    private static string Encode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096)
            throw Invalid("restore operation identity exceeds its bound");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static string Decode(string value)
    {
        if (value.Length is 0 or > 8192)
            throw Invalid("restore operation encoded identity exceeds its bound");
        try
        {
            byte[] bytes = Convert.FromBase64String(value);
            string decoded = new UTF8Encoding(false, true).GetString(bytes);
            if (Encode(decoded) != value)
                throw Invalid("restore operation identity encoding is not canonical");
            return decoded;
        }
        catch (FormatException exception)
        {
            throw Invalid(
                "restore operation identity encoding is malformed",
                exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid(
                "restore operation identity encoding is not valid UTF-8",
                exception);
        }
    }

    private static string Number(long value)
    {
        if (value <= 0)
            throw Invalid("restore operation generation must be positive");
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static long Parse(string value)
    {
        if (!long.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long parsed) ||
            parsed <= 0 ||
            parsed.ToString(CultureInfo.InvariantCulture) != value)
            throw Invalid("restore operation number is not canonical");
        return parsed;
    }

    private static void ValidateComponent(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            value is "." or ".." ||
            value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '-' or '_' or '.')))
            throw Invalid("restore identity is not one safe component");
    }

    private static InvalidOperationException Invalid(
        string detail,
        Exception? inner = null) =>
        new(
            "Environment.Storage.RestoreIncomplete: " + detail + ".",
            inner);
}
