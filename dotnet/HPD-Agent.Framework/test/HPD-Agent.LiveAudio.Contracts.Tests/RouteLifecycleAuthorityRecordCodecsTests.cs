using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class RouteLifecycleAuthorityRecordCodecsTests
{
    [Fact]
    public void All_seven_route_lifecycle_records_round_trip_with_distinct_hash_domains()
    {
        var operation = OperationId.Create(); var position = new JournalPositionV1(Session(), 7); var authority = Authority(position.Session);
        var request = new RouteRequestAdmitted(operation, position, authority, 1);
        var preparation = new RoutePreparationOwnerClaimed(operation, position, authority, 2);
        var cutover = new RouteCutoverAuthorized(operation, position, authority, 3);
        var committed = new RouteAuthorityCommitted(operation, position, authority, 4);
        var axis = new RouteAxisApplied(operation, position, authority, 5);
        var registration = new RouteRegistrationClosed(operation, position, authority, 6);
        var terminal = new RouteTransitionTerminalized(operation, position, authority, 7);
        RoundTrip(request, RouteLifecycleAuthorityRecordCodecs.Encode, RouteLifecycleAuthorityRecordCodecs.TryDecodeRequest);
        RoundTrip(preparation, RouteLifecycleAuthorityRecordCodecs.Encode, RouteLifecycleAuthorityRecordCodecs.TryDecodePreparation);
        RoundTrip(cutover, RouteLifecycleAuthorityRecordCodecs.Encode, RouteLifecycleAuthorityRecordCodecs.TryDecodeCutover);
        RoundTrip(committed, RouteLifecycleAuthorityRecordCodecs.Encode, RouteLifecycleAuthorityRecordCodecs.TryDecodeAuthority);
        RoundTrip(axis, RouteLifecycleAuthorityRecordCodecs.Encode, RouteLifecycleAuthorityRecordCodecs.TryDecodeAxis);
        RoundTrip(registration, RouteLifecycleAuthorityRecordCodecs.Encode, RouteLifecycleAuthorityRecordCodecs.TryDecodeRegistration);
        RoundTrip(terminal, RouteLifecycleAuthorityRecordCodecs.Encode, RouteLifecycleAuthorityRecordCodecs.TryDecodeTerminal);
        var hashes = new[] { RouteLifecycleAuthorityRecordCodecs.ComputeHash(request), RouteLifecycleAuthorityRecordCodecs.ComputeHash(preparation), RouteLifecycleAuthorityRecordCodecs.ComputeHash(cutover), RouteLifecycleAuthorityRecordCodecs.ComputeHash(committed), RouteLifecycleAuthorityRecordCodecs.ComputeHash(axis), RouteLifecycleAuthorityRecordCodecs.ComputeHash(registration), RouteLifecycleAuthorityRecordCodecs.ComputeHash(terminal) };
        Assert.Equal(7, hashes.Distinct().Count());
    }

    [Fact]
    public void Constructor_and_decoder_fail_closed()
    {
        var session = Session(); var position = new JournalPositionV1(session, 1); var authority = Authority(session);
        Assert.Throws<ArgumentException>(() => new RouteRequestAdmitted(default, position, authority, 1));
        Assert.Throws<ArgumentException>(() => new RouteRequestAdmitted(OperationId.Create(), position, authority, 0));
        var bytes = RouteLifecycleAuthorityRecordCodecs.Encode(new RouteRequestAdmitted(OperationId.Create(), position, authority, 1));
        Assert.False(RouteLifecycleAuthorityRecordCodecs.TryDecodeRequest(bytes.Concat(new byte[] { 0 }).ToArray(), out _));
        Assert.False(RouteLifecycleAuthorityRecordCodecs.TryDecodeRequest(new byte[] { 0xff }, out _));
    }

    private static void RoundTrip<T>(T value, Func<T, byte[]> encode, Decoder<T> decode) where T : class { var bytes=encode(value); Assert.True(decode(bytes,out var result)); Assert.Equal(value,result); }
    private delegate bool Decoder<T>(ReadOnlyMemory<byte> bytes, out T? value) where T : class;
    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.Create(), LiveSessionId.Create());
    private static ExpectedAuthorityVectorV1 Authority(SessionAuthorityStampV1 session) => ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Route(RouteGenerationId.Create())]);
}
