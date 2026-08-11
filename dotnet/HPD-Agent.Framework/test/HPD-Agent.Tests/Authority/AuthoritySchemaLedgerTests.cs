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
        Assert.Equal(47, AuthoritySchemaLedgerV1.IdFamilies.Length);
        Assert.Equal(95, AuthoritySchemaLedgerV1.IdFamilyCborUsages.Length);
        Assert.Equal(11, AuthoritySchemaLedgerV1.Axes.Length);
        Assert.Equal(14, AuthoritySchemaLedgerV1.Dimensions.Length);
        Assert.Equal(39, AuthoritySchemaLedgerV1.LinearizationPoints.Length);
        Assert.Equal(32, AuthoritySchemaLedgerV1.WireTypes.Length);
        Assert.Equal(127, AuthoritySchemaLedgerV1.WireTypeMembers.Length);
        Assert.Equal(113, AuthoritySchemaLedgerV1.Schemas.Length);
        Assert.Equal(440, AuthoritySchemaLedgerV1.SchemaFields.Length);
        Assert.Equal(11, AuthoritySchemaLedgerV1.AxisValueBindings.Length);
        Assert.Equal(11, AuthoritySchemaLedgerV1.CapacitySubjectBindings.Length);
        Assert.Equal(9, AuthoritySchemaLedgerV1.UnionDiscriminators.Length);
        Assert.Equal(113, AuthoritySchemaLedgerV1.JsonProjectionContexts.Length);
        Assert.Equal(113, AuthoritySchemaLedgerV1.CborCodecHashInventory.Length);
        Assert.Equal(33, AuthoritySchemaLedgerV1.AuthorityPayloadDiscriminators.Length);
        Assert.Equal(11, AuthoritySchemaLedgerV1.GenerationTransitionSchemas.Length);
        Assert.Equal(10, AuthoritySchemaLedgerV1.GenerationInitializationSchemas.Length);
        Assert.Empty(AuthoritySchemaLedgerV1.NativeSchemaInventory);
        Assert.Contains("CapacitySubjectValueV1|union-discriminator|1|CapacitySubjectValueKindV1", AuthoritySchemaLedgerV1.WireTypeMembers);
        Assert.Contains("CapacitySubjectValueV1|union-variant|1|StableId|2|value|StableId128", AuthoritySchemaLedgerV1.WireTypeMembers);
        Assert.Contains("CapacitySubjectValueV1|union-variant|2|OwnerSlice|2|value|OwnerSliceId", AuthoritySchemaLedgerV1.WireTypeMembers);
        Assert.Contains("10|Owner|OwnerSliceId|None|OwnerSlice", AuthoritySchemaLedgerV1.CapacitySubjectBindings);
    }

    [Fact]
    public void SchemaLedger_IdFamiliesExactlyMatchTheWrapperRegistry()
    {
        var wrappers = AuthorityIdFamilyRegistryV1.All.Select(
            row => $"{row.Token}|{row.Type}|owner={row.Owner}|allocator={row.AllocatorOwner}|{row.Kind}");

        Assert.Equal(wrappers, AuthoritySchemaLedgerV1.IdFamilies);
    }

    [Fact]
    public void GeneratedLedger_ExactlyMatchesEveryCheckedInInputRow()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var lines = File.ReadAllLines(Path.Combine(root, "src/HPD-Agent/Authority/Generated/authority-schema-ledger-v1.txt"));
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
        Assert.Equal(107, AuthoritySchemaLedgerV1.JsonProjectionContexts.Distinct(StringComparer.Ordinal).Count());
    }
}
