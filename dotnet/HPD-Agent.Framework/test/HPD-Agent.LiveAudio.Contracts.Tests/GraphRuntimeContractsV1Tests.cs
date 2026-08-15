using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeContractsV1Tests
{
    [Fact]
    public void RequestHashes_HaveIndependentLiteralPreimageAndDigestGoldens()
    {
        const string activate = "6870642d73322d67726170682d72756e74696d652d6566666563742d726571756573742d763100010101010101010101010101010101010202020202020202020202020202020200010303030303030303030303030303030300000000000000070404040404040404040404040404040404040404040404040404040404040404050505050505050505050505050505050000000000000009";
        const string retire = "6870642d73322d67726170682d72756e74696d652d6566666563742d726571756573742d7631000101010101010101010101010101010102020202020202020202020202020202000203030303030303030303030303030303000000000000000b";
        var session = Session(); var operation = OperationId.FromValue(Id(3)); var fingerprint = Hash(4);
        var activatePreimage = GraphRuntimeEffectHashesV1.RequestPreimage(session, GraphRuntimeCommandKindV1.Activate,
            operation, Position(7), fingerprint, GraphGenerationId.FromValue(Id(5)), Position(9));
        var retirePreimage = GraphRuntimeEffectHashesV1.RequestPreimage(session, GraphRuntimeCommandKindV1.Retire, operation, Position(11));
        Assert.Equal(activate, Convert.ToHexString(activatePreimage).ToLowerInvariant());
        Assert.Equal("8a7010709f5e776169dc8894a3f29ffeedce36d85088e27f7eac3a11379ddc98",
            GraphRuntimeEffectHashesV1.Activate(session, operation, Position(7), fingerprint, GraphGenerationId.FromValue(Id(5)), Position(9)).ToString());
        Assert.Equal(retire, Convert.ToHexString(retirePreimage).ToLowerInvariant());
        Assert.Equal("b2ac7d1d2bbc9c8568a67f93a0a42b0217acd30b0035796f484330f33543cb14",
            GraphRuntimeEffectHashesV1.Retire(session, operation, Position(11)).ToString());
    }

    [Fact]
    public void ReceiptHashes_HaveIndependentLengthOneAnd4096Goldens()
    {
        var session = Session(); var operation = OperationId.FromValue(Id(3));
        Hash256.TryParse("8a7010709f5e776169dc8894a3f29ffeedce36d85088e27f7eac3a11379ddc98", out var requestHash);
        var one = GraphRuntimeEffectHashesV1.ReceiptPreimage(session, GraphRuntimeCommandKindV1.Activate, operation, requestHash, new byte[] { 0xa5 });
        const string oneHeader = "6870642d73322d67726170682d72756e74696d652d6566666563742d726563656970742d76310001010101010101010101010101010101020202020202020202020202020202020001030303030303030303030303030303038a7010709f5e776169dc8894a3f29ffeedce36d85088e27f7eac3a11379ddc9800000001";
        Assert.Equal(oneHeader + "a5", Convert.ToHexString(one).ToLowerInvariant());
        Assert.Equal("fc71df49f314e0d460de987ef4f2f6c70a3a1854a3c121b7fb0a692da3f038c6",
            GraphRuntimeEffectHashesV1.Receipt(session, GraphRuntimeCommandKindV1.Activate, operation, requestHash, new byte[] { 0xa5 }).ToString());

        var opaque = Enumerable.Repeat((byte)0xa5, 4096).ToArray();
        var maximum = GraphRuntimeEffectHashesV1.ReceiptPreimage(session, GraphRuntimeCommandKindV1.Activate, operation, requestHash, opaque);
        const string maximumHeader = "6870642d73322d67726170682d72756e74696d652d6566666563742d726563656970742d76310001010101010101010101010101010101020202020202020202020202020202020001030303030303030303030303030303038a7010709f5e776169dc8894a3f29ffeedce36d85088e27f7eac3a11379ddc9800001000";
        Assert.Equal(maximumHeader + string.Concat(Enumerable.Repeat("a5", 4096)), Convert.ToHexString(maximum).ToLowerInvariant());
        Assert.Equal("4ee2416041f5003f04fe306c401fe0758c8e8a4b2ed934f0ce9df4198addc401",
            GraphRuntimeEffectHashesV1.Receipt(session, GraphRuntimeCommandKindV1.Activate, operation, requestHash, opaque).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => GraphRuntimeEffectHashesV1.Receipt(session, GraphRuntimeCommandKindV1.Activate, operation, requestHash, Array.Empty<byte>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => GraphRuntimeEffectHashesV1.Receipt(session, GraphRuntimeCommandKindV1.Activate, operation, requestHash, new byte[4097]));
    }

    [Fact]
    public void FactIds_AreDeterministicAndDomainSeparated()
    {
        var operation = OperationId.FromValue(Id(3));
        var activate = GraphRuntimeFactIdsV1.Command(Session(), operation, GraphRuntimeCommandKindV1.Activate);
        var retire = GraphRuntimeFactIdsV1.Command(Session(), operation, GraphRuntimeCommandKindV1.Retire);
        var result = GraphRuntimeFactIdsV1.Result(Position(17));
        Assert.Equal(JournalFactId.FromValue(StableId128.FromBytes(Convert.FromHexString("bb90fdf3ee3896aab427d9e3ac6fdacf"))), activate);
        Assert.Equal(JournalFactId.FromValue(StableId128.FromBytes(Convert.FromHexString("3dc1cb3c2e406c45afc0702942e220e6"))), result);
        Assert.NotEqual(activate, retire); Assert.NotEqual(activate, result);
        Assert.Throws<ArgumentException>(() => GraphRuntimeFactIdsV1.Result(default));
    }

    [Fact]
    public void Contracts_RejectImpossiblePhaseAndOutcomeCombinations()
    {
        Assert.Throws<ArgumentException>(() => new GraphRuntimeRetirementV1(default, Position(4)));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeOwnerPayloadV1(Session(), null!, new byte[] { 1 }));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeFactV1(Position(8), Position(7), Position(7),
            GraphRuntimeOutcomeV1.Activated, null, null, null));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeFactV1(Position(8), Position(7), Position(7),
            GraphRuntimeOutcomeV1.Conflict, null, null, new BoundedAscii("wrong-code")));
    }

    [Fact]
    public void AuthorityBoundaries_RequireExactGraphAndOwnBody()
    {
        var session = Session();
        var generation = GraphGenerationId.FromValue(Id(5));
        var exact = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(generation)]);
        var empty = ExpectedAuthorityVectorV1.Create(session, []);
        var noGraph = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Activity(ActivityGenerationId.FromValue(Id(6)))]);
        var wrongGraph = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(7)))]);

        Assert.Throws<ArgumentException>(() => new GraphRuntimeOwnerPayloadV1(session, null!, new byte[] { 1 }));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeOwnerPayloadV1(session, empty, new byte[] { 1 }));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeOwnerPayloadV1(session, noGraph, new byte[] { 1 }));
        Assert.Throws<ArgumentException>(() => Snapshot(generation, wrongGraph));
        Assert.Throws<ArgumentException>(() => Snapshot(generation, null!));

        var bytes = new byte[] { 1, 2, 3 };
        var payload = new GraphRuntimeOwnerPayloadV1(session, exact, bytes);
        bytes[0] = 9;
        Assert.Equal(new byte[] { 1, 2, 3 }, payload.Body.ToArray());
        Assert.Equal(GraphRuntimePhaseV1.Active, Snapshot(generation, exact).Phase);
    }

    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
    private static JournalPositionV1 Position(long sequence) => new(Session(), sequence);
    private static StableId128 Id(byte value) => StableId128.FromBytes(Enumerable.Repeat(value, 16).ToArray());
    private static Hash256 Hash(byte value) { Hash256.TryCreate(Enumerable.Repeat(value, 32).ToArray(), out var hash); return hash; }
    private static GraphRuntimeSnapshotV1 Snapshot(GraphGenerationId generation, ExpectedAuthorityVectorV1 authority) =>
        new(GraphRuntimePhaseV1.Active, generation, Hash(4), Position(3), authority,
            OperationId.FromValue(Id(8)), Position(10), Position(10), null);
}
