using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class CapacityDimensionRegistryV1Tests
{
    private const string FrozenRows = """
1|media-bytes|Bytes|Resident|Tenant,Session,Participant|None|16777216|1|MediaLeaseReleased
2|encoded-bytes|Bytes|Resident|Tenant,Session,Operation|None|4194304|1|EncodedBufferReleased
3|queue-items|Items|Resident|Tenant,Session,Operation|None|1024|1|QueueItemsRemoved
4|audio-samples|Samples|Resident|Tenant,Session,Participant|None|480000|1|SampleRangeReleased
5|buffer-nanoseconds|Nanoseconds|Resident|Tenant,Session,Participant|None|10000000000|1|TimedBufferReleased
6|provider-inflight|Slots|Exclusive|Tenant,Session,Provider|None|64|1|ProviderSlotSettled
7|output-inflight|Slots|Exclusive|Tenant,Session,Sink|None|64|1|OutputSlotSettled
8|subscriber-items|Items|Resident|Tenant,Session,Subscriber|None|256|1|SubscriberRangeReleased
9|subscriber-bytes|Bytes|Resident|Tenant,Session,Subscriber|None|1048576|1|SubscriberBytesReleased
10|journal-bytes|Bytes|Resident|Tenant,Session|Authority|1048576|1|JournalRetentionRetired
11|copy-obligations|Items|Resident|Tenant,Session,Custodian|Privacy|4096|1|CopyObligationTerminalized
12|quarantine-bytes|Bytes|Resident|Tenant,Session,Schema|Recovery|1048576|1|QuarantinePayloadRetired
13|diagnostic-cardinality|Items|RateWindow|Tenant,Exporter|None|1024|1|DiagnosticWindowElapsed
14|recovery-work|Items|Resident|Tenant,Session,Owner|Recovery|1024|1|RecoveryWorkTerminalized
""";

    [Fact]
    public void Registry_is_the_exact_ordered_fourteen_dimension_set()
    {
        string[] expectedTokens =
        [
            "media-bytes", "encoded-bytes", "queue-items", "audio-samples", "buffer-nanoseconds",
            "provider-inflight", "output-inflight", "subscriber-items", "subscriber-bytes", "journal-bytes",
            "copy-obligations", "quarantine-bytes", "diagnostic-cardinality", "recovery-work",
        ];

        Assert.Equal(14, CapacityDimensionRegistryV1.All.Count);
        Assert.IsNotType<CapacityDimensionDescriptorV1[]>(CapacityDimensionRegistryV1.All);
        Assert.Equal(expectedTokens, CapacityDimensionRegistryV1.All.Select(static descriptor => descriptor.Token));
        Assert.Equal(Enumerable.Range(1, 14), CapacityDimensionRegistryV1.All.Select(static descriptor => (int)descriptor.Id.Value));
        Assert.All(CapacityDimensionRegistryV1.All, static descriptor =>
        {
            Assert.True(descriptor.Id.IsValid);
            Assert.True(descriptor.MaximumPerCharge > 0);
            Assert.Equal((ushort)1, descriptor.SchemaVersion);
            Assert.NotEmpty(descriptor.ScopeKinds);
            Assert.Equal(descriptor.ScopeKinds.Count, descriptor.ScopeKinds.Distinct().Count());
            Assert.Matches("^[a-z][a-z0-9-]+$", descriptor.Token);
            Assert.Matches("^[A-Z][A-Za-z0-9]+$", descriptor.SettlementEvidence);
        });
    }

    [Fact]
    public void Registered_values_resolve_to_the_same_immutable_entry()
    {
        foreach (var descriptor in CapacityDimensionRegistryV1.All)
            Assert.Same(descriptor, CapacityDimensionRegistryV1.Get(descriptor.Id));

        Assert.Throws<ArgumentOutOfRangeException>(() => CapacityDimensionRegistryV1.Get(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapacityDimensionId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapacityDimensionId(15));
        Assert.False(default(CapacityDimensionId).IsValid);
        Assert.Equal(string.Empty, default(CapacityDimensionId).ToString());
    }

    [Fact]
    public void Descriptor_contract_matches_the_frozen_governance_rows()
    {
        var media = CapacityDimensionRegistryV1.Get(new CapacityDimensionId(1));
        Assert.IsNotType<CapacityScopeKindV1[]>(media.ScopeKinds);
        Assert.Equal(CapacityUnitV1.Bytes, media.Unit);
        Assert.Equal(CapacityConservationV1.Resident, media.Conservation);
        Assert.Equal([CapacityScopeKindV1.Tenant, CapacityScopeKindV1.Session, CapacityScopeKindV1.Participant], media.ScopeKinds);
        Assert.Equal(16_777_216, media.MaximumPerCharge);
        Assert.Equal(CapacityEmergencyClassV1.None, media.EmergencyClass);

        var journal = CapacityDimensionRegistryV1.Get(new CapacityDimensionId(10));
        Assert.Equal(CapacityEmergencyClassV1.Authority, journal.EmergencyClass);
        Assert.Equal("JournalRetentionRetired", journal.SettlementEvidence);

        var privacy = CapacityDimensionRegistryV1.Get(new CapacityDimensionId(11));
        Assert.Equal(CapacityEmergencyClassV1.Privacy, privacy.EmergencyClass);

        var recovery = CapacityDimensionRegistryV1.Get(new CapacityDimensionId(14));
        Assert.Equal(CapacityEmergencyClassV1.Recovery, recovery.EmergencyClass);
    }

    [Fact]
    public void Every_generated_field_matches_the_independent_frozen_rows()
    {
        var actual = string.Join('\n', CapacityDimensionRegistryV1.All.Select(static descriptor => string.Join('|',
            descriptor.Id.Value,
            descriptor.Token,
            descriptor.Unit,
            descriptor.Conservation,
            string.Join(',', descriptor.ScopeKinds),
            descriptor.EmergencyClass,
            descriptor.MaximumPerCharge,
            descriptor.SchemaVersion,
            descriptor.SettlementEvidence)));

        Assert.Equal(FrozenRows, actual);
        Assert.All(CapacityDimensionRegistryV1.All, static descriptor => Assert.Equal(OwnerSliceId.S2, descriptor.Owner));
        Assert.Equal(CapacityDimensionsV1.JournalBytes, CapacityDimensionRegistryV1.All[9].Id);
    }
}
