using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthoritySchemaLedgerTests
{
    private static readonly (string Name, string[] Rows)[] GeneratedSections =
    [
        ("IdFamilies", AuthoritySchemaLedgerV1.IdFamilies),
        ("IdFamilyCborUsages", AuthoritySchemaLedgerV1.IdFamilyCborUsages),
        ("Axes", AuthoritySchemaLedgerV1.Axes),
        ("Dimensions", AuthoritySchemaLedgerV1.Dimensions),
        ("LinearizationPoints", AuthoritySchemaLedgerV1.LinearizationPoints),
        ("WireTypes", AuthoritySchemaLedgerV1.WireTypes),
        ("WireTypeMembers", AuthoritySchemaLedgerV1.WireTypeMembers),
        ("Schemas", AuthoritySchemaLedgerV1.Schemas),
        ("SchemaFields", AuthoritySchemaLedgerV1.SchemaFields),
        ("AxisValueBindings", AuthoritySchemaLedgerV1.AxisValueBindings),
        ("CapacitySubjectBindings", AuthoritySchemaLedgerV1.CapacitySubjectBindings),
        ("UnionDiscriminators", AuthoritySchemaLedgerV1.UnionDiscriminators),
        ("JsonProjectionContexts", AuthoritySchemaLedgerV1.JsonProjectionContexts),
        ("CborCodecHashInventory", AuthoritySchemaLedgerV1.CborCodecHashInventory),
        ("AuthorityPayloadDiscriminators", AuthoritySchemaLedgerV1.AuthorityPayloadDiscriminators),
        ("GenerationTransitionSchemas", AuthoritySchemaLedgerV1.GenerationTransitionSchemas),
        ("GenerationInitializationSchemas", AuthoritySchemaLedgerV1.GenerationInitializationSchemas),
        ("NativeSchemaInventory", AuthoritySchemaLedgerV1.NativeSchemaInventory),
    ];

    [Fact]
    public void GeneratedLedger_HasTheAcceptedExactCardinalities()
    {
        Assert.Equal(48, AuthoritySchemaLedgerV1.IdFamilies.Length);
        Assert.Equal(135, AuthoritySchemaLedgerV1.IdFamilyCborUsages.Length);
        Assert.Equal(11, AuthoritySchemaLedgerV1.Axes.Length);
        Assert.Equal(14, AuthoritySchemaLedgerV1.Dimensions.Length);
        Assert.Equal(39, AuthoritySchemaLedgerV1.LinearizationPoints.Length);
        Assert.Equal(34, AuthoritySchemaLedgerV1.WireTypes.Length);
        Assert.Equal(134, AuthoritySchemaLedgerV1.WireTypeMembers.Length);
        Assert.Equal(162, AuthoritySchemaLedgerV1.Schemas.Length);
        Assert.Equal(727, AuthoritySchemaLedgerV1.SchemaFields.Length);
        Assert.Equal(11, AuthoritySchemaLedgerV1.AxisValueBindings.Length);
        Assert.Equal(11, AuthoritySchemaLedgerV1.CapacitySubjectBindings.Length);
        Assert.Equal(9, AuthoritySchemaLedgerV1.UnionDiscriminators.Length);
        Assert.Equal(162, AuthoritySchemaLedgerV1.JsonProjectionContexts.Length);
        Assert.Equal(162, AuthoritySchemaLedgerV1.CborCodecHashInventory.Length);
        Assert.Equal(48, AuthoritySchemaLedgerV1.AuthorityPayloadDiscriminators.Length);
        Assert.Equal(11, AuthoritySchemaLedgerV1.GenerationTransitionSchemas.Length);
        Assert.Equal(10, AuthoritySchemaLedgerV1.GenerationInitializationSchemas.Length);
        Assert.Empty(AuthoritySchemaLedgerV1.NativeSchemaInventory);
        Assert.Contains("CapacitySubjectValueV1|union-discriminator|1|CapacitySubjectValueKindV1", AuthoritySchemaLedgerV1.WireTypeMembers);
        Assert.Contains("CapacitySubjectValueV1|union-variant|1|StableId|2|value|StableId128", AuthoritySchemaLedgerV1.WireTypeMembers);
        Assert.Contains("CapacitySubjectValueV1|union-variant|2|OwnerSlice|2|value|OwnerSliceId", AuthoritySchemaLedgerV1.WireTypeMembers);
        Assert.Contains("CapacitySettlementKindV1|enum|8|WindowAgedOut", AuthoritySchemaLedgerV1.WireTypeMembers);
        Assert.Contains("CapacityChargeWindowV1|union-variant|2|EndsAt|2|value|MonotonicStampV1", AuthoritySchemaLedgerV1.WireTypeMembers);
        Assert.Contains("hpd.capacity-charge.v1|5|window|CapacityChargeWindowV1|required=true|registered-type:CapacityChargeWindowV1|union=None", AuthoritySchemaLedgerV1.SchemaFields);
        Assert.Contains("hpd.capacity-settlement-fact-body.v1|6|evidenceAt|MonotonicStampV1|required=true|registered-type:MonotonicStampV1|union=None", AuthoritySchemaLedgerV1.SchemaFields);
        Assert.Contains("10|Owner|OwnerSliceId|None|OwnerSlice", AuthoritySchemaLedgerV1.CapacitySubjectBindings);
        Assert.Contains(AuthoritySchemaLedgerV1.Schemas, row => row.StartsWith("hpd.graph-runtime-command.v1|1.0|S2|HPD-Agent.Audio|", StringComparison.Ordinal));
        Assert.Contains(AuthoritySchemaLedgerV1.Schemas, row => row.StartsWith("hpd.graph-runtime-snapshot.v1|1.0|S2|HPD-Agent.Audio|", StringComparison.Ordinal));
        Assert.Contains(AuthoritySchemaLedgerV1.Schemas, row => row.StartsWith("hpd.graph-runtime-fact.v1|1.0|S2|HPD-Agent.Audio|", StringComparison.Ordinal));
        Assert.Contains("hpd.authority-owner-payload.v1|36|GraphRuntimeCommand|hpd.authority-payload-graph-runtime-command.v1|GraphRuntimeOwnerPayloadV1|S2", AuthoritySchemaLedgerV1.AuthorityPayloadDiscriminators);
        Assert.Contains("hpd.authority-owner-payload.v1|37|GraphRuntimeFact|hpd.authority-payload-graph-runtime-fact.v1|GraphRuntimeOwnerPayloadV1|S2", AuthoritySchemaLedgerV1.AuthorityPayloadDiscriminators);
        Assert.DoesNotContain(AuthoritySchemaLedgerV1.GenerationTransitionSchemas, row => row.Contains("GraphRuntime", StringComparison.Ordinal));
        Assert.DoesNotContain(AuthoritySchemaLedgerV1.GenerationInitializationSchemas, row => row.Contains("GraphRuntime", StringComparison.Ordinal));
        Assert.Contains("hpd.authority-owner-payload.v1|38|GraphParticipantReservationCommand|hpd.authority-payload-graph-participant-reservation-command.v1|GraphParticipantReservationCommandV1|S1", AuthoritySchemaLedgerV1.AuthorityPayloadDiscriminators);
        Assert.Contains("hpd.authority-owner-payload.v1|41|GraphParticipantBindingFact|hpd.authority-payload-graph-participant-binding-fact.v1|GraphParticipantBindingFactV1|S1", AuthoritySchemaLedgerV1.AuthorityPayloadDiscriminators);
        Assert.Contains("hpd.authority-owner-payload.v1|42|GlobalParticipantClaimRecord|hpd.authority-payload-global-participant-claim-record.v1|GlobalParticipantClaimRecordV1|S1", AuthoritySchemaLedgerV1.AuthorityPayloadDiscriminators);
        Assert.Contains("gpa|GlobalParticipantAllocatorJournalId|owner=S1|allocator=S1|authority|visibility=internal", AuthoritySchemaLedgerV1.IdFamilies);
    }

    [Fact]
    public void SchemaLedger_IdFamiliesExactlyMatchTheWrapperRegistry()
    {
        var wrappers = AuthorityIdFamilyRegistryV1.All.Select(
            row => $"{row.Token}|{row.Type}|owner={row.Owner}|allocator={row.AllocatorOwner}|{row.Kind}|visibility={row.Visibility}");

        Assert.Equal(wrappers, AuthoritySchemaLedgerV1.IdFamilies);
    }

    [Fact]
    public void GeneratedLedger_ExactlyMatchesEveryCheckedInInputRow()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var lines = File.ReadAllLines(Path.Combine(root, "src/HPD-Agent/Authority/Generated/authority-schema-ledger-v1.txt"));
        Assert.Equal("# source-contract-sha256=751de62504018a277eec7ecd25fb24a972121f24760579f908070e438e954885", lines[0]);
        Assert.Equal("# source-registry-sha256=61630b3b2f890add5146b6c80611316cdf0dba012547fd76dbce02ab155a58da", lines[1]);
        Assert.Contains("hpd.global-participant-page.v1|7|isFinal|UInt16|required=true|range:0..1|union=None", lines);
        var expected = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;
        foreach (var line in lines)
        {
            if (line.StartsWith('@'))
            {
                current = [];
                expected.Add(line[1..], current);
            }
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                Assert.NotNull(current);
                current!.Add(line);
            }
        }

        Assert.Equal(GeneratedSections.Select(section => section.Name), expected.Keys);
        foreach (var section in GeneratedSections)
            Assert.Equal(expected[section.Name], section.Rows);
    }

    [Fact]
    public void ReservationV2LedgerRows_AreExact()
    {
        var schemas=AuthoritySchemaLedgerV1.Schemas.Select(row=>row.Split('|')[0]).ToHashSet(StringComparer.Ordinal);
        var payloadDiscriminators=AuthoritySchemaLedgerV1.AuthorityPayloadDiscriminators.ToList();
        Assert.Equal(162,schemas.Count);
        Assert.Equal(48,payloadDiscriminators.Count);
        Assert.Contains("hpd.authority-owner-payload.v1|43|GraphParticipantReservationCommandV2|hpd.authority-payload-graph-participant-reservation-command.v2|GraphParticipantReservationCommandV2|S1",payloadDiscriminators);
        Assert.Contains("hpd.authority-owner-payload.v1|44|GraphParticipantReservationFactV2|hpd.authority-payload-graph-participant-reservation-fact.v2|GraphParticipantReservationFactV2|S1",payloadDiscriminators);
        Assert.Contains("hpd.authority-owner-payload.v1|45|GraphMediaPhysicalReleaseCommand|hpd.authority-payload-graph-media-physical-release-command.v1|GraphMediaPhysicalReleaseOuterV1|S1",payloadDiscriminators);
        Assert.Contains("hpd.authority-owner-payload.v1|46|GraphMediaPhysicalReleaseFact|hpd.authority-payload-graph-media-physical-release-fact.v1|GraphMediaPhysicalReleaseOuterV1|S1",payloadDiscriminators);
        Assert.Contains("hpd.authority-owner-payload.v1|47|GraphMediaWorkExecutionCommand|hpd.authority-payload-graph-media-work-execution-command.v1|GraphMediaWorkExecutionOuterV1|S1",payloadDiscriminators);
        Assert.Contains("hpd.authority-owner-payload.v1|48|GraphMediaWorkExecutionFact|hpd.authority-payload-graph-media-work-execution-fact.v1|GraphMediaWorkExecutionOuterV1|S1",payloadDiscriminators);
    }

    [Fact]
    public void SessionStampSchema_JoinsItsTagsCodecAndProjectionInventory()
    {
        Assert.Contains("hpd.session-authority-stamp.v1|1.0|S1|HPD-Agent|HPD.Agent.Generated.AuthorityJsonContextV1|DeterministicCborV1:hpd.session-authority-stamp.v1|hpd-authority-sha256-v1", AuthoritySchemaLedgerV1.Schemas);
        Assert.Contains("hpd.session-authority-stamp.v1|1|runtimeGenerationId|RuntimeGenerationId|required=true|stable-id-family-ledger:16-bytes/nonzero|union=None", AuthoritySchemaLedgerV1.SchemaFields);
        Assert.Contains("hpd.session-authority-stamp.v1|2|liveSessionId|LiveSessionId|required=true|stable-id-family-ledger:16-bytes/nonzero|union=None", AuthoritySchemaLedgerV1.SchemaFields);
        Assert.Contains("hpd.session-authority-stamp.v1|1.0|DeterministicCborV1:hpd.session-authority-stamp.v1|hpd-authority-sha256-v1", AuthoritySchemaLedgerV1.CborCodecHashInventory);
        Assert.Contains("HPD-Agent|HPD.Agent.Generated.AuthorityJsonContextV1|SessionAuthorityStampV1", AuthoritySchemaLedgerV1.JsonProjectionContexts);
    }

    [Fact]
    public void EverySchema_HasOneCodecAndOneProjectionContext()
    {
        var schemas = AuthoritySchemaLedgerV1.Schemas.Select(row => row.Split('|')[0]).ToHashSet(StringComparer.Ordinal);
        var codecs = AuthoritySchemaLedgerV1.CborCodecHashInventory.Select(row => row.Split('|')[0]).ToHashSet(StringComparer.Ordinal);
        var fields = AuthoritySchemaLedgerV1.SchemaFields.Select(row => row.Split('|')[0]).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(schemas, codecs);
        Assert.Subset(schemas, fields);
        Assert.Equal(161, AuthoritySchemaLedgerV1.JsonProjectionContexts.Distinct(StringComparer.Ordinal).Count());
    }
}
