#nullable enable

using System.Net;
using HPD.Media.WebRTC;

namespace HPD.Media.WebRTC.Tests.AotSmoke;

public sealed class FullIceDatagramPathFactoryTests
{
    [Fact]
    public async Task ReadCandidateEventAsync_GathersHostServerReflexiveRelayAndCompletion()
    {
        var stunServer = new IceServerOptions { Uri = new Uri("stun:stun.example.test:3478") };
        var turnServer = new IceServerOptions { Uri = new Uri("turn:turn.example.test:3478") };
        var srflxGatherer = new FixedServerReflexiveGatherer(new IPEndPoint(IPAddress.Parse("203.0.113.10"), 50000));
        var relayAllocator = new FixedRelayAllocator(new IPEndPoint(IPAddress.Parse("198.51.100.44"), 55000));
        await using var factory = new FullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.Full,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            IceServers = new[] { stunServer, turnServer },
            ServerReflexiveCandidateGatherer = srflxGatherer,
            RelayCandidateAllocator = relayAllocator
        });

        IceCandidateEvent? host = await factory.ReadCandidateEventAsync();
        IceCandidateEvent? srflx = await factory.ReadCandidateEventAsync();
        IceCandidateEvent? relay = await factory.ReadCandidateEventAsync();
        IceCandidateEvent? complete = await factory.ReadCandidateEventAsync();
        ValueTask<IceCandidateEvent?> pendingEnd = factory.ReadCandidateEventAsync();

        Assert.Equal(IceMode.Full, factory.Mode);
        Assert.NotNull(host);
        Assert.Equal(IceCandidateType.Host, host.Value.Candidate.CandidateType);
        Assert.NotNull(srflx);
        Assert.Equal(IceCandidateType.ServerReflexive, srflx.Value.Candidate.CandidateType);
        Assert.Equal(stunServer.Uri, srflxGatherer.LastServerUri);
        Assert.StartsWith("candidate:", srflx.Value.SignalingCandidate.Candidate, StringComparison.Ordinal);
        Assert.NotNull(relay);
        Assert.Equal(IceCandidateType.Relay, relay.Value.Candidate.CandidateType);
        Assert.Equal(turnServer.Uri, relayAllocator.LastServerUri);
        Assert.StartsWith("candidate:", relay.Value.SignalingCandidate.Candidate, StringComparison.Ordinal);
        Assert.NotNull(complete);
        Assert.Equal(IceCandidateEventKind.LocalCandidateGatheringComplete, complete.Value.Kind);
        Assert.False(pendingEnd.IsCompleted);
        await factory.DisposeAsync();
        IceCandidateEvent? end = await pendingEnd;
        Assert.Null(end);
    }

    [Fact]
    public async Task RelayOnlyPolicy_GathersRelayCandidateWithoutHostOrServerReflexiveCandidates()
    {
        var srflxGatherer = new FixedServerReflexiveGatherer(new IPEndPoint(IPAddress.Parse("203.0.113.10"), 50000));
        var relayAllocator = new FixedRelayAllocator(new IPEndPoint(IPAddress.Parse("198.51.100.44"), 55000));
        await using var factory = new FullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.Full,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            GatheringPolicy = IceGatheringPolicy.RelayOnly,
            IceServers = new[]
            {
                new IceServerOptions { Uri = new Uri("stun:stun.example.test:3478") },
                new IceServerOptions { Uri = new Uri("turn:turn.example.test:3478") }
            },
            ServerReflexiveCandidateGatherer = srflxGatherer,
            RelayCandidateAllocator = relayAllocator
        });

        IceCandidateEvent? relay = await factory.ReadCandidateEventAsync();
        IceCandidateEvent? complete = await factory.ReadCandidateEventAsync();
        ValueTask<IceCandidateEvent?> pendingEnd = factory.ReadCandidateEventAsync();

        Assert.NotNull(relay);
        Assert.Equal(IceCandidateEventKind.LocalCandidateDiscovered, relay.Value.Kind);
        Assert.Equal(IceCandidateType.Relay, relay.Value.Candidate.CandidateType);
        Assert.Null(srflxGatherer.LastServerUri);
        Assert.NotNull(relayAllocator.LastServerUri);
        Assert.NotNull(complete);
        Assert.Equal(IceCandidateEventKind.LocalCandidateGatheringComplete, complete.Value.Kind);
        Assert.False(pendingEnd.IsCompleted);
        await factory.DisposeAsync();
        IceCandidateEvent? end = await pendingEnd;
        Assert.Null(end);
    }

    [Fact]
    public async Task IceLiteFactory_ExposesIceLiteModeAndHostGathering()
    {
        await using var factory = new IceLiteDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.IceLite,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        IceCandidateEvent? candidate = await factory.ReadCandidateEventAsync();

        Assert.Equal(IceMode.IceLite, factory.Mode);
        Assert.NotNull(candidate);
        Assert.Equal(IceCandidateType.Host, candidate.Value.Candidate.CandidateType);
    }

    private sealed class FixedServerReflexiveGatherer(IPEndPoint endPoint) : IIceServerReflexiveCandidateGatherer
    {
        public Uri? LastServerUri { get; private set; }

        public ValueTask<IceCandidate?> GatherAsync(
            IceServerOptions server,
            IPEndPoint localEndPoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastServerUri = server.Uri;
            return new ValueTask<IceCandidate?>(new IceCandidate
            {
                Foundation = "srflx-1",
                ComponentId = 1,
                Transport = "UDP",
                Priority = 1_690_000_000,
                EndPoint = endPoint,
                CandidateType = IceCandidateType.ServerReflexive,
                ExtensionAttributes = new[]
                {
                    new IceCandidateAttribute { Name = "raddr", Value = localEndPoint.Address.ToString() },
                    new IceCandidateAttribute { Name = "rport", Value = localEndPoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                }
            });
        }
    }

    private sealed class FixedRelayAllocator(IPEndPoint endPoint) : IIceRelayCandidateAllocator
    {
        public Uri? LastServerUri { get; private set; }

        public ValueTask<IceCandidate?> AllocateAsync(
            IceServerOptions server,
            IPEndPoint localEndPoint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastServerUri = server.Uri;
            return new ValueTask<IceCandidate?>(new IceCandidate
            {
                Foundation = "relay-1",
                ComponentId = 1,
                Transport = "UDP",
                Priority = 100_000,
                EndPoint = endPoint,
                CandidateType = IceCandidateType.Relay,
                ExtensionAttributes = new[]
                {
                    new IceCandidateAttribute { Name = "raddr", Value = localEndPoint.Address.ToString() },
                    new IceCandidateAttribute { Name = "rport", Value = localEndPoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                }
            });
        }
    }
}
