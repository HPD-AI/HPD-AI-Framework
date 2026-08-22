using System.Collections.Immutable;
using System.Security.Cryptography;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace HPD.Base.Tests.Application.Activations;

public sealed class ScheduleRecoveryManifestTests
{
    [Fact]
    public void Installed_key_registry_is_canonical_and_defensively_owned()
    {
        byte[] seed = SHA256.HashData("registry-key"u8);
        BaseScheduleRecoveryVerificationKey second = BaseScheduleRecoveryManifestContract.CreateVerificationKeyFromPrivateSeed(
            "key-b", 2, seed, 0);
        BaseScheduleRecoveryVerificationKey first = BaseScheduleRecoveryManifestContract.CreateVerificationKeyFromPrivateSeed(
            "key-a", 1, seed, 0);
        var registry = new BaseScheduleRecoveryKeyRegistry([second, first]);

        Assert.Equal(["key-a", "key-b"], registry.Keys.Select(static key => key.Id));
        BaseScheduleRecoveryVerificationKey[] copy = registry.Keys.ToArray();
        copy[0] = copy[0] with { PublicKey = new byte[32].ToImmutableArray() };
        Assert.True(first.PublicKey.AsSpan().SequenceEqual(registry.Keys[0].PublicKey.AsSpan()));
        Assert.Throws<InvalidOperationException>(() => new BaseScheduleRecoveryKeyRegistry([first, first]));
    }

    [Fact]
    public void Manifest_is_canonical_authenticated_and_exactly_bound()
    {
        byte[] seed = SHA256.HashData("schedule-recovery-test-key"u8);
        byte[] publicKey = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);
        BaseScheduleRecoveryVerificationKey key = BaseScheduleRecoveryManifestContract.CreateVerificationKey(
            "recovery-key", 1, publicKey, 100, 10_000);
        BaseScheduleRecoveryManifest manifest = BaseScheduleRecoveryManifestContract.Sign(Unsigned(), key, seed);
        BaseScheduleRecoveryManifestValidation expected = Expected(500);

        Assert.True(BaseScheduleRecoveryManifestContract.Validate(manifest, expected, [key]));
        Assert.False(BaseScheduleRecoveryManifestContract.Validate(
            manifest with { BackupArtifactId = "backup-substituted" }, expected, [key]));
        Assert.False(BaseScheduleRecoveryManifestContract.Validate(manifest, expected with
        { BackupArtifactChecksum = Bytes("other-artifact") }, [key]));
        Assert.False(BaseScheduleRecoveryManifestContract.Validate(manifest, expected with { AcceptedNow = 2_000 }, [key]));
        Assert.False(BaseScheduleRecoveryManifestContract.Validate(manifest, expected, [key with
        { PublicKey = Bytes("wrong-key"), Checksum = key.Checksum }]));
        Assert.Equal(64, manifest.Signature.Length);
        Assert.Equal(32, manifest.ManifestChecksum.Length);
        Assert.Equal("74eca82c085891e03d5ff6c43ff0e20ce480c411007492c14cc555705794211f",
            Convert.ToHexStringLower(manifest.ManifestChecksum.AsSpan()));
        Assert.Equal("42562b898befcd9d5c231ecdd7cd1bce23f7c52f8cd1299f260b8683d00b43e7a924a01ab49e8742a02379817c653b632c17012e0a7ad47de0d3c8fe66eb2403",
            Convert.ToHexStringLower(manifest.Signature.AsSpan()));
        Assert.NotEmpty(BaseScheduleRecoveryManifestContract.CanonicalBytes(manifest));
    }

    [Fact]
    public void Reordered_or_incomplete_floor_authority_fails_closed()
    {
        byte[] seed = SHA256.HashData("schedule-recovery-test-key"u8);
        byte[] publicKey = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);
        BaseScheduleRecoveryVerificationKey key = BaseScheduleRecoveryManifestContract.CreateVerificationKey(
            "recovery-key", 1, publicKey, 100, 10_000);
        BaseScheduleRecoveryManifest unsigned = Unsigned();
        BaseScheduleRecoveryManifest manifest = BaseScheduleRecoveryManifestContract.Sign(unsigned, key, seed);

        Assert.Throws<InvalidOperationException>(() => BaseScheduleRecoveryManifestContract.Sign(
            unsigned with { Floors = unsigned.Floors.Reverse().ToImmutableArray() }, key, seed));
        Assert.False(BaseScheduleRecoveryManifestContract.Validate(manifest, Expected(500) with
        { ExpectedScheduleKeyDigests = [unsigned.Floors[0].ProtectedScheduleKeyDigest] }, [key]));
        Assert.False(BaseScheduleRecoveryManifestContract.Validate(manifest, Expected(500) with
        { ExpectedScheduleKeyDigests = [unsigned.Floors[0].ProtectedScheduleKeyDigest, unsigned.Floors[0].ProtectedScheduleKeyDigest] }, [key]));
    }

    private static BaseScheduleRecoveryManifest Unsigned()
    {
        ImmutableArray<byte> first = Bytes("schedule-a");
        ImmutableArray<byte> second = Bytes("schedule-b");
        ImmutableArray<BaseScheduleRecoveryFloor> floors =
        [
            Floor(first, 4, 400, 7, "occurrences-a", "lineage-a"),
            Floor(second, 6, 450, 9, "occurrences-b", "lineage-b"),
        ];
        floors = floors.OrderBy(static item => Convert.ToHexString(item.ProtectedScheduleKeyDigest.AsSpan()), StringComparer.Ordinal)
            .ToImmutableArray();
        return new BaseScheduleRecoveryManifest
        {
            ApplicationId = "application", LogicalStoreId = "logical-store",
            BackupArtifactId = "backup-1", BackupArtifactChecksum = Bytes("artifact"),
            SourceStoreInstanceId = "source-instance", SourceRestoreEpoch = 3,
            Floors = floors, IssuedAt = 200, ExpiresAt = 1_000, Nonce = Bytes("nonce"),
            SigningKeyId = "recovery-key", SigningKeyVersion = 1,
            ManifestChecksum = [], Signature = [],
        };
    }

    private static BaseScheduleRecoveryManifestValidation Expected(long now)
    {
        BaseScheduleRecoveryManifest unsigned = Unsigned();
        return new BaseScheduleRecoveryManifestValidation
        {
            ApplicationId = unsigned.ApplicationId, LogicalStoreId = unsigned.LogicalStoreId,
            BackupArtifactId = unsigned.BackupArtifactId, BackupArtifactChecksum = unsigned.BackupArtifactChecksum,
            AcceptedNow = now,
            ExpectedScheduleKeyDigests = unsigned.Floors.Select(static item => item.ProtectedScheduleKeyDigest).ToImmutableArray(),
        };
    }

    private static BaseScheduleRecoveryFloor Floor(
        ImmutableArray<byte> digest, long epoch, long nominal, long count, string occurrences, string lineage) => new()
    {
        ProtectedScheduleKeyDigest = digest, ScheduleEpoch = epoch, LastConsideredNominal = nominal,
        OccurrenceCount = count, OccurrenceChecksum = Bytes(occurrences),
        LatestActivationLineageChecksum = Bytes(lineage),
    };

    private static ImmutableArray<byte> Bytes(string value) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)).ToImmutableArray();
}
