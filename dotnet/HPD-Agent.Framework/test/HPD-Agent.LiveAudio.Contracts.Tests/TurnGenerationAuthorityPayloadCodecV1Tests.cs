using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class TurnGenerationAuthorityPayloadCodecV1Tests
{
    [Fact]
    public void Remaining_typed_generation_transitions_round_trip_and_bind_exact_schema_domains()
    {
        var (session, _) = Authority();
        var graph = new GraphGenerationChanged(session, GraphGenerationId.FromValue(Id(20)), GraphGenerationId.FromValue(Id(21)), OwnerSliceId.S2);
        var activity = new ActivityGenerationChanged(session, ActivityGenerationId.FromValue(Id(22)), ActivityGenerationId.FromValue(Id(23)), OwnerSliceId.S3);
        var turn = new TurnGenerationChanged(session, TurnGenerationId.FromValue(Id(24)), TurnGenerationId.FromValue(Id(25)), OwnerSliceId.S4);
        var output = new OutputGenerationChanged(session, OutputGenerationId.FromValue(Id(26)), OutputGenerationId.FromValue(Id(27)), OwnerSliceId.S6);
        var sink = new SinkGenerationChanged(session, SinkGenerationId.FromValue(Id(28)), SinkGenerationId.FromValue(Id(29)), OwnerSliceId.S6);
        var tool = new ToolGenerationChanged(session, ToolGenerationId.FromValue(Id(30)), ToolGenerationId.FromValue(Id(31)), OwnerSliceId.S7);
        var privacy = new PrivacyGenerationChanged(session, PrivacyGenerationId.FromValue(Id(32)), PrivacyGenerationId.FromValue(Id(33)), OwnerSliceId.S9);
        var graphBytes = TurnGenerationRecordCodecs.Encode(graph); var activityBytes = TurnGenerationRecordCodecs.Encode(activity);
        var turnBytes = TurnGenerationRecordCodecs.Encode(turn); var outputBytes = TurnGenerationRecordCodecs.Encode(output);
        var sinkBytes = TurnGenerationRecordCodecs.Encode(sink); var toolBytes = TurnGenerationRecordCodecs.Encode(tool);
        var privacyBytes = TurnGenerationRecordCodecs.Encode(privacy);
        Assert.True(TurnGenerationRecordCodecs.TryDecodeGraph(graphBytes, out var decodedGraph));
        Assert.True(TurnGenerationRecordCodecs.TryDecodeActivity(activityBytes, out var decodedActivity));
        Assert.True(TurnGenerationRecordCodecs.TryDecodeTurnGeneration(turnBytes, out var decodedTurn));
        Assert.True(TurnGenerationRecordCodecs.TryDecodeOutput(outputBytes, out var decodedOutput));
        Assert.True(TurnGenerationRecordCodecs.TryDecodeSink(sinkBytes, out var decodedSink));
        Assert.True(TurnGenerationRecordCodecs.TryDecodeTool(toolBytes, out var decodedTool));
        Assert.True(TurnGenerationRecordCodecs.TryDecodePrivacy(privacyBytes, out var decodedPrivacy));
        Assert.Equal(graph, decodedGraph); Assert.Equal(activity, decodedActivity); Assert.Equal(turn, decodedTurn);
        Assert.Equal(output, decodedOutput); Assert.Equal(sink, decodedSink); Assert.Equal(tool, decodedTool); Assert.Equal(privacy, decodedPrivacy);
        Assert.Equal(7, new[] { TurnGenerationRecordCodecs.ComputeHash(graph), TurnGenerationRecordCodecs.ComputeHash(activity),
            TurnGenerationRecordCodecs.ComputeHash(turn), TurnGenerationRecordCodecs.ComputeHash(output), TurnGenerationRecordCodecs.ComputeHash(sink),
            TurnGenerationRecordCodecs.ComputeHash(tool), TurnGenerationRecordCodecs.ComputeHash(privacy) }.Distinct().Count());
    }

    [Fact]
    public void Typed_turn_and_generation_records_round_trip_and_hash_by_schema()
    {
        var (session, authority) = Authority();
        var turn = new TurnDecisionFinalized(OperationId.FromValue(Id(3)), new JournalPositionV1(session, 7), authority, 2);
        var provider = new ProviderGenerationChanged(session, ProviderGenerationId.FromValue(Id(4)), ProviderGenerationId.FromValue(Id(5)), OwnerSliceId.S5);
        var route = new RouteGenerationChanged(session, RouteGenerationId.FromValue(Id(6)), RouteGenerationId.FromValue(Id(7)), OwnerSliceId.S8);
        var transport = new TransportGenerationChanged(session, TransportGenerationId.FromValue(Id(8)), TransportGenerationId.FromValue(Id(9)), OwnerSliceId.S11);

        var turnBytes = TurnGenerationRecordCodecs.Encode(turn);
        var providerBytes = TurnGenerationRecordCodecs.Encode(provider);
        var routeBytes = TurnGenerationRecordCodecs.Encode(route);
        var transportBytes = TurnGenerationRecordCodecs.Encode(transport);
        Assert.True(TurnGenerationRecordCodecs.TryDecodeTurn(turnBytes, out var decodedTurn));
        Assert.True(TurnGenerationRecordCodecs.TryDecodeProvider(providerBytes, out var decodedProvider));
        Assert.True(TurnGenerationRecordCodecs.TryDecodeRoute(routeBytes, out var decodedRoute));
        Assert.True(TurnGenerationRecordCodecs.TryDecodeTransport(transportBytes, out var decodedTransport));
        Assert.Equal(turnBytes, TurnGenerationRecordCodecs.Encode(decodedTurn!));
        Assert.Equal(providerBytes, TurnGenerationRecordCodecs.Encode(decodedProvider!));
        Assert.Equal(routeBytes, TurnGenerationRecordCodecs.Encode(decodedRoute!));
        Assert.Equal(transportBytes, TurnGenerationRecordCodecs.Encode(decodedTransport!));
        Assert.Equal(OwnerSliceId.S5, decodedProvider!.Owner);
        Assert.Equal(OwnerSliceId.S8, decodedRoute!.Owner);
        Assert.Equal(OwnerSliceId.S11, decodedTransport!.Owner);
        Assert.Equal(4, new[]
        {
            TurnGenerationRecordCodecs.ComputeHash(turn),
            TurnGenerationRecordCodecs.ComputeHash(provider),
            TurnGenerationRecordCodecs.ComputeHash(route),
            TurnGenerationRecordCodecs.ComputeHash(transport)
        }.Distinct().Count());
    }

    [Fact]
    public void Distinct_outer_payloads_own_body_round_trip_and_hash_by_schema()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var turn = new TurnDecisionFinalizedOuter(session, authority, source);
        var graph = new GraphGenerationChangedOuter(session, authority, source);
        var provider = new ProviderGenerationChangedOuter(session, authority, source);
        var route = new RouteGenerationChangedOuter(session, authority, source);
        var transport = new TransportGenerationChangedOuter(session, authority, source);
        source[0] = 99;

        var turnBytes = TurnGenerationAuthorityOuterCodec.Encode(turn);
        var graphBytes = TurnGenerationAuthorityOuterCodec.Encode(graph);
        var providerBytes = TurnGenerationAuthorityOuterCodec.Encode(provider);
        var routeBytes = TurnGenerationAuthorityOuterCodec.Encode(route);
        var transportBytes = TurnGenerationAuthorityOuterCodec.Encode(transport);
        Assert.True(TurnGenerationAuthorityOuterCodec.TryDecodeTurn(turnBytes, out var decodedTurn));
        Assert.True(TurnGenerationAuthorityOuterCodec.TryDecodeGraph(graphBytes, out var decodedGraph));
        Assert.True(TurnGenerationAuthorityOuterCodec.TryDecodeProvider(providerBytes, out var decodedProvider));
        Assert.True(TurnGenerationAuthorityOuterCodec.TryDecodeRoute(routeBytes, out var decodedRoute));
        Assert.True(TurnGenerationAuthorityOuterCodec.TryDecodeTransport(transportBytes, out var decodedTransport));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedTurn!.Body);
        Assert.Equal(turnBytes, TurnGenerationAuthorityOuterCodec.Encode(decodedTurn));
        Assert.Equal(graphBytes, TurnGenerationAuthorityOuterCodec.Encode(decodedGraph!));
        Assert.Equal(providerBytes, TurnGenerationAuthorityOuterCodec.Encode(decodedProvider!));
        Assert.Equal(routeBytes, TurnGenerationAuthorityOuterCodec.Encode(decodedRoute!));
        Assert.Equal(transportBytes, TurnGenerationAuthorityOuterCodec.Encode(decodedTransport!));
        Assert.Equal(5, new[]
        {
            TurnGenerationAuthorityOuterCodec.ComputeHash(turn),
            TurnGenerationAuthorityOuterCodec.ComputeHash(graph),
            TurnGenerationAuthorityOuterCodec.ComputeHash(provider),
            TurnGenerationAuthorityOuterCodec.ComputeHash(route),
            TurnGenerationAuthorityOuterCodec.ComputeHash(transport)
        }.Distinct().Count());
    }

    [Fact]
    public void Constructors_and_decoders_fail_closed_for_invalid_or_noncanonical_values()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(10)), LiveSessionId.FromValue(Id(11)));
        Assert.Throws<ArgumentException>(() => new TurnDecisionFinalized(OperationId.FromValue(Id(3)), new JournalPositionV1(session, 7), ExpectedAuthorityVectorV1.Create(other, []), 2));
        Assert.Throws<ArgumentException>(() => new ProviderGenerationChanged(session, ProviderGenerationId.FromValue(Id(4)), ProviderGenerationId.FromValue(Id(4)), OwnerSliceId.S5));
        Assert.Throws<ArgumentException>(() => new RouteGenerationChanged(session, RouteGenerationId.FromValue(Id(4)), RouteGenerationId.FromValue(Id(5)), OwnerSliceId.S5));
        Assert.Throws<ArgumentException>(() => new TransportGenerationChangedOuter(session, authority, new byte[65_537]));

        var typed = TurnGenerationRecordCodecs.Encode(new TurnDecisionFinalized(OperationId.FromValue(Id(3)), new JournalPositionV1(session, 7), authority, 2));
        Assert.False(TurnGenerationRecordCodecs.TryDecodeTurn(typed.Concat(new byte[] { 0 }).ToArray(), out _));
        var canonical = TurnGenerationAuthorityOuterCodec.Encode(new TurnDecisionFinalizedOuter(session, authority, [7]));
        Assert.False(TurnGenerationAuthorityOuterCodec.TryDecodeTurn(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax); reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]); reordered.WriteEndMap();
        Assert.False(TurnGenerationAuthorityOuterCodec.TryDecodeProvider(reordered.Encode(), out _));
        Assert.False(TurnGenerationAuthorityOuterCodec.TryDecodeRoute(new byte[66_561], out _));
    }

    [Fact]
    public void Registrations_bind_exact_discriminators_owners_and_sessions()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(10)), LiveSessionId.FromValue(Id(11)));
        var values = new (ushort Discriminator, AuthorityPayloadRegistrationV1 Registration, byte[] Payload, OwnerSliceId Owner)[]
        {
            (4, TurnGenerationAuthorityPayloadRegistrations.GraphGenerationChanged, TurnGenerationAuthorityOuterCodec.Encode(new GraphGenerationChangedOuter(session, authority, [])), OwnerSliceId.S2),
            (10, TurnGenerationAuthorityPayloadRegistrations.TurnDecisionFinalized, TurnGenerationAuthorityOuterCodec.Encode(new TurnDecisionFinalizedOuter(session, authority, [])), OwnerSliceId.S4),
            (15, TurnGenerationAuthorityPayloadRegistrations.ProviderGenerationChanged, TurnGenerationAuthorityOuterCodec.Encode(new ProviderGenerationChangedOuter(session, authority, [])), OwnerSliceId.S5),
            (24, TurnGenerationAuthorityPayloadRegistrations.RouteGenerationChanged, TurnGenerationAuthorityOuterCodec.Encode(new RouteGenerationChangedOuter(session, authority, [])), OwnerSliceId.S8),
            (33, TurnGenerationAuthorityPayloadRegistrations.TransportGenerationChanged, TurnGenerationAuthorityOuterCodec.Encode(new TransportGenerationChangedOuter(session, authority, [])), OwnerSliceId.S11)
        };
        Assert.Equal((ushort)4, TurnGenerationAuthorityPayloadRegistrations.GraphGenerationChangedDiscriminator);
        Assert.Equal((ushort)10, TurnGenerationAuthorityPayloadRegistrations.TurnDecisionFinalizedDiscriminator);
        Assert.Equal((ushort)15, TurnGenerationAuthorityPayloadRegistrations.ProviderGenerationChangedDiscriminator);
        Assert.Equal((ushort)24, TurnGenerationAuthorityPayloadRegistrations.RouteGenerationChangedDiscriminator);
        Assert.Equal((ushort)33, TurnGenerationAuthorityPayloadRegistrations.TransportGenerationChangedDiscriminator);
        foreach (var value in values)
        {
            Assert.Equal(value.Owner, value.Registration.Owner);
            Assert.True(value.Registration.Validate(value.Payload, session));
            Assert.False(value.Registration.Validate(value.Payload, other));
        }
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    { var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2))); return (session, ExpectedAuthorityVectorV1.Create(session, [])); }

    private static StableId128 Id(byte value)
    { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); return StableId128.FromBytes(bytes); }
}
