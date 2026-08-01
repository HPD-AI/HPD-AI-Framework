namespace HPD.Environment.Tests;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

public sealed class PortableVolumeBackupArchiveTests
{
    [Fact]
    public void Encrypted_archive_round_trips_and_preserves_empty_directories()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(
            Path.Combine(fixture.Source, "empty"));
        Directory.CreateDirectory(
            Path.Combine(fixture.Source, "nested"));
        File.WriteAllText(
            Path.Combine(fixture.Source, "nested", "data.txt"),
            "durable-data");

        PortableVolumeBackupManifest captured =
            fixture.Capture();
        PortableVolumeBackupManifest restored =
            fixture.Restore();

        Assert.Equal(captured, restored);
        Assert.Equal(
            "durable-data",
            File.ReadAllText(
                Path.Combine(
                    fixture.Restored,
                    "nested",
                    "data.txt")));
        Assert.True(
            Directory.Exists(
                Path.Combine(fixture.Restored, "empty")));
        Assert.NotEqual(
            "durable-data",
            System.Text.Encoding.UTF8.GetString(
                File.ReadAllBytes(fixture.Artifact)));
    }

    [Fact]
    public void Tamper_and_wrong_key_fail_before_restore_selection()
    {
        using var fixture = new Fixture();
        File.WriteAllText(
            Path.Combine(fixture.Source, "data.txt"),
            "durable-data");
        _ = fixture.Capture();

        byte[] artifact = File.ReadAllBytes(fixture.Artifact);
        artifact[artifact.Length / 2] ^= 0x40;
        File.WriteAllBytes(fixture.Artifact, artifact);
        InvalidOperationException tampered = Assert.Throws<
            InvalidOperationException>(() => fixture.Restore());
        Assert.Contains("BackupInvalid", tampered.Message);
        Assert.False(Directory.Exists(fixture.Restored));

        File.Delete(fixture.Artifact);
        _ = fixture.Capture();
        byte[] wrong = Enumerable.Repeat((byte)0x77, 32).ToArray();
        using var wrongKey =
            new StorageBackupKeyMaterial("test-key", wrong);
        InvalidOperationException rejected = Assert.Throws<
            InvalidOperationException>(() =>
                PortableVolumeBackupArchive.Validate(
                    fixture.Artifact,
                    wrongKey,
                    1024 * 1024));
        Assert.Contains("BackupInvalid", rejected.Message);
    }

    [Fact]
    public void Linked_source_and_size_overrun_fail_closed()
    {
        using var fixture = new Fixture();
        File.WriteAllText(
            Path.Combine(fixture.Source, "data.txt"),
            "too-large");
        InvalidOperationException overrun = Assert.Throws<
            InvalidOperationException>(() =>
                fixture.Capture(maximumBytes: 2));
        Assert.Contains("BackupInvalid", overrun.Message);

        if (!OperatingSystem.IsWindows())
        {
            File.Delete(
                Path.Combine(fixture.Source, "data.txt"));
            File.CreateSymbolicLink(
                Path.Combine(fixture.Source, "linked"),
                "/tmp");
            InvalidOperationException linked = Assert.Throws<
                InvalidOperationException>(() =>
                    fixture.Capture());
            Assert.Contains(
                "IntegrityCheckRequired",
                linked.Message);
        }
    }

    [Fact]
    public async Task Encoded_payload_stream_round_trips_without_plaintext_host_staging()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Source, "empty"));
        File.WriteAllText(
            Path.Combine(fixture.Source, "data.txt"),
            "streamed-durable-data");
        PortableVolumeBackupManifest source = fixture.Capture();
        var encoded = new MemoryStream();

        PortableVolumeBackupManifest streamed = await
            PortableVolumeBackupArchive.StreamValidatedPayloadAsync(
                fixture.Artifact,
                fixture.Key,
                1024 * 1024,
                (chunk, _) =>
                {
                    encoded.Write(chunk.Span);
                    return ValueTask.CompletedTask;
                });

        string secondArtifact = Path.Combine(
            fixture.Root,
            "streamed.hpdbackup");
        PortableVolumeBackupManifest second = await
            PortableVolumeBackupArchive.CaptureEncodedPayloadAsync(
                secondArtifact,
                streamed with { BackupId = "backup-streamed" },
                fixture.Key,
                encoded.Length,
                Chunks(encoded.ToArray()),
                1024 * 1024);
        string destination = Path.Combine(fixture.Root, "streamed-restore");
        PortableVolumeBackupManifest restored =
            PortableVolumeBackupArchive.RestoreToStaging(
                secondArtifact,
                destination,
                fixture.Key,
                1024 * 1024);

        Assert.Equal(source.ContentSha256, second.ContentSha256);
        Assert.Equal(second, restored);
        Assert.Equal(
            "streamed-durable-data",
            File.ReadAllText(Path.Combine(destination, "data.txt")));
        Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
    }

    [Fact]
    public async Task Incomplete_encoded_payload_is_not_published()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Source, "data.txt"), "value");
        PortableVolumeBackupManifest source = fixture.Capture();
        var encoded = new MemoryStream();
        _ = await PortableVolumeBackupArchive.StreamValidatedPayloadAsync(
            fixture.Artifact,
            fixture.Key,
            1024 * 1024,
            (chunk, _) =>
            {
                encoded.Write(chunk.Span);
                return ValueTask.CompletedTask;
            });
        string destination = Path.Combine(fixture.Root, "incomplete.hpdbackup");

        InvalidOperationException error = await Assert.ThrowsAsync<
            InvalidOperationException>(async () => await
                PortableVolumeBackupArchive.CaptureEncodedPayloadAsync(
                    destination,
                    source with { BackupId = "backup-incomplete" },
                    fixture.Key,
                    encoded.Length,
                    Chunks(encoded.ToArray()[..^1]),
                    1024 * 1024));

        Assert.Contains("IntegrityCheckRequired", error.Message);
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.staging-*"));
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> Chunks(
        byte[] content)
    {
        const int chunkSize = 7;
        for (int offset = 0; offset < content.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, content.Length - offset);
            yield return content.AsMemory(offset, length);
            await Task.Yield();
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root =
            Path.Combine(
                Path.GetTempPath(),
                "hpd-backup-test-" + Guid.NewGuid().ToString("N"));
        private readonly StorageBackupKeyMaterial _key =
            new(
                "test-key",
                Enumerable.Range(0, 32)
                    .Select(static value => (byte)value)
                    .ToArray());

        public Fixture()
        {
            Source = Path.Combine(_root, "source");
            Restored = Path.Combine(_root, "restored");
            Artifact = Path.Combine(_root, "backup.hpdbackup");
            Directory.CreateDirectory(Source);
        }

        public string Source { get; }
        public string Restored { get; }
        public string Artifact { get; }
        public string Root => _root;
        public StorageBackupKeyMaterial Key => _key;

        public PortableVolumeBackupManifest Capture(
            long maximumBytes = 1024 * 1024) =>
            PortableVolumeBackupArchive.Capture(
                Source,
                Artifact,
                new PortableVolumeBackupManifest
                {
                    BackupId = "backup-a",
                    OwnerTypeId = "io.penpot.penpot",
                    OwnerScopeId = "installation-a",
                    OwnerVersion = "revision-a",
                    CompatibilityDomain = "penpot-v1",
                    LogicalVolumeId = "data",
                    VolumeGeneration = 3,
                    ProviderId = "hpd.execution.local",
                    Consistency = VolumeBackupConsistency.Stopped,
                    CreatedAt = DateTimeOffset.UtcNow,
                    LogicalBytes = 0,
                    EntryCount = 0,
                    ContentSha256 = "pending",
                    EncryptionKeyId = "pending",
                },
                _key,
                maximumBytes);

        public PortableVolumeBackupManifest Restore() =>
            PortableVolumeBackupArchive.RestoreToStaging(
                Artifact,
                Restored,
                _key,
                1024 * 1024);

        public void Dispose()
        {
            _key.Dispose();
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
