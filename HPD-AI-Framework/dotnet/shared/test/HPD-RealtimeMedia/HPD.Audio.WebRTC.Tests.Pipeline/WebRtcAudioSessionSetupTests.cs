#nullable enable

using System.Net;
using System.Security.Cryptography.X509Certificates;
using HPD.Audio.WebRTC;
using HPD.Media.Sdp;
using HPD.Media.Transport;
using HPD.Media.WebRTC;

namespace HPD.Audio.WebRTC.Tests.Pipeline;

public sealed class WebRtcAudioSessionSetupTests
{
    private const string BrowserAudioOffer = """
v=0
o=- 4611733057959812032 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0
a=ice-ufrag:ufrag123
a=ice-pwd:password456
a=fingerprint:sha-256 00:01:02:03:04:05:06:07:08:09:0A:0B:0C:0D:0E:0F:10:11:12:13:14:15:16:17:18:19:1A:1B:1C:1D:1E:1F
m=audio 9 UDP/TLS/RTP/SAVPF 111 0
c=IN IP4 0.0.0.0
a=mid:0
a=sendrecv
a=rtcp-mux
a=rtpmap:111 opus/48000/2
a=rtpmap:0 PCMU/8000
a=fmtp:111 minptime=10;useinbandfec=1
a=setup:actpass
a=candidate:1 1 udp 2122260223 192.0.2.1 54400 typ host generation 0
a=end-of-candidates
""";

    [Fact]
    public async Task TryCreateAsync_ParsesSdpConfiguresIceConnectsAndCreatesSecurity()
    {
        var ice = new CapturingIceDatagramPathFactory();

        WebRtcAudioSession? session = await WebRtcAudioSessionSetup.TryCreateAsync(new WebRtcAudioSessionSetupOptions
        {
            RemoteDescription = new WebRtcSessionDescription
            {
                Type = WebRtcSessionDescriptionType.Offer,
                Sdp = BrowserAudioOffer
            },
            SdpParser = new SdpParser(),
            Ice = ice,
            SecureHandshake = new SuccessfulHandshake(),
            LocalCertificate = new LocalCertificate { Certificate = new X509Certificate2() },
            HandshakeOptions = new SecureHandshakeOptions(),
            PeerIdentityVerifier = new AcceptingPeerVerifier(),
            KeySchedule = new FixedKeySchedule(),
            PacketProtectorFactoryProvider = new NoOpPacketProtectorFactoryProvider(),
            RtpAudioFormatMapVersion = 17
        });

        Assert.NotNull(session);
        Assert.Equal("0", session.Media.Mid);
        Assert.Equal(17ul, session.FormatMap.Version);
        Assert.True(session.FormatMap.TryGetFormat(111, out _));
        Assert.Equal("ufrag123", ice.RemoteCredentials.UsernameFragment);
        Assert.Equal("password456", ice.RemoteCredentials.Password);
        Assert.Single(ice.RemoteCandidates);
        Assert.True(ice.EndRemoteCandidatesCalled);
        Assert.Equal(1, ice.ConnectCount);
        Assert.Same(ice.Path, session.Security.Path);
    }

    [Fact]
    public async Task TryCreateAsync_ReturnsNullForMissingRequestedMediaId()
    {
        WebRtcAudioSession? session = await WebRtcAudioSessionSetup.TryCreateAsync(new WebRtcAudioSessionSetupOptions
        {
            RemoteDescription = new WebRtcSessionDescription
            {
                Type = WebRtcSessionDescriptionType.Offer,
                Sdp = BrowserAudioOffer
            },
            SdpParser = new SdpParser(),
            Ice = new CapturingIceDatagramPathFactory(),
            SecureHandshake = new SuccessfulHandshake(),
            LocalCertificate = new LocalCertificate { Certificate = new X509Certificate2() },
            HandshakeOptions = new SecureHandshakeOptions(),
            PeerIdentityVerifier = new AcceptingPeerVerifier(),
            KeySchedule = new FixedKeySchedule(),
            PacketProtectorFactoryProvider = new NoOpPacketProtectorFactoryProvider(),
            MediaId = "missing"
        });

        Assert.Null(session);
    }

    [Fact]
    public async Task TryCreateAsync_DisposesSelectedPathWhenSecuritySetupFails()
    {
        var ice = new CapturingIceDatagramPathFactory();

        WebRtcAudioSession? session = await WebRtcAudioSessionSetup.TryCreateAsync(new WebRtcAudioSessionSetupOptions
        {
            RemoteDescription = new WebRtcSessionDescription
            {
                Type = WebRtcSessionDescriptionType.Offer,
                Sdp = BrowserAudioOffer
            },
            SdpParser = new SdpParser(),
            Ice = ice,
            SecureHandshake = new SuccessfulHandshake(),
            LocalCertificate = new LocalCertificate { Certificate = new X509Certificate2() },
            HandshakeOptions = new SecureHandshakeOptions(),
            PeerIdentityVerifier = new RejectingPeerVerifier(),
            KeySchedule = new FixedKeySchedule(),
            PacketProtectorFactoryProvider = new NoOpPacketProtectorFactoryProvider()
        });

        Assert.Null(session);
        Assert.True(ice.Path.IsDisposed);
    }

    private sealed class CapturingIceDatagramPathFactory : IIceDatagramPathFactory
    {
        public ReadyDatagramPath Path { get; } = new();

        public IceMode Mode => IceMode.PublicHostFull;

        public IceCredentials LocalCredentials { get; } = new()
        {
            UsernameFragment = "local",
            Password = "local-password"
        };

        public IceCredentials RemoteCredentials { get; private set; }

        public List<IceCandidate> RemoteCandidates { get; } = [];

        public bool EndRemoteCandidatesCalled { get; private set; }

        public int ConnectCount { get; private set; }

        public ValueTask<IceCandidateEvent?> ReadCandidateEventAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IceCandidateEvent?>((IceCandidateEvent?)null);
        }

        public ValueTask<IcePathEvent?> ReadPathEventAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IcePathEvent?>((IcePathEvent?)null);
        }

        public ValueTask SetRemoteCredentialsAsync(IceCredentials credentials, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoteCredentials = credentials;
            return ValueTask.CompletedTask;
        }

        public ValueTask AddRemoteCandidateAsync(IceCandidate candidate, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoteCandidates.Add(candidate);
            return ValueTask.CompletedTask;
        }

        public ValueTask EndRemoteCandidatesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EndRemoteCandidatesCalled = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<IDatagramPath> ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCount++;
            return new ValueTask<IDatagramPath>(Path);
        }

        public ValueTask<IDatagramPath> RestartAsync(IceRestartRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IDatagramPath>(Path);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ReadyDatagramPath : IDatagramPath
    {
        public bool IsDisposed { get; private set; }

        public IPEndPoint LocalEndPoint { get; } = new(IPAddress.Loopback, 30000);

        public IPEndPoint RemoteEndPoint { get; } = new(IPAddress.Loopback, 30001);

        public PathState State => IsDisposed ? PathState.Closed : PathState.Ready;

        public ValueTask<PathStateChange?> ReadStateChangeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<PathStateChange?>((PathStateChange?)null);
        }

        public ValueTask<DatagramReceiveResult> ReceiveAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<DatagramReceiveResult>(new DatagramReceiveResult { HasDatagram = false });
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SuccessfulHandshake : ISecureHandshake
    {
        public ValueTask<SecureHandshakeResult> HandshakeAsync(
            IDatagramPath path,
            LocalCertificate localCertificate,
            SecureHandshakeOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<SecureHandshakeResult>(new SecureHandshakeResult
            {
                PeerProof = new PeerProofMaterial { CertificateDer = new byte[] { 0x01 } },
                LocalRole = DtlsRole.Client,
                NegotiatedSrtpProfile = SrtpProtectionProfile.Aes128CmHmacSha1_80,
                KeyExporter = new FixedKeyExporter()
            });
        }
    }

    private sealed class FixedKeyExporter : IKeyExporter
    {
        public bool TryExport(string label, ReadOnlySpan<byte> context, Span<byte> destination)
        {
            destination.Fill(0x11);
            return true;
        }
    }

    private sealed class FixedKeySchedule : ISrtpKeySchedule
    {
        public SrtpProtectionMaterial Derive(SecureHandshakeResult handshake)
        {
            return new SrtpProtectionMaterial
            {
                Profile = handshake.NegotiatedSrtpProfile,
                OutboundMasterKey = new byte[16],
                OutboundMasterSalt = new byte[14],
                InboundMasterKey = new byte[16],
                InboundMasterSalt = new byte[14]
            };
        }
    }

    private sealed class NoOpPacketProtectorFactoryProvider : IWebRtcAudioPacketProtectorFactoryProvider
    {
        public IPacketProtectorFactory Create(SrtpProtectionMaterial material)
        {
            return new NoOpPacketProtectorFactory();
        }
    }

    private sealed class NoOpPacketProtectorFactory : IPacketProtectorFactory
    {
        public IPacketProtector Create(PacketProtectionPurpose purpose, PacketDirection direction, uint ssrc)
        {
            return new NoOpPacketProtector();
        }
    }

    private sealed class NoOpPacketProtector : IPacketProtector
    {
        public int MaximumExpansionBytes => 0;

        public PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength)
        {
            outputLength = inputLength;
            return PacketProtectionStatus.Success;
        }

        public PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength)
        {
            outputLength = inputLength;
            return PacketProtectionStatus.Success;
        }
    }

    private sealed class AcceptingPeerVerifier : IPeerIdentityVerifier
    {
        public PeerIdentityVerificationResult Verify(PeerProofMaterial proof, ExpectedPeerIdentity expected)
        {
            return new PeerIdentityVerificationResult { IsVerified = true };
        }
    }

    private sealed class RejectingPeerVerifier : IPeerIdentityVerifier
    {
        public PeerIdentityVerificationResult Verify(PeerProofMaterial proof, ExpectedPeerIdentity expected)
        {
            return new PeerIdentityVerificationResult
            {
                IsVerified = false,
                FailureReason = "Rejected."
            };
        }
    }
}
