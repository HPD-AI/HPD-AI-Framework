using System.Collections.Immutable;

namespace HPD.Base.Tests.Schema;

public sealed class BaseLogicalIndexSelectionEvidenceContractTests
{
    [Fact]
    public void Evidence_is_canonically_sealed_and_deeply_owned()
    {
        byte[] index = Enumerable.Repeat((byte)0x11, 32).ToArray();
        byte[] publication = Enumerable.Repeat((byte)0x22, 32).ToArray();
        byte[] members = Enumerable.Repeat((byte)0x33, 32).ToArray();
        byte[] key = [1, 2, 3, 4];
        byte[] keyChecksum = System.Security.Cryptography.SHA256.HashData(key);
        byte[] predicate = Enumerable.Repeat((byte)0x44, 32).ToArray();
        BaseLogicalIndexSelectionEvidence draft = Draft(
            index, publication, members, key, keyChecksum, predicate);
        draft = draft with
        {
            EvidenceBytes = BaseLogicalIndexSelectionEvidenceContract.Encode(draft).LongLength,
        };

        BaseLogicalIndexSelectionEvidence sealedEvidence =
            BaseLogicalIndexSelectionEvidenceContract.Seal(draft);
        byte[] expected = sealedEvidence.Checksum.ToArray();

        index[0] ^= 0xff;
        publication[0] ^= 0xff;
        members[0] ^= 0xff;
        key[0] ^= 0xff;
        keyChecksum[0] ^= 0xff;
        predicate[0] ^= 0xff;

        Assert.True(BaseLogicalIndexSelectionEvidenceContract.Validate(sealedEvidence));
        Assert.Equal(expected, sealedEvidence.Checksum.ToArray());
        Assert.Equal(0x11, sealedEvidence.IndexChecksum.ToArray()[0]);
        Assert.Equal(0x22, sealedEvidence.DirectoryPublicationChecksum[0]);
        Assert.Equal(1, sealedEvidence.ReadInterval.CanonicalLowerBound[0]);
    }

    [Fact]
    public void Evidence_rejects_checksum_interval_and_accounting_substitution()
    {
        BaseLogicalIndexSelectionEvidence draft = Draft(
            Enumerable.Repeat((byte)0x11, 32).ToArray(),
            Enumerable.Repeat((byte)0x22, 32).ToArray(),
            Enumerable.Repeat((byte)0x33, 32).ToArray(),
            [1, 2, 3, 4],
            Enumerable.Repeat((byte)0x55, 32).ToArray(),
            Enumerable.Repeat((byte)0x44, 32).ToArray());
        draft = draft with
        {
            EvidenceBytes = BaseLogicalIndexSelectionEvidenceContract.Encode(draft).LongLength,
        };
        BaseLogicalIndexSelectionEvidence sealedEvidence =
            BaseLogicalIndexSelectionEvidenceContract.Seal(draft);

        Assert.False(BaseLogicalIndexSelectionEvidenceContract.Validate(sealedEvidence with
        {
            Candidates = sealedEvidence.Candidates + 1,
        }));
        Assert.False(BaseLogicalIndexSelectionEvidenceContract.Validate(sealedEvidence with
        {
            ReadInterval = sealedEvidence.ReadInterval with
            {
                CanonicalUpperBound = [9],
            },
        }));
        Assert.False(BaseLogicalIndexSelectionEvidenceContract.Validate(sealedEvidence with
        {
            Checksum = Enumerable.Repeat((byte)0xff, 32).ToImmutableArray(),
        }));
    }

    private static BaseLogicalIndexSelectionEvidence Draft(
        byte[] index,
        byte[] publication,
        byte[] members,
        byte[] key,
        byte[] keyChecksum,
        byte[] predicate) => new()
    {
        IndexId = BaseLogicalIndexId.Create("proof.index.v1"),
        IndexVersion = 1,
        IndexChecksum = BaseLogicalIndexChecksum.Create(index),
        AccessShape = BaseIndexAccessShape.LogicalIndexPoint,
        DirectoryGeneration = 2,
        DirectoryPublicationChecksum = publication.ToImmutableArray(),
        MemberSetChecksum = members.ToImmutableArray(),
        EqualityKeyChecksum = keyChecksum.ToImmutableArray(),
        MatchedPredicateChecksum = predicate.ToImmutableArray(),
        ReadInterval = new BaseAtomicReadIntervalEvidence
        {
            LogicalAccessPathId = "logical-index:proof.index.v1",
            CanonicalLowerBound = key.ToImmutableArray(),
            LowerInclusive = true,
            CanonicalUpperBound = key.ToImmutableArray(),
            UpperInclusive = true,
        },
        ExaminedPostings = 1,
        Candidates = 1,
        Comparisons = 0,
        EvidenceBytes = 0,
        Checksum = [],
    };
}
