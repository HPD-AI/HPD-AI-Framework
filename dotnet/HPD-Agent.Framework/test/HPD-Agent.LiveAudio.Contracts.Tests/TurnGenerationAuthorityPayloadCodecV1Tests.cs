using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class TurnGenerationAuthorityPayloadCodecV1Tests
{
    [Fact]
    public void Typed_turn_and_generation_records_round_trip_and_hash_by_schema()
    {
        var (session, authority) = Authority();
        var turn = new TurnDecisionFinalizedV1(OperationId.FromValue(Id(3)), new JournalPositionV1(session, 7), authority, 2);
        var provider = new ProviderGenerationChangedV1(session, ProviderGenerationId.FromValue(Id(4)), ProviderGenerationId.FromValue(Id(5)), OwnerSliceId.S5);
        var route = new RouteGenerationChangedV1(session, RouteGenerationId.FromValue(Id(6)), RouteGenerationId.FromValue(Id(7)), OwnerSliceId.S8);
        var transport = new TransportGenerationChangedV1(session, TransportGenerationId.FromValue(Id(8)), TransportGenerationId.FromValue(Id(9)), OwnerSliceId.S11);

        var turnBytes = TurnGenerationRecordCodecsV1.Encode(turn);
        var providerBytes = TurnGenerationRecordCodecsV1.Encode(provider);
        var routeBytes = TurnGenerationRecordCodecsV1.Encode(route);
        var transportBytes = TurnGenerationRecordCodecsV1.Encode(transport);
        Assert.True(TurnGenerationRecordCodecsV1.TryDecodeTurn(turnBytes, out var decodedTurn));
        Assert.True(TurnGenerationRecordCodecsV1.TryDecodeProvider(providerBytes, out var decodedProvider));
        Assert.True(TurnGenerationRecordCodecsV1.TryDecodeRoute(routeBytes, out var decodedRoute));
        Assert.True(TurnGenerationRecordCodecsV1.TryDecodeTransport(transportBytes, out var decodedTransport));
        Assert.Equal(turnBytes, TurnGenerationRecordCodecsV1.Encode(decodedTurn!));
        Assert.Equal(providerBytes, TurnGenerationRecordCodecsV1.Encode(decodedProvider!));
        Assert.Equal(routeBytes, TurnGenerationRecordCodecsV1.Encode(decodedRoute!));
        Assert.Equal(transportBytes, TurnGenerationRecordCodecsV1.Encode(decodedTransport!));
        Assert.Equal(OwnerSliceId.S5, decodedProvider!.Owner);
        Assert.Equal(OwnerSliceId.S8, decodedRoute!.Owner);
        Assert.Equal(OwnerSliceId.S11, decodedTransport!.Owner);
        Assert.Equal(4, new[]
        {
            TurnGenerationRecordCodecsV1.ComputeHash(turn),
            TurnGenerationRecordCodecsV1.ComputeHash(provider),
            TurnGenerationRecordCodecsV1.ComputeHash(route),
            TurnGenerationRecordCodecsV1.ComputeHash(transport)
        }.Distinct().Count());
    }

    [Fact]
    public void Distinct_outer_payloads_own_body_round_trip_and_hash_by_schema()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var turn = new TurnDecisionFinalizedOuterV1(session, authority, source);
        var graph = new GraphGenerationChangedOuterV1(session, authority, source);
        var provider = new ProviderGenerationChangedOuterV1(session, authority, source);
        var route = new RouteGenerationChangedOuterV1(session, authority, source);
        var transport = new TransportGenerationChangedOuterV1(session, authority, source);
        source[0] = 99;

        var turnBytes = TurnGenerationAuthorityOuterCodecV1.Encode(turn);
        var graphBytes = TurnGenerationAuthorityOuterCodecV1.Encode(graph);
        var providerBytes = TurnGenerationAuthorityOuterCodecV1.Encode(provider);
        var routeBytes = TurnGenerationAuthorityOuterCodecV1.Encode(route);
        var transportBytes = TurnGenerationAuthorityOuterCodecV1.Encode(transport);
        Assert.True(TurnGenerationAuthorityOuterCodecV1.TryDecodeTurn(turnBytes, out var decodedTurn));
        Assert.True(TurnGenerationAuthorityOuterCodecV1.TryDecodeGraph(graphBytes, out var decodedGraph));
        Assert.True(TurnGenerationAuthorityOuterCodecV1.TryDecodeProvider(providerBytes, out var decodedProvider));
        Assert.True(TurnGenerationAuthorityOuterCodecV1.TryDecodeRoute(routeBytes, out var decodedRoute));
        Assert.True(TurnGenerationAuthorityOuterCodecV1.TryDecodeTransport(transportBytes, out var decodedTransport));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedTurn!.Body);
        Assert.Equal(turnBytes, TurnGenerationAuthorityOuterCodecV1.Encode(decodedTurn));
        Assert.Equal(graphBytes, TurnGenerationAuthorityOuterCodecV1.Encode(decodedGraph!));
        Assert.Equal(providerBytes, TurnGenerationAuthorityOuterCodecV1.Encode(decodedProvider!));
        Assert.Equal(routeBytes, TurnGenerationAuthorityOuterCodecV1.Encode(decodedRoute!));
        Assert.Equal(transportBytes, TurnGenerationAuthorityOuterCodecV1.Encode(decodedTransport!));
        Assert.Equal(5, new[]
        {
            TurnGenerationAuthorityOuterCodecV1.ComputeHash(turn),
            TurnGenerationAuthorityOuterCodecV1.ComputeHash(graph),
            TurnGenerationAuthorityOuterCodecV1.ComputeHash(provider),
            TurnGenerationAuthorityOuterCodecV1.ComputeHash(route),
            TurnGenerationAuthorityOuterCodecV1.ComputeHash(transport)
        }.Distinct().Count());
    }

    [Fact]
    public void Constructors_and_decoders_fail_closed_for_invalid_or_noncanonical_values()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(10)), LiveSessionId.FromValue(Id(11)));
        Assert.Throws<ArgumentException>(() => new TurnDecisionFinalizedV1(OperationId.FromValue(Id(3)), new JournalPositionV1(session, 7), ExpectedAuthorityVectorV1.Create(other, []), 2));
        Assert.Throws<ArgumentException>(() => new ProviderGenerationChangedV1(session, ProviderGenerationId.FromValue(Id(4)), ProviderGenerationId.FromValue(Id(4)), OwnerSliceId.S5));
        Assert.Throws<ArgumentException>(() => new RouteGenerationChangedV1(session, RouteGenerationId.FromValue(Id(4)), RouteGenerationId.FromValue(Id(5)), OwnerSliceId.S5));
        Assert.Throws<ArgumentException>(() => new TransportGenerationChangedOuterV1(session, authority, new byte[65_537]));

        var typed = TurnGenerationRecordCodecsV1.Encode(new TurnDecisionFinalizedV1(OperationId.FromValue(Id(3)), new JournalPositionV1(session, 7), authority, 2));
        Assert.False(TurnGenerationRecordCodecsV1.TryDecodeTurn(typed.Concat(new byte[] { 0 }).ToArray(), out _));
        var canonical = TurnGenerationAuthorityOuterCodecV1.Encode(new TurnDecisionFinalizedOuterV1(session, authority, [7]));
        Assert.False(TurnGenerationAuthorityOuterCodecV1.TryDecodeTurn(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax); reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]); reordered.WriteEndMap();
        Assert.False(TurnGenerationAuthorityOuterCodecV1.TryDecodeProvider(reordered.Encode(), out _));
        Assert.False(TurnGenerationAuthorityOuterCodecV1.TryDecodeRoute(new byte[66_561], out _));
    }

    [Fact]
    public void Registrations_bind_exact_discriminators_owners_and_sessions()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(10)), LiveSessionId.FromValue(Id(11)));
        var values = new (ushort Discriminator, AuthorityPayloadRegistrationV1 Registration, byte[] Payload, OwnerSliceId Owner)[]
        {
            (4, TurnGenerationAuthorityPayloadRegistrationsV1.GraphGenerationChanged, TurnGenerationAuthorityOuterCodecV1.Encode(new GraphGenerationChangedOuterV1(session, authority, [])), OwnerSliceId.S2),
            (10, TurnGenerationAuthorityPayloadRegistrationsV1.TurnDecisionFinalized, TurnGenerationAuthorityOuterCodecV1.Encode(new TurnDecisionFinalizedOuterV1(session, authority, [])), OwnerSliceId.S4),
            (15, TurnGenerationAuthorityPayloadRegistrationsV1.ProviderGenerationChanged, TurnGenerationAuthorityOuterCodecV1.Encode(new ProviderGenerationChangedOuterV1(session, authority, [])), OwnerSliceId.S5),
            (24, TurnGenerationAuthorityPayloadRegistrationsV1.RouteGenerationChanged, TurnGenerationAuthorityOuterCodecV1.Encode(new RouteGenerationChangedOuterV1(session, authority, [])), OwnerSliceId.S8),
            (33, TurnGenerationAuthorityPayloadRegistrationsV1.TransportGenerationChanged, TurnGenerationAuthorityOuterCodecV1.Encode(new TransportGenerationChangedOuterV1(session, authority, [])), OwnerSliceId.S11)
        };
        Assert.Equal((ushort)4, TurnGenerationAuthorityPayloadRegistrationsV1.GraphGenerationChangedDiscriminator);
        Assert.Equal((ushort)10, TurnGenerationAuthorityPayloadRegistrationsV1.TurnDecisionFinalizedDiscriminator);
        Assert.Equal((ushort)15, TurnGenerationAuthorityPayloadRegistrationsV1.ProviderGenerationChangedDiscriminator);
        Assert.Equal((ushort)24, TurnGenerationAuthorityPayloadRegistrationsV1.RouteGenerationChangedDiscriminator);
        Assert.Equal((ushort)33, TurnGenerationAuthorityPayloadRegistrationsV1.TransportGenerationChangedDiscriminator);
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
