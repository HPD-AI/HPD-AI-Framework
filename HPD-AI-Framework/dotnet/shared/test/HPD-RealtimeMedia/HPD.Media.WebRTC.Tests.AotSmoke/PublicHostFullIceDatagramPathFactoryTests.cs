#nullable enable

using System.Net;
using System.Net.Sockets;
using HPD.Media.Transport;
using HPD.Media.WebRTC;

namespace HPD.Media.WebRTC.Tests.AotSmoke;

public sealed class PublicHostFullIceDatagramPathFactoryTests
{
    [Fact]
    public async Task Constructor_GathersHostCandidateAndCompletionEvent()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());

        IceCandidateEvent? candidateEvent = await factory.ReadCandidateEventAsync();
        IceCandidateEvent? completeEvent = await factory.ReadCandidateEventAsync();
        ValueTask<IceCandidateEvent?> pendingEnd = factory.ReadCandidateEventAsync();

        Assert.NotNull(candidateEvent);
        Assert.Equal(IceCandidateEventKind.LocalCandidateDiscovered, candidateEvent.Value.Kind);
        Assert.Equal(IceCandidateType.Host, candidateEvent.Value.Candidate.CandidateType);
        Assert.NotNull(candidateEvent.Value.Candidate.EndPoint);
        Assert.StartsWith("candidate:", candidateEvent.Value.SignalingCandidate.Candidate, StringComparison.Ordinal);
        Assert.NotNull(completeEvent);
        Assert.Equal(IceCandidateEventKind.LocalCandidateGatheringComplete, completeEvent.Value.Kind);
        Assert.False(pendingEnd.IsCompleted);
        await factory.DisposeAsync();
        IceCandidateEvent? end = await pendingEnd;
        Assert.Null(end);
    }

    [Fact]
    public async Task RelayOnlyPolicy_DoesNotEmitHostCandidate()
    {
        IceDatagramPathOptions options = CreateOptions(IceGatheringPolicy.RelayOnly);
        await using var factory = new PublicHostFullIceDatagramPathFactory(options);

        IceCandidateEvent? onlyEvent = await factory.ReadCandidateEventAsync();
        ValueTask<IceCandidateEvent?> pendingEnd = factory.ReadCandidateEventAsync();

        Assert.NotNull(onlyEvent);
        Assert.Equal(IceCandidateEventKind.LocalCandidateGatheringComplete, onlyEvent.Value.Kind);
        Assert.False(pendingEnd.IsCompleted);
        await factory.DisposeAsync();
        IceCandidateEvent? end = await pendingEnd;
        Assert.Null(end);
    }

    [Fact]
    public void Constructor_RejectsInvalidIceOptionsBeforeOpeningSocket()
    {
        Assert.Throws<ArgumentNullException>(() => new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = null!
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            GatheringPolicy = (IceGatheringPolicy)99
        }));
        Assert.Throws<ArgumentException>(() => new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            AdvertisedAddress = IPAddress.IPv6Loopback
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            CheckInterval = TimeSpan.Zero
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            MdnsResolutionTimeout = TimeSpan.Zero
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            MdnsResolutionRetryCount = -1
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            DegradedTimeout = TimeSpan.FromSeconds(8),
            FailedTimeout = TimeSpan.FromSeconds(8)
        }));
        Assert.Throws<ArgumentException>(() => new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            IceServers = new[] { new IceServerOptions { Uri = new Uri("https://example.test/ice") } }
        }));
    }

    [Fact]
    public async Task AddRemoteCandidate_RejectsMdnsWhenResolutionUnavailable()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            EnableMdnsCandidateResolution = false
        });
        DrainCandidateEvents(factory);
        var candidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            Port = 60769,
            MdnsHostName = "a1b2c3d4.local",
            CandidateType = IceCandidateType.Host
        };

        await factory.AddRemoteCandidateAsync(candidate);
        IceCandidateEvent? rejected = await factory.ReadCandidateEventAsync();

        Assert.NotNull(rejected);
        Assert.Equal(IceCandidateEventKind.CandidateRejected, rejected.Value.Kind);
        Assert.Equal(IceCandidateRejectReason.MdnsResolutionFailed, rejected.Value.RejectReason);
    }

    [Fact]
    public async Task AddRemoteCandidate_ResolvesMdnsCandidateWhenResolverIsConfigured()
    {
        var resolver = new FixedMdnsResolver(IPAddress.Loopback);
        await using var factory = new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            EnableMdnsCandidateResolution = true,
            MdnsResolver = resolver
        });
        DrainCandidateEvents(factory);
        var candidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            Port = 60769,
            MdnsHostName = "a1b2c3d4.local",
            CandidateType = IceCandidateType.Host
        };

        await factory.AddRemoteCandidateAsync(candidate);
        IceCandidateEvent? accepted = await factory.ReadCandidateEventAsync();

        Assert.Equal("a1b2c3d4.local", resolver.LastHostName);
        Assert.NotNull(accepted);
        Assert.Equal(IceCandidateEventKind.RemoteCandidateAccepted, accepted.Value.Kind);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 60769), accepted.Value.Candidate.EndPoint);
        Assert.Equal(candidate.MdnsHostName, accepted.Value.Candidate.MdnsHostName);
    }

    [Fact]
    public async Task AddRemoteCandidate_RetriesMdnsResolutionAfterNullResult()
    {
        var resolver = new RetryMdnsResolver(null, IPAddress.Loopback);
        await using var factory = new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            EnableMdnsCandidateResolution = true,
            MdnsResolver = resolver,
            MdnsResolutionRetryCount = 1,
            MdnsResolutionTimeout = TimeSpan.FromSeconds(1)
        });
        DrainCandidateEvents(factory);
        var candidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            Port = 60769,
            MdnsHostName = "a1b2c3d4.local",
            CandidateType = IceCandidateType.Host
        };

        await factory.AddRemoteCandidateAsync(candidate);
        IceCandidateEvent? accepted = await factory.ReadCandidateEventAsync();

        Assert.Equal(2, resolver.AttemptCount);
        Assert.NotNull(accepted);
        Assert.Equal(IceCandidateEventKind.RemoteCandidateAccepted, accepted.Value.Kind);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 60769), accepted.Value.Candidate.EndPoint);
    }

    [Fact]
    public async Task AddRemoteCandidate_TimesOutMdnsResolutionAttemptAndRejects()
    {
        var resolver = new DelayedMdnsResolver(TimeSpan.FromSeconds(5), IPAddress.Loopback);
        await using var factory = new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            EnableMdnsCandidateResolution = true,
            MdnsResolver = resolver,
            MdnsResolutionTimeout = TimeSpan.FromMilliseconds(10)
        });
        DrainCandidateEvents(factory);
        var candidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            Port = 60769,
            MdnsHostName = "a1b2c3d4.local",
            CandidateType = IceCandidateType.Host
        };

        await factory.AddRemoteCandidateAsync(candidate);
        IceCandidateEvent? rejected = await factory.ReadCandidateEventAsync();

        Assert.Equal(1, resolver.AttemptCount);
        Assert.NotNull(rejected);
        Assert.Equal(IceCandidateEventKind.CandidateRejected, rejected.Value.Kind);
        Assert.Equal(IceCandidateRejectReason.MdnsResolutionFailed, rejected.Value.RejectReason);
    }

    [Fact]
    public async Task AddRemoteCandidate_RejectsMalformedDirectCandidateShapes()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        DrainCandidateEvents(factory);
        var baseCandidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            EndPoint = new IPEndPoint(IPAddress.Loopback, 60769),
            CandidateType = IceCandidateType.Host
        };

        await factory.AddRemoteCandidateAsync(baseCandidate with { ComponentId = 3 });
        await factory.AddRemoteCandidateAsync(baseCandidate with { Foundation = "bad foundation" });
        await factory.AddRemoteCandidateAsync(baseCandidate with { CandidateType = (IceCandidateType)99 });
        await factory.AddRemoteCandidateAsync(baseCandidate with
        {
            ExtensionAttributes = new[] { new IceCandidateAttribute { Name = "network-id", Value = "1 2" } }
        });

        for (int i = 0; i < 4; i++)
        {
            IceCandidateEvent? rejected = await factory.ReadCandidateEventAsync();
            Assert.NotNull(rejected);
            Assert.Equal(IceCandidateEventKind.CandidateRejected, rejected.Value.Kind);
            Assert.Equal(IceCandidateRejectReason.InvalidSyntax, rejected.Value.RejectReason);
        }

        ValueTask<IceCandidateEvent?> pendingEnd = factory.ReadCandidateEventAsync();
        Assert.False(pendingEnd.IsCompleted);
        await factory.DisposeAsync();
        Assert.Null(await pendingEnd);
    }

    [Fact]
    public async Task AddRemoteCandidate_RejectsMismatchedAddressFamily()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        DrainCandidateEvents(factory);
        var candidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            EndPoint = new IPEndPoint(IPAddress.IPv6Loopback, 60769),
            CandidateType = IceCandidateType.Host
        };

        await factory.AddRemoteCandidateAsync(candidate);
        IceCandidateEvent? rejected = await factory.ReadCandidateEventAsync();

        Assert.NotNull(rejected);
        Assert.Equal(IceCandidateEventKind.CandidateRejected, rejected.Value.Kind);
        Assert.Equal(IceCandidateRejectReason.InvalidSyntax, rejected.Value.RejectReason);
    }

    [Fact]
    public async Task AddRemoteCandidate_RejectsMdnsResolutionWithMismatchedAddressFamily()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            EnableMdnsCandidateResolution = true,
            MdnsResolver = new FixedMdnsResolver(IPAddress.IPv6Loopback)
        });
        DrainCandidateEvents(factory);
        var candidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            Port = 60769,
            MdnsHostName = "a1b2c3d4.local",
            CandidateType = IceCandidateType.Host
        };

        await factory.AddRemoteCandidateAsync(candidate);
        IceCandidateEvent? rejected = await factory.ReadCandidateEventAsync();

        Assert.NotNull(rejected);
        Assert.Equal(IceCandidateEventKind.CandidateRejected, rejected.Value.Kind);
        Assert.Equal(IceCandidateRejectReason.InvalidSyntax, rejected.Value.RejectReason);
    }

    [Fact]
    public async Task AddRemoteCandidate_RejectsDuplicateCandidate()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        DrainCandidateEvents(factory);
        var candidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            EndPoint = new IPEndPoint(IPAddress.Loopback, 60769),
            CandidateType = IceCandidateType.Host
        };

        await factory.AddRemoteCandidateAsync(candidate);
        await factory.AddRemoteCandidateAsync(candidate);
        IceCandidateEvent? accepted = await factory.ReadCandidateEventAsync();
        IceCandidateEvent? duplicate = await factory.ReadCandidateEventAsync();

        Assert.NotNull(accepted);
        Assert.Equal(IceCandidateEventKind.RemoteCandidateAccepted, accepted.Value.Kind);
        Assert.NotNull(duplicate);
        Assert.Equal(IceCandidateEventKind.CandidateRejected, duplicate.Value.Kind);
        Assert.Equal(IceCandidateRejectReason.Duplicate, duplicate.Value.RejectReason);
    }

    [Fact]
    public async Task AddRemoteCandidate_RejectsMdnsCandidateWhenResolverReturnsNull()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            EnableMdnsCandidateResolution = true,
            MdnsResolver = new FixedMdnsResolver(null)
        });
        DrainCandidateEvents(factory);
        var candidate = new IceCandidate
        {
            Foundation = "2",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            Port = 60769,
            MdnsHostName = "a1b2c3d4.local",
            CandidateType = IceCandidateType.Host
        };

        await factory.AddRemoteCandidateAsync(candidate);
        IceCandidateEvent? rejected = await factory.ReadCandidateEventAsync();

        Assert.NotNull(rejected);
        Assert.Equal(IceCandidateEventKind.CandidateRejected, rejected.Value.Kind);
        Assert.Equal(IceCandidateRejectReason.MdnsResolutionFailed, rejected.Value.RejectReason);
    }

    [Fact]
    public async Task ConnectAsync_ReturnsSelectedUdpPathThatCanExchangeDatagrams()
    {
        await using var leftFactory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        await using var rightFactory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        IceCandidate leftCandidate = ReadLocalCandidate(leftFactory);
        IceCandidate rightCandidate = ReadLocalCandidate(rightFactory);

        await leftFactory.SetRemoteCredentialsAsync(rightFactory.LocalCredentials);
        await rightFactory.SetRemoteCredentialsAsync(leftFactory.LocalCredentials);
        await leftFactory.AddRemoteCandidateAsync(rightCandidate);
        await rightFactory.AddRemoteCandidateAsync(leftCandidate);

        await using IDatagramPath leftPath = await leftFactory.ConnectAsync();
        await using IDatagramPath rightPath = await rightFactory.ConnectAsync();
        byte[] payload = [0x80, 0x60, 0x00, 0x01, 0xAA, 0xBB, 0xCC, 0xDD];
        byte[] receiveBuffer = new byte[64];

        await leftPath.SendAsync(payload);
        DatagramReceiveResult received = await rightPath.ReceiveAsync(receiveBuffer);

        Assert.True(received.HasDatagram);
        Assert.Equal(payload.Length, received.BytesWritten);
        Assert.Equal(payload, receiveBuffer.AsSpan(0, received.BytesWritten).ToArray());
        Assert.Equal(leftCandidate.EndPoint, received.RemoteEndPoint);
        Assert.Equal(DatagramProtocolHint.SrtpOrSrtcp, received.Hint);
    }

    [Fact]
    public async Task ConnectedPath_RespondsToStunBindingRequestsWithoutSurfacingThemAsMedia()
    {
        using var remoteSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        remoteSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var remoteEndPoint = (IPEndPoint)remoteSocket.LocalEndPoint!;
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        IceCandidate localCandidate = ReadLocalCandidate(factory);
        await factory.SetRemoteCredentialsAsync(new IceCredentials
        {
            UsernameFragment = "remoteUfrag",
            Password = "remotePassword"
        });
        await factory.AddRemoteCandidateAsync(new IceCandidate
        {
            Foundation = "remote",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            EndPoint = remoteEndPoint,
            CandidateType = IceCandidateType.Host
        });
        await using IDatagramPath path = await factory.ConnectAsync();
        byte[] mediaBuffer = new byte[64];
        ValueTask<DatagramReceiveResult> pendingReceive = path.ReceiveAsync(mediaBuffer);
        Assert.False(pendingReceive.IsCompleted);
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        transactionId.AsSpan().Fill(0x5A);
        byte[] stunRequest = new byte[StunBindingMessage.HeaderLength];
        Assert.True(StunBindingMessage.TryWriteBindingRequest(stunRequest, transactionId, out int stunBytes));

        _ = await remoteSocket.SendToAsync(stunRequest.AsMemory(0, stunBytes), SocketFlags.None, localCandidate.EndPoint!);
        byte[] stunResponse = new byte[64];
        EndPoint responseRemote = new IPEndPoint(IPAddress.Any, 0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        SocketReceiveFromResult response = await remoteSocket.ReceiveFromAsync(
            stunResponse,
            SocketFlags.None,
            responseRemote,
            timeout.Token);
        byte[] media = [0x80, 0x60, 0x00, 0x01];
        _ = await remoteSocket.SendToAsync(media, SocketFlags.None, localCandidate.EndPoint!);
        DatagramReceiveResult received = await pendingReceive;

        Assert.Equal(localCandidate.EndPoint, response.RemoteEndPoint);
        Assert.True(StunBindingMessage.TryParseBindingSuccessResponse(
            stunResponse.AsSpan(0, response.ReceivedBytes),
            transactionId,
            out IPEndPoint mappedEndPoint));
        Assert.Equal(remoteEndPoint, mappedEndPoint);
        Assert.True(received.HasDatagram);
        Assert.Equal(media.Length, received.BytesWritten);
        Assert.Equal(media, mediaBuffer.AsSpan(0, received.BytesWritten).ToArray());
        Assert.Equal(DatagramProtocolHint.SrtpOrSrtcp, received.Hint);
    }

    [Fact]
    public async Task ConnectedPath_DropsMalformedStunControlDatagramsWithoutSurfacingThemAsMedia()
    {
        using var remoteSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        remoteSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var remoteEndPoint = (IPEndPoint)remoteSocket.LocalEndPoint!;
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        IceCandidate localCandidate = ReadLocalCandidate(factory);
        await factory.SetRemoteCredentialsAsync(new IceCredentials
        {
            UsernameFragment = "remoteUfrag",
            Password = "remotePassword"
        });
        await factory.AddRemoteCandidateAsync(new IceCandidate
        {
            Foundation = "remote",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            EndPoint = remoteEndPoint,
            CandidateType = IceCandidateType.Host
        });
        await using IDatagramPath path = await factory.ConnectAsync();
        byte[] mediaBuffer = new byte[64];
        ValueTask<DatagramReceiveResult> pendingReceive = path.ReceiveAsync(mediaBuffer);
        Assert.False(pendingReceive.IsCompleted);
        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        transactionId.AsSpan().Fill(0x6B);
        byte[] nonRequestStun = new byte[64];
        Assert.True(StunBindingMessage.TryWriteBindingSuccessResponse(
            nonRequestStun,
            transactionId,
            remoteEndPoint,
            out int nonRequestBytes));

        _ = await remoteSocket.SendToAsync(nonRequestStun.AsMemory(0, nonRequestBytes), SocketFlags.None, localCandidate.EndPoint!);
        byte[] media = [0x80, 0x60, 0x00, 0x02];
        _ = await remoteSocket.SendToAsync(media, SocketFlags.None, localCandidate.EndPoint!);
        DatagramReceiveResult received = await pendingReceive;

        Assert.True(received.HasDatagram);
        Assert.Equal(media.Length, received.BytesWritten);
        Assert.Equal(media, mediaBuffer.AsSpan(0, received.BytesWritten).ToArray());
        Assert.Equal(DatagramProtocolHint.SrtpOrSrtcp, received.Hint);
    }

    [Fact]
    public async Task ConnectAsync_WrapsRelayCandidatePathWithTurnChannelData()
    {
        using var relaySocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        relaySocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var relayEndPoint = (IPEndPoint)relaySocket.LocalEndPoint!;
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        IceCandidate localCandidate = ReadLocalCandidate(factory);
        const ushort ChannelNumber = 0x4006;
        var relayCandidate = new IceCandidate
        {
            Foundation = "relay",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 900,
            EndPoint = relayEndPoint,
            CandidateType = IceCandidateType.Relay,
            ExtensionAttributes = new[]
            {
                new IceCandidateAttribute
                {
                    Name = "turn-channel",
                    Value = ChannelNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            }
        };

        await factory.SetRemoteCredentialsAsync(new IceCredentials
        {
            UsernameFragment = "relayUfrag",
            Password = "relayPassword"
        });
        await factory.AddRemoteCandidateAsync(relayCandidate);
        await using IDatagramPath path = await factory.ConnectAsync();
        byte[] outboundPayload = [0x80, 0x61, 0x00, 0x06];

        await path.SendAsync(outboundPayload);
        byte[] encodedOutbound = new byte[32];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        SocketReceiveFromResult outbound = await relaySocket.ReceiveFromAsync(
            encodedOutbound,
            SocketFlags.None,
            new IPEndPoint(IPAddress.Any, 0),
            timeout.Token);

        Assert.Equal(TurnChannelDataStatus.Success, TurnChannelDataMessage.TryParse(
            encodedOutbound.AsSpan(0, outbound.ReceivedBytes),
            out TurnChannelDataView outboundView));
        Assert.Equal(ChannelNumber, outboundView.ChannelNumber);
        Assert.True(outboundView.Payload.SequenceEqual(outboundPayload));

        byte[] inboundPayload = [0x80, 0x61, 0x00, 0x07];
        byte[] encodedInbound = new byte[32];
        Assert.Equal(TurnChannelDataStatus.Success, TurnChannelDataMessage.TryWrite(
            ChannelNumber,
            inboundPayload,
            encodedInbound,
            out int encodedInboundBytes));
        _ = await relaySocket.SendToAsync(
            encodedInbound.AsMemory(0, encodedInboundBytes),
            SocketFlags.None,
            localCandidate.EndPoint!);
        byte[] receiveBuffer = new byte[32];
        DatagramReceiveResult received = await path.ReceiveAsync(receiveBuffer, timeout.Token);

        Assert.True(received.HasDatagram);
        Assert.Equal(inboundPayload.Length, received.BytesWritten);
        Assert.Equal(inboundPayload, receiveBuffer.AsSpan(0, received.BytesWritten).ToArray());
        Assert.Equal(DatagramProtocolHint.SrtpOrSrtcp, received.Hint);
    }

    [Fact]
    public async Task AddRemoteCandidate_RejectsInvalidTurnChannelAttribute()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        DrainCandidateEvents(factory);
        var candidate = new IceCandidate
        {
            Foundation = "relay",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            EndPoint = new IPEndPoint(IPAddress.Loopback, 41020),
            CandidateType = IceCandidateType.Relay,
            ExtensionAttributes = new[]
            {
                new IceCandidateAttribute { Name = "turn-channel", Value = "1" }
            }
        };

        await factory.AddRemoteCandidateAsync(candidate);
        IceCandidateEvent? rejected = await factory.ReadCandidateEventAsync();

        Assert.NotNull(rejected);
        Assert.Equal(IceCandidateEventKind.CandidateRejected, rejected.Value.Kind);
        Assert.Equal(IceCandidateRejectReason.InvalidSyntax, rejected.Value.RejectReason);
    }

    [Fact]
    public async Task ConnectAsync_UsesConfiguredConnectivityCheckerBeforeSelectingPair()
    {
        var checker = new FixedConnectivityChecker(succeeds: true);
        await using var leftFactory = new PublicHostFullIceDatagramPathFactory(CreateOptions(connectivityChecker: checker));
        await using var rightFactory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        IceCandidate leftCandidate = ReadLocalCandidate(leftFactory);
        IceCandidate rightCandidate = ReadLocalCandidate(rightFactory);

        await leftFactory.SetRemoteCredentialsAsync(rightFactory.LocalCredentials);
        await leftFactory.AddRemoteCandidateAsync(rightCandidate);
        await using IDatagramPath path = await leftFactory.ConnectAsync();

        Assert.NotNull(path);
        Assert.Equal(1, checker.CheckCount);
        Assert.Equal(leftCandidate.EndPoint, checker.LastLocalCandidate?.EndPoint);
        Assert.Equal(rightCandidate.EndPoint, checker.LastRemoteCandidate?.EndPoint);
        Assert.Equal(rightFactory.LocalCredentials.UsernameFragment, checker.LastRemoteCredentials?.UsernameFragment);
    }

    [Fact]
    public async Task ConnectAsync_SelectsHighestPriorityAcceptedRemoteCandidate()
    {
        var checker = new FixedConnectivityChecker(succeeds: true);
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions(connectivityChecker: checker));
        _ = ReadLocalCandidate(factory);
        var lowPriority = new IceCandidate
        {
            Foundation = "low",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            EndPoint = new IPEndPoint(IPAddress.Loopback, 41000),
            CandidateType = IceCandidateType.Host
        };
        var highPriority = new IceCandidate
        {
            Foundation = "high",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 900,
            EndPoint = new IPEndPoint(IPAddress.Loopback, 41001),
            CandidateType = IceCandidateType.Host
        };

        await factory.SetRemoteCredentialsAsync(new IceCredentials
        {
            UsernameFragment = "remoteUfrag",
            Password = "remotePassword"
        });
        await factory.AddRemoteCandidateAsync(lowPriority);
        await factory.AddRemoteCandidateAsync(highPriority);
        await using IDatagramPath path = await factory.ConnectAsync();

        Assert.NotNull(path);
        Assert.Equal(1, checker.CheckCount);
        Assert.Equal(highPriority.EndPoint, checker.LastRemoteCandidate?.EndPoint);
        Assert.Equal(highPriority.Priority, checker.LastRemoteCandidate?.Priority);
        Assert.Equal(highPriority.EndPoint, path.RemoteEndPoint);
    }

    [Fact]
    public async Task ConnectAsync_FallsBackToNextPriorityCandidateWhenCheckFails()
    {
        var checker = new SequenceConnectivityChecker(false, true);
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions(connectivityChecker: checker));
        _ = ReadLocalCandidate(factory);
        var lowPriority = new IceCandidate
        {
            Foundation = "low",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 100,
            EndPoint = new IPEndPoint(IPAddress.Loopback, 41010),
            CandidateType = IceCandidateType.Host
        };
        var highPriority = new IceCandidate
        {
            Foundation = "high",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 900,
            EndPoint = new IPEndPoint(IPAddress.Loopback, 41011),
            CandidateType = IceCandidateType.Host
        };

        await factory.SetRemoteCredentialsAsync(new IceCredentials
        {
            UsernameFragment = "remoteUfrag",
            Password = "remotePassword"
        });
        await factory.AddRemoteCandidateAsync(lowPriority);
        await factory.AddRemoteCandidateAsync(highPriority);
        await using IDatagramPath path = await factory.ConnectAsync();

        Assert.Equal(2, checker.CheckedRemoteCandidates.Count);
        Assert.Equal(highPriority.EndPoint, checker.CheckedRemoteCandidates[0].EndPoint);
        Assert.Equal(lowPriority.EndPoint, checker.CheckedRemoteCandidates[1].EndPoint);
        Assert.Equal(lowPriority.EndPoint, path.RemoteEndPoint);
    }

    [Fact]
    public async Task ConnectAsync_FailsWhenConfiguredConnectivityCheckerRejectsPair()
    {
        var checker = new FixedConnectivityChecker(succeeds: false);
        await using var leftFactory = new PublicHostFullIceDatagramPathFactory(CreateOptions(connectivityChecker: checker));
        await using var rightFactory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        IceCandidate rightCandidate = ReadLocalCandidate(rightFactory);
        _ = ReadLocalCandidate(leftFactory);

        await leftFactory.SetRemoteCredentialsAsync(rightFactory.LocalCredentials);
        await leftFactory.AddRemoteCandidateAsync(rightCandidate);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await leftFactory.ConnectAsync());
        IcePathEvent? checking = await leftFactory.ReadPathEventAsync();
        IcePathEvent? failed = await leftFactory.ReadPathEventAsync();

        Assert.Equal("ICE connectivity check failed for all candidate pairs.", exception.Message);
        Assert.Equal(1, checker.CheckCount);
        Assert.NotNull(checking);
        Assert.Equal(IcePathEventKind.CheckingStarted, checking.Value.Kind);
        Assert.NotNull(failed);
        Assert.Equal(IcePathEventKind.Failed, failed.Value.Kind);
        Assert.Equal(rightCandidate.EndPoint, failed.Value.RemoteEndPoint);
    }

    [Fact]
    public async Task RestartAsync_EmitsRestartEventsBeforeConnecting()
    {
        await using var leftFactory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        await using var rightFactory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        IceCandidate rightCandidate = ReadLocalCandidate(rightFactory);
        _ = ReadLocalCandidate(leftFactory);
        await leftFactory.SetRemoteCredentialsAsync(rightFactory.LocalCredentials);
        await leftFactory.AddRemoteCandidateAsync(rightCandidate);

        await using IDatagramPath path = await leftFactory.RestartAsync(new IceRestartRequest
        {
            RemoteCredentials = new IceCredentials
            {
                UsernameFragment = "restartUfrag",
                Password = "restartPassword"
            },
            RestartId = "restart-1"
        });

        IcePathEvent? restartStarted = await leftFactory.ReadPathEventAsync();
        IcePathEvent? restartCompleted = await leftFactory.ReadPathEventAsync();

        Assert.NotNull(path);
        Assert.NotNull(restartStarted);
        Assert.Equal(IcePathEventKind.RestartStarted, restartStarted.Value.Kind);
        Assert.Equal("restart-1", restartStarted.Value.RestartId);
        Assert.NotNull(restartCompleted);
        Assert.Equal(IcePathEventKind.RestartCompleted, restartCompleted.Value.Kind);
        Assert.Equal("restart-1", restartCompleted.Value.RestartId);
    }

    [Fact]
    public async Task RestartAsync_RejectsMissingRemoteCredentialsWithoutRotatingLocalCredentials()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        _ = ReadLocalCandidate(factory);
        IceCredentials originalCredentials = factory.LocalCredentials;

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(async () => await factory.RestartAsync(new IceRestartRequest
        {
            RemoteCredentials = new IceCredentials
            {
                UsernameFragment = "",
                Password = "restartPassword"
            },
            RestartId = "bad-restart"
        }));

        Assert.Equal("request", exception.ParamName);
        Assert.Equal(originalCredentials.UsernameFragment, factory.LocalCredentials.UsernameFragment);
        Assert.Equal(originalCredentials.Password, factory.LocalCredentials.Password);
        ValueTask<IcePathEvent?> pending = factory.ReadPathEventAsync();
        Assert.False(pending.IsCompleted);
        await factory.DisposeAsync();
        IcePathEvent? closed = await pending;
        Assert.NotNull(closed);
        Assert.Equal(IcePathEventKind.Closed, closed.Value.Kind);
    }

    [Fact]
    public async Task RestartAsync_RejectsBlankRestartIdWithoutRotatingLocalCredentials()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        _ = ReadLocalCandidate(factory);
        IceCredentials originalCredentials = factory.LocalCredentials;

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(async () => await factory.RestartAsync(new IceRestartRequest
        {
            RemoteCredentials = new IceCredentials
            {
                UsernameFragment = "restartUfrag",
                Password = "restartPassword"
            },
            RestartId = " "
        }));

        Assert.Equal("request", exception.ParamName);
        Assert.Equal(originalCredentials.UsernameFragment, factory.LocalCredentials.UsernameFragment);
        Assert.Equal(originalCredentials.Password, factory.LocalCredentials.Password);
        ValueTask<IcePathEvent?> pending = factory.ReadPathEventAsync();
        Assert.False(pending.IsCompleted);
        await factory.DisposeAsync();
        IcePathEvent? closed = await pending;
        Assert.NotNull(closed);
        Assert.Equal(IcePathEventKind.Closed, closed.Value.Kind);
    }

    [Fact]
    public async Task ReadPathEventAsync_CanceledReadDoesNotConsumeClosedEvent()
    {
        await using var factory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        using var cancellation = new CancellationTokenSource();

        ValueTask<IcePathEvent?> canceled = factory.ReadPathEventAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled);

        ValueTask<IcePathEvent?> pending = factory.ReadPathEventAsync();
        await factory.DisposeAsync();
        IcePathEvent? closed = await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(closed);
        Assert.Equal(IcePathEventKind.Closed, closed.Value.Kind);
    }

    [Fact]
    public async Task RestartAsync_AfterConnectRotatesLocalCredentialsAndReturnsNewPath()
    {
        await using var leftFactory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        await using var rightFactory = new PublicHostFullIceDatagramPathFactory(CreateOptions());
        IceCandidate leftCandidate = ReadLocalCandidate(leftFactory);
        IceCandidate rightCandidate = ReadLocalCandidate(rightFactory);
        await leftFactory.SetRemoteCredentialsAsync(rightFactory.LocalCredentials);
        await rightFactory.SetRemoteCredentialsAsync(leftFactory.LocalCredentials);
        await leftFactory.AddRemoteCandidateAsync(rightCandidate);
        await rightFactory.AddRemoteCandidateAsync(leftCandidate);
        IceCredentials originalLocalCredentials = leftFactory.LocalCredentials;

        await using IDatagramPath firstPath = await leftFactory.ConnectAsync();
        await firstPath.DisposeAsync();
        await using IDatagramPath restartedPath = await leftFactory.RestartAsync(new IceRestartRequest
        {
            RemoteCredentials = new IceCredentials
            {
                UsernameFragment = "restartUfrag",
                Password = "restartPassword"
            },
            RestartId = "restart-after-connect"
        });

        Assert.NotNull(restartedPath);
        Assert.Equal(rightCandidate.EndPoint, restartedPath.RemoteEndPoint);
        Assert.NotEqual(originalLocalCredentials.UsernameFragment, leftFactory.LocalCredentials.UsernameFragment);
        Assert.NotEqual(originalLocalCredentials.Password, leftFactory.LocalCredentials.Password);

        IcePathEvent? checkingStarted = null;
        IcePathEvent? ready = null;
        for (int i = 0; i < 8 && (checkingStarted is null || ready is null); i++)
        {
            IcePathEvent? pathEvent = await leftFactory.ReadPathEventAsync();
            Assert.NotNull(pathEvent);
            if (pathEvent.Value.Kind == IcePathEventKind.CheckingStarted &&
                pathEvent.Value.RestartId is null)
            {
                checkingStarted = pathEvent;
            }

            if (pathEvent.Value.Kind == IcePathEventKind.Ready &&
                pathEvent.Value.RemoteEndPoint?.Equals(rightCandidate.EndPoint) == true)
            {
                ready = pathEvent;
            }
        }

        Assert.NotNull(checkingStarted);
        Assert.NotNull(ready);
    }

    private static IceDatagramPathOptions CreateOptions(
        IceGatheringPolicy policy = IceGatheringPolicy.All,
        IIceConnectivityChecker? connectivityChecker = null)
    {
        return new IceDatagramPathOptions
        {
            Mode = IceMode.PublicHostFull,
            BindEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            GatheringPolicy = policy,
            ConnectivityChecker = connectivityChecker
        };
    }

    private static IceCandidate ReadLocalCandidate(PublicHostFullIceDatagramPathFactory factory)
    {
        Assert.True(factory.TryReadCandidateEvent(out IceCandidateEvent candidateEvent));
        Assert.Equal(IceCandidateEventKind.LocalCandidateDiscovered, candidateEvent.Kind);
        Assert.True(factory.TryReadCandidateEvent(out _));
        return candidateEvent.Candidate;
    }

    private static void DrainCandidateEvents(PublicHostFullIceDatagramPathFactory factory)
    {
        while (factory.TryReadCandidateEvent(out _))
        {
        }
    }

    private sealed class FixedMdnsResolver(IPAddress? address) : IIceMdnsResolver
    {
        public string? LastHostName { get; private set; }

        public ValueTask<IPAddress?> ResolveAsync(string hostName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastHostName = hostName;
            return new ValueTask<IPAddress?>(address);
        }
    }

    private sealed class RetryMdnsResolver(params IPAddress?[] addresses) : IIceMdnsResolver
    {
        public int AttemptCount { get; private set; }

        public ValueTask<IPAddress?> ResolveAsync(string hostName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Math.Min(AttemptCount, addresses.Length - 1);
            AttemptCount++;
            return new ValueTask<IPAddress?>(addresses[index]);
        }
    }

    private sealed class DelayedMdnsResolver(TimeSpan delay, IPAddress? address) : IIceMdnsResolver
    {
        public int AttemptCount { get; private set; }

        public async ValueTask<IPAddress?> ResolveAsync(string hostName, CancellationToken cancellationToken = default)
        {
            AttemptCount++;
            await Task.Delay(delay, cancellationToken);
            return address;
        }
    }

    private sealed class FixedConnectivityChecker(bool succeeds) : IIceConnectivityChecker
    {
        public int CheckCount { get; private set; }

        public IceCandidate? LastLocalCandidate { get; private set; }

        public IceCandidate? LastRemoteCandidate { get; private set; }

        public IceCredentials? LastRemoteCredentials { get; private set; }

        public ValueTask<bool> CheckAsync(
            Socket socket,
            IceCredentials localCredentials,
            IceCredentials remoteCredentials,
            IceCandidate localCandidate,
            IceCandidate remoteCandidate,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(socket);
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(timeout > TimeSpan.Zero);
            CheckCount++;
            LastLocalCandidate = localCandidate;
            LastRemoteCandidate = remoteCandidate;
            LastRemoteCredentials = remoteCredentials;
            return new ValueTask<bool>(succeeds);
        }
    }

    private sealed class SequenceConnectivityChecker(params bool[] results) : IIceConnectivityChecker
    {
        private int checkIndex;

        public List<IceCandidate> CheckedRemoteCandidates { get; } = [];

        public ValueTask<bool> CheckAsync(
            Socket socket,
            IceCredentials localCredentials,
            IceCredentials remoteCredentials,
            IceCandidate localCandidate,
            IceCandidate remoteCandidate,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(socket);
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(timeout > TimeSpan.Zero);
            CheckedRemoteCandidates.Add(remoteCandidate);
            bool result = checkIndex < results.Length && results[checkIndex];
            checkIndex++;
            return new ValueTask<bool>(result);
        }
    }
}
