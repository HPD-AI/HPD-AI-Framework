using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class RouteLifecycleAuthorityRecordCodecsV1Tests
{
    [Fact]
    public void All_seven_route_lifecycle_records_round_trip_with_distinct_hash_domains()
    {
        var operation = OperationId.Create(); var position = new JournalPositionV1(Session(), 7); var authority = Authority(position.Session);
        var request = new RouteRequestAdmittedV1(operation, position, authority, 1);
        var preparation = new RoutePreparationOwnerClaimedV1(operation, position, authority, 2);
        var cutover = new RouteCutoverAuthorizedV1(operation, position, authority, 3);
        var committed = new RouteAuthorityCommittedV1(operation, position, authority, 4);
        var axis = new RouteAxisAppliedV1(operation, position, authority, 5);
        var registration = new RouteRegistrationClosedV1(operation, position, authority, 6);
        var terminal = new RouteTransitionTerminalizedV1(operation, position, authority, 7);
        RoundTrip(request, RouteLifecycleAuthorityRecordCodecsV1.Encode, RouteLifecycleAuthorityRecordCodecsV1.TryDecodeRequest);
        RoundTrip(preparation, RouteLifecycleAuthorityRecordCodecsV1.Encode, RouteLifecycleAuthorityRecordCodecsV1.TryDecodePreparation);
        RoundTrip(cutover, RouteLifecycleAuthorityRecordCodecsV1.Encode, RouteLifecycleAuthorityRecordCodecsV1.TryDecodeCutover);
        RoundTrip(committed, RouteLifecycleAuthorityRecordCodecsV1.Encode, RouteLifecycleAuthorityRecordCodecsV1.TryDecodeAuthority);
        RoundTrip(axis, RouteLifecycleAuthorityRecordCodecsV1.Encode, RouteLifecycleAuthorityRecordCodecsV1.TryDecodeAxis);
        RoundTrip(registration, RouteLifecycleAuthorityRecordCodecsV1.Encode, RouteLifecycleAuthorityRecordCodecsV1.TryDecodeRegistration);
        RoundTrip(terminal, RouteLifecycleAuthorityRecordCodecsV1.Encode, RouteLifecycleAuthorityRecordCodecsV1.TryDecodeTerminal);
        var hashes = new[] { RouteLifecycleAuthorityRecordCodecsV1.ComputeHash(request), RouteLifecycleAuthorityRecordCodecsV1.ComputeHash(preparation), RouteLifecycleAuthorityRecordCodecsV1.ComputeHash(cutover), RouteLifecycleAuthorityRecordCodecsV1.ComputeHash(committed), RouteLifecycleAuthorityRecordCodecsV1.ComputeHash(axis), RouteLifecycleAuthorityRecordCodecsV1.ComputeHash(registration), RouteLifecycleAuthorityRecordCodecsV1.ComputeHash(terminal) };
        Assert.Equal(7, hashes.Distinct().Count());
    }

    [Fact]
    public void Constructor_and_decoder_fail_closed()
    {
        var session = Session(); var position = new JournalPositionV1(session, 1); var authority = Authority(session);
        Assert.Throws<ArgumentException>(() => new RouteRequestAdmittedV1(default, position, authority, 1));
        Assert.Throws<ArgumentException>(() => new RouteRequestAdmittedV1(OperationId.Create(), position, authority, 0));
        var bytes = RouteLifecycleAuthorityRecordCodecsV1.Encode(new RouteRequestAdmittedV1(OperationId.Create(), position, authority, 1));
        Assert.False(RouteLifecycleAuthorityRecordCodecsV1.TryDecodeRequest(bytes.Concat(new byte[] { 0 }).ToArray(), out _));
        Assert.False(RouteLifecycleAuthorityRecordCodecsV1.TryDecodeRequest(new byte[] { 0xff }, out _));
    }

    private static void RoundTrip<T>(T value, Func<T, byte[]> encode, Decoder<T> decode) where T : class { var bytes=encode(value); Assert.True(decode(bytes,out var result)); Assert.Equal(value,result); }
    private delegate bool Decoder<T>(ReadOnlyMemory<byte> bytes, out T? value) where T : class;
    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.Create(), LiveSessionId.Create());
    private static ExpectedAuthorityVectorV1 Authority(SessionAuthorityStampV1 session) => ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Route(RouteGenerationId.Create())]);
}
