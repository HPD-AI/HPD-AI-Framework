using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeExecutablePlanTests
{
    [Fact]
    public void Catalog_and_plan_are_canonical_owned_and_have_fixed_goldens()
    {
        var declarations = new List<GraphRuntimeExecutableFactoryDeclaration>
        {
            Declaration("source", "Graph.App:Graph.Nodes.Source@2", 7),
            Declaration("sink", "Graph.App:Graph.Nodes.Sink@1", 4),
        };
        var result = Catalog(declarations);
        declarations.Clear();
        Assert.Equal(["sink", "source"], result.Catalog.Entries.Select(x => x.NodeKey.ToString()));
        Assert.False(result.Catalog.Entries is GraphRuntimeExecutableFactoryBinding[]);

        var topology = Topology("source", "sink");
        var charges = new List<CapacityChargeV1> { Charge() };
        var compiled = Assert.IsType<GraphRuntimeExecutableCompileResult.Compiled>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, result, charges));
        charges.Clear();
        Assert.Single(compiled.Plan.CapacityCharges);
        Assert.Equal(2, compiled.Plan.NodeBindings.Count);
        Assert.False(compiled.Plan.CapacityCharges is CapacityChargeV1[]);
        Assert.False(compiled.Plan.NodeBindings is GraphRuntimeExecutableFactoryBinding[]);
        Assert.Equal("1e2871e890e082d6671db5476624931953c3e70ad9d06250307cc37ff7bc6879",
            result.Catalog.Fingerprint.ToString());
        Assert.Equal("6b1f1a1103de0954ce849d0c8e9ddfcc734953be0da3c5c2156b5e11fd36b09f",
            compiled.Plan.Fingerprint.ToString());
        Assert.Equal("1400a75ccff1f2e330df855739cfe8ec", Hex(result.Catalog.Entries[0].FactoryIdentity));
    }

    [Fact]
    public void Catalog_order_and_fingerprint_ignore_declaration_order()
    {
        var first = Catalog([Declaration("z", "A:Z@1", 2), Declaration("a", "A:A@1", 1)]).Catalog;
        var second = Catalog([Declaration("a", "A:A@1", 1), Declaration("z", "A:Z@1", 2)]).Catalog;
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Entries, second.Entries);
    }

    [Fact]
    public void Catalog_bounds_and_duplicate_keys_fail_closed()
    {
        AssertInvalid([], GraphRuntimeExecutableCatalogInvalid.Empty);
        Assert.IsType<GraphRuntimeExecutableCatalogResult.Created>(
            GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(
                Enumerable.Range(0, 64).Select(i => Declaration($"n{i:00}", $"A:N{i:00}@1", 1))));
        AssertInvalid(Enumerable.Range(0, 65).Select(i => Declaration($"n{i:00}", $"A:N{i:00}@1", 1)),
            GraphRuntimeExecutableCatalogInvalid.TooMany);
        AssertInvalid([Declaration("a", "A:X@1", 1), Declaration("a", "A:Y@1", 1)],
            GraphRuntimeExecutableCatalogInvalid.DuplicateNodeKey);
    }

    [Theory]
    [InlineData("A:T@1", 0u, 8)]
    [InlineData("no-format", 1u, 7)]
    [InlineData("A:T@0", 1u, 7)]
    [InlineData("A:T@01", 1u, 7)]
    [InlineData("A:T@4294967296", 1u, 7)]
    public void Identity_and_revision_validation_is_closed(string identity, uint revision, int expected) =>
        AssertInvalid([Declaration("a", identity, revision)], (GraphRuntimeExecutableCatalogInvalid)expected);

    [Fact]
    public void Identity_requires_nfc_and_exact_utf8_bound()
    {
        AssertInvalid([Declaration("a", "A:T\u0065\u0301@1", 1)], GraphRuntimeExecutableCatalogInvalid.InvalidIdentity);
        AssertInvalid([Declaration("a", $"A:{new string('x', 509)}@1", 1)], GraphRuntimeExecutableCatalogInvalid.InvalidIdentity);
        Assert.IsType<GraphRuntimeExecutableCatalogResult.Created>(
            GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(
                [Declaration("a", $"A:{new string('x', 508)}@1", 1)]));
    }

    [Fact]
    public void Compile_reports_topology_missing_and_extra_exactly()
    {
        var topology = Topology("a", "b");
        Assert.IsType<GraphRuntimeExecutableCompileResult.TopologyMismatch>(
            GraphRuntimeExecutablePlan.Compile(topology, WrongHash(),
                Catalog([Declaration("a", "A:A@1", 1), Declaration("b", "A:B@1", 1)]), [Charge()]));
        var missing = Assert.IsType<GraphRuntimeExecutableCompileResult.MissingFactory>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint,
                Catalog([Declaration("a", "A:A@1", 1)]), [Charge()]));
        Assert.Equal("b", missing.NodeKey.ToString());
        var extraCatalog = Catalog([Declaration("a", "A:A@1", 1), Declaration("b", "A:B@1", 1),
            Declaration("c", "A:C@1", 1)]);
        var extra = Assert.IsType<GraphRuntimeExecutableCompileResult.ExtraFactory>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, extraCatalog, [Charge()]));
        Assert.Equal("c", extra.NodeKey.ToString());
    }

    [Fact]
    public void Compile_requires_full_distinct_capacity_dimension_binding()
    {
        var topology = Topology("a");
        var catalog = Catalog([Declaration("a", "A:A@1", 1)]);
        Assert.IsType<GraphRuntimeExecutableCompileResult.InvalidCatalog>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, catalog, []));
        Assert.IsType<GraphRuntimeExecutableCompileResult.InvalidCatalog>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, catalog, [Charge(), Charge()]));
    }

    [Fact]
    public void Capacity_charge_order_uses_amount_as_final_tie_breaker()
    {
        var topology = Topology("a"); var catalog = Catalog([Declaration("a", "A:A@1", 1)]);
        var first = Assert.IsType<GraphRuntimeExecutableCompileResult.Compiled>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, catalog, [Charge(9), Charge(2)])).Plan;
        var second = Assert.IsType<GraphRuntimeExecutableCompileResult.Compiled>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, catalog, [Charge(2), Charge(9)])).Plan;
        Assert.Equal([2L, 9L], first.CapacityCharges.Select(x => x.Amount));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Declaration_authority_reports_missing_extra_duplicate_and_zero_hash()
    {
        var a = new BoundedAscii("a"); var b = new BoundedAscii("b");
        Assert.Equal(GraphRuntimeExecutableCatalogInvalid.MissingDeclaration,
            Assert.IsType<GraphRuntimeExecutableCatalogResult.Invalid>(
                GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(
                    [Declaration("a", "A:A@1", 1)], [a, b], Hash)).Reason);
        Assert.Equal(GraphRuntimeExecutableCatalogInvalid.ExtraDeclaration,
            Assert.IsType<GraphRuntimeExecutableCatalogResult.Invalid>(
                GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(
                    [Declaration("a", "A:A@1", 1), Declaration("b", "A:B@1", 1)], [a], Hash)).Reason);
        Assert.Equal(GraphRuntimeExecutableCatalogInvalid.DuplicateFactoryIdentity,
            Assert.IsType<GraphRuntimeExecutableCatalogResult.Invalid>(
                GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(
                    [Declaration("a", "A:A@1", 1), Declaration("b", "A:B@1", 1)], null, FixedHash)).Reason);
        Assert.Equal(GraphRuntimeExecutableCatalogInvalid.InvalidIdentity,
            Assert.IsType<GraphRuntimeExecutableCatalogResult.Invalid>(
                GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(
                    [Declaration("a", "A:A@1", 1)], null, static _ => new byte[32])).Reason);
    }

    [Fact]
    public void Corrupt_hash_seam_and_invalid_unicode_fail_through_closed_identity_outcome()
    {
        var declaration = new[] { Declaration("a", "A:A@1", 1) };
        AssertHashInvalid(declaration, null);
        AssertHashInvalid(declaration, static _ => throw new InvalidOperationException("hostile hash"));
        AssertHashInvalid(declaration, static _ => null!);
        AssertHashInvalid(declaration, static _ => new byte[31]);
        AssertHashInvalid(declaration, static _ => new byte[33]);
        AssertInvalid([Declaration("a", "A:T\ud800@1", 1)],
            GraphRuntimeExecutableCatalogInvalid.InvalidIdentity);
    }

    [Fact]
    public void Null_inputs_fail_through_closed_results_and_aot_path_has_no_discovery()
    {
        Assert.Equal(GraphRuntimeExecutableCatalogInvalid.MissingDeclaration,
            Assert.IsType<GraphRuntimeExecutableCatalogResult.Invalid>(
                GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(null!)).Reason);
        Assert.Equal(GraphRuntimeExecutableCatalogInvalid.MissingDeclaration,
            Assert.IsType<GraphRuntimeExecutableCatalogResult.Invalid>(
                GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(
                    new GraphRuntimeExecutableFactoryDeclaration[] { null! })).Reason);
        var topology = Topology("a"); var catalog = Catalog([Declaration("a", "A:A@1", 1)]);
        Assert.IsType<GraphRuntimeExecutableCompileResult.InvalidCatalog>(
            GraphRuntimeExecutablePlan.Compile(null!, topology.Fingerprint, catalog, [Charge()]));
        Assert.IsType<GraphRuntimeExecutableCompileResult.InvalidCatalog>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, catalog, null!));
        Assert.IsType<GraphRuntimeExecutableCompileResult.InvalidCatalog>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, catalog,
                new CapacityChargeV1[] { null! }));
        // Direct generated data only: no reflection, scanning, global registry, adapter, or effect callback enters this path.
        Assert.IsType<GraphRuntimeExecutableCompileResult.Compiled>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, catalog, [Charge()]));
    }

    [Fact]
    public void Every_plan_binding_changes_the_plan_fingerprint()
    {
        var baselineTopology = Topology("a");
        var baselineCatalog = Catalog([Declaration("a", "A:A@1", 1)]);
        var baseline = Compile(baselineTopology, baselineCatalog, [Charge()]);
        Assert.NotEqual(baseline, Compile(TopologyWith(Id(11), Id(2), Id(3), Id(4), "a"), baselineCatalog, [Charge()]));
        Assert.NotEqual(baseline, Compile(TopologyWith(Id(1), Id(12), Id(3), Id(4), "a"), baselineCatalog, [Charge()]));
        Assert.NotEqual(baseline, Compile(TopologyWith(Id(1), Id(2), Id(13), Id(4), "a"), baselineCatalog, [Charge()]));
        Assert.NotEqual(baseline, Compile(TopologyWith(Id(1), Id(2), Id(3), Id(14), "a"), baselineCatalog, [Charge()]));
        Assert.NotEqual(baseline, Compile(baselineTopology, baselineCatalog, [Charge(2)]));
        Assert.NotEqual(baseline, Compile(baselineTopology, Catalog([Declaration("a", "A:A@1", 2)]), [Charge()]));
    }

    [Fact]
    public void Revisions_and_implementation_identity_change_stable_fingerprints()
    {
        var a = Catalog([Declaration("a", "A:T@1", 1)]).Catalog;
        var b = Catalog([Declaration("a", "A:T@1", 2)]).Catalog;
        var c = Catalog([Declaration("a", "A:T@2", 1)]).Catalog;
        Assert.NotEqual(a.Entries[0].FactoryIdentity, b.Entries[0].FactoryIdentity);
        Assert.NotEqual(a.Entries[0].FactoryIdentity, c.Entries[0].FactoryIdentity);
        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
        Assert.NotEqual(a.Fingerprint, c.Fingerprint);
    }

    private static GraphRuntimeExecutableCatalogResult.Created Catalog(
        IEnumerable<GraphRuntimeExecutableFactoryDeclaration> declarations) =>
        Assert.IsType<GraphRuntimeExecutableCatalogResult.Created>(
            GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(declarations));

    private static void AssertInvalid(IEnumerable<GraphRuntimeExecutableFactoryDeclaration> declarations,
        GraphRuntimeExecutableCatalogInvalid expected) => Assert.Equal(expected,
        Assert.IsType<GraphRuntimeExecutableCatalogResult.Invalid>(
            GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(declarations)).Reason);

    private static void AssertHashInvalid(IEnumerable<GraphRuntimeExecutableFactoryDeclaration> declarations,
        GraphRuntimeExecutableFactoryCatalog.FactoryHash? hash) => Assert.Equal(
            GraphRuntimeExecutableCatalogInvalid.InvalidIdentity,
            Assert.IsType<GraphRuntimeExecutableCatalogResult.Invalid>(
                GraphRuntimeExecutableFactoryCatalog.FromGeneratedApplicationManifest(declarations, null, hash)).Reason);

    private static GraphRuntimeExecutableFactoryDeclaration Declaration(string key, string identity, uint revision) =>
        new(new BoundedAscii(key), identity, revision);

    private static GraphTopologyPlan Topology(params string[] keys) => new(Session(), Graph(), Grant(),
        keys.Select(key => new GraphTopologyNode(new BoundedAscii(key))), [], [new CapacityDimensionId(3)]);

    private static CapacityChargeV1 Charge(long amount = 1) => new(new CapacityDimensionId(3),
        new CapacityScopeV1(TenantId.FromValue(Id(5)), null, new CapacitySubjectV1.Operation(OperationId.FromValue(Id(6)))),
        amount, CapacityPurposeId.FromValue(Id(7)), new CapacityChargeWindowV1.NoWindow());
    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
    private static GraphGenerationId Graph() => GraphGenerationId.FromValue(Id(3));
    private static CapacityGrantId Grant() => CapacityGrantId.FromValue(Id(4));
    private static StableId128 Id(byte seed)
    { Span<byte> bytes = stackalloc byte[16]; for (var i = 0; i < 16; i++) bytes[i] = checked((byte)(seed + i)); return StableId128.FromBytes(bytes); }
    private static Hash256 WrongHash()
    { Span<byte> bytes = stackalloc byte[32]; bytes[0] = 1; return Hash256.FromBytes(bytes); }
    private static byte[] Hash(ReadOnlySpan<byte> preimage) => System.Security.Cryptography.SHA256.HashData(preimage);
    private static byte[] FixedHash(ReadOnlySpan<byte> _) { var bytes = new byte[32]; bytes[0] = 1; return bytes; }
    private static string Hex(StableId128 value)
    { Span<byte> bytes = stackalloc byte[16]; value.TryWriteBytes(bytes); return Convert.ToHexString(bytes).ToLowerInvariant(); }
    private static Hash256 Compile(GraphTopologyPlan topology, GraphRuntimeExecutableCatalogResult.Created catalog,
        IEnumerable<CapacityChargeV1> charges) => Assert.IsType<GraphRuntimeExecutableCompileResult.Compiled>(
            GraphRuntimeExecutablePlan.Compile(topology, topology.Fingerprint, catalog, charges)).Plan.Fingerprint;
    private static GraphTopologyPlan TopologyWith(StableId128 runtime, StableId128 session, StableId128 graph,
        StableId128 grant, params string[] keys) => new(
            new(RuntimeGenerationId.FromValue(runtime), LiveSessionId.FromValue(session)),
            GraphGenerationId.FromValue(graph), CapacityGrantId.FromValue(grant),
            keys.Select(key => new GraphTopologyNode(new BoundedAscii(key))), [], [new CapacityDimensionId(3)]);
}
