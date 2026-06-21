#nullable enable

using System.Net;
using System.Security.Cryptography.X509Certificates;
using HPD.Audio.Codecs;
using HPD.Audio.Primitives;
using HPD.Audio.WebRTC;
using HPD.Media.Rtp;
using HPD.Media.Rtp.Audio;
using HPD.Media.Transport;

namespace HPD.Audio.WebRTC.Tests.Pipeline;

public sealed class WebRtcAudioSecurityContextTests
{
    private static readonly AudioFormat PcmFormat = new()
    {
        SampleRate = 8000,
        ChannelCount = 1,
        SampleFormat = AudioSampleFormat.Pcm16
    };

    private static readonly EncodedAudioFormat PcmuFormat = new()
    {
        Encoding = AudioEncoding.Pcmu,
        SampleRate = 8000,
        ChannelCount = 1,
        RtpClockRate = 8000
    };

    [Fact]
    public async Task CreateAsync_RunsHandshakeDerivesMaterialAndCreatesDirectionalPumps()
    {
        var provider = new CapturingPacketProtectorFactoryProvider();
        WebRtcAudioSecurityContext? context = await WebRtcAudioSecurityContext.CreateAsync(new WebRtcAudioSecurityOptions
        {
            Path = new ReadyDatagramPath(),
            SecureHandshake = new SuccessfulHandshake(),
            LocalCertificate = new LocalCertificate { Certificate = new X509Certificate2() },
            HandshakeOptions = new SecureHandshakeOptions(),
            KeySchedule = new FixedKeySchedule(),
            PacketProtectorFactoryProvider = provider,
            PeerIdentityVerifier = new AcceptingPeerVerifier(),
            ExpectedPeerIdentity = new ExpectedPeerIdentity
            {
                FingerprintAlgorithm = CertificateFingerprintAlgorithm.Sha256,
                Fingerprint = new byte[32]
            }
        });

        Assert.NotNull(context);
        Assert.Equal(SrtpProtectionProfile.Aes128CmHmacSha1_80, context.ProtectionMaterial.Profile);
        Assert.Equal(1, provider.CreateCount);

        var formatMap = new RtpAudioFormatMap(
            1,
            [
                new RtpAudioFormatBinding
                {
                    PayloadType = 0,
                    EncodedFormat = PcmuFormat,
                    DefaultPacketTime = TimeSpan.FromMilliseconds(20)
                }
            ]);
        var decodedSink = new CountingFrameSink();
        WebRtcAudioInboundPump inbound = context.CreateInboundAudioPump(
            remoteSsrc: 0x11111111,
            formatMap,
            new PassthroughDecoder(PcmFormat),
            decodedSink);
        var packetSink = new CapturingProtectedPacketSink();
        WebRtcAudioOutboundPump outbound = context.CreateOutboundAudioPump(
            localSsrc: 0x22222222,
            payloadType: 0,
            formatMap,
            new PassthroughEncoder(PcmFormat, PcmuFormat),
            packetSink,
            initialSequenceNumber: 9,
            initialTimestamp: 160);

        Span<byte> inboundPacket = stackalloc byte[64];
        int inboundLength = WriteRtpPacket(inboundPacket, payloadType: 0, sequenceNumber: 7, timestamp: 160, ssrc: 0x11111111);
        Assert.Equal(WebRtcAudioInboundStatus.Success, inbound.ProcessPacket(inboundPacket, inboundLength));

        byte[] scratch = new byte[64];
        byte[] pcm = new byte[320];
        Assert.Equal(WebRtcAudioOutboundStatus.Success, outbound.ProcessFrame(new AudioFrameView(pcm, PcmFormat, 160), scratch));

        Assert.Equal(1, decodedSink.FrameCount);
        Assert.Equal(1, packetSink.PacketCount);
        CapturingPacketProtectorFactory factory = Assert.IsType<CapturingPacketProtectorFactory>(provider.Factory);
        Assert.Contains(factory.Created, created => created.Direction == PacketDirection.Inbound && created.Ssrc == 0x11111111);
        Assert.Contains(factory.Created, created => created.Direction == PacketDirection.Outbound && created.Ssrc == 0x22222222);
    }

    [Fact]
    public async Task CreateAsync_ReturnsNullWhenExpectedPeerIdentityHasNoVerifier()
    {
        WebRtcAudioSecurityContext? context = await WebRtcAudioSecurityContext.CreateAsync(new WebRtcAudioSecurityOptions
        {
            Path = new ReadyDatagramPath(),
            SecureHandshake = new SuccessfulHandshake(),
            LocalCertificate = new LocalCertificate { Certificate = new X509Certificate2() },
            HandshakeOptions = new SecureHandshakeOptions(),
            KeySchedule = new FixedKeySchedule(),
            PacketProtectorFactoryProvider = new CapturingPacketProtectorFactoryProvider(),
            ExpectedPeerIdentity = new ExpectedPeerIdentity
            {
                FingerprintAlgorithm = CertificateFingerprintAlgorithm.Sha256,
                Fingerprint = new byte[32]
            }
        });

        Assert.Null(context);
    }

    [Fact]
    public async Task CreateAsync_ReturnsNullWhenPeerIdentityFails()
    {
        WebRtcAudioSecurityContext? context = await WebRtcAudioSecurityContext.CreateAsync(new WebRtcAudioSecurityOptions
        {
            Path = new ReadyDatagramPath(),
            SecureHandshake = new SuccessfulHandshake(),
            LocalCertificate = new LocalCertificate { Certificate = new X509Certificate2() },
            HandshakeOptions = new SecureHandshakeOptions(),
            KeySchedule = new FixedKeySchedule(),
            PacketProtectorFactoryProvider = new CapturingPacketProtectorFactoryProvider(),
            PeerIdentityVerifier = new RejectingPeerVerifier(),
            ExpectedPeerIdentity = new ExpectedPeerIdentity
            {
                FingerprintAlgorithm = CertificateFingerprintAlgorithm.Sha256,
                Fingerprint = new byte[32]
            }
        });

        Assert.Null(context);
    }

    private static int WriteRtpPacket(
        Span<byte> destination,
        byte payloadType,
        ushort sequenceNumber,
        uint timestamp,
        uint ssrc)
    {
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = payloadType,
                SequenceNumber = sequenceNumber,
                Timestamp = timestamp,
                Ssrc = ssrc
            },
            Payload = new byte[] { 0x7F },
            ArrivalTime = DateTimeOffset.UtcNow
        };
        Assert.Equal(RtpPacketStatus.Success, RtpPacketWriter.TryWrite(packet, destination, out int bytesWritten));
        return bytesWritten;
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
                PeerProof = new PeerProofMaterial { CertificateDer = new byte[] { 0x01, 0x02, 0x03 } },
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

    private sealed class CapturingPacketProtectorFactoryProvider : IWebRtcAudioPacketProtectorFactoryProvider
    {
        public int CreateCount { get; private set; }

        public IPacketProtectorFactory? Factory { get; private set; }

        public IPacketProtectorFactory Create(SrtpProtectionMaterial material)
        {
            CreateCount++;
            Factory = new CapturingPacketProtectorFactory();
            return Factory;
        }
    }

    private sealed class CapturingPacketProtectorFactory : IPacketProtectorFactory
    {
        public List<CreatedProtector> Created { get; } = [];

        public IPacketProtector Create(PacketProtectionPurpose purpose, PacketDirection direction, uint ssrc)
        {
            Created.Add(new CreatedProtector(purpose, direction, ssrc));
            return new NoOpPacketProtector();
        }
    }

    private sealed record CreatedProtector(PacketProtectionPurpose Purpose, PacketDirection Direction, uint Ssrc);

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
                FailureReason = "Rejected by test."
            };
        }
    }

    private sealed class PassthroughDecoder(AudioFormat outputFormat) : IRealtimeAudioDecoder
    {
        public AudioFormat OutputFormat { get; } = outputFormat;

        public AudioCodecStatus Decode(in AudioDecodeInputView input, IAudioFrameViewSink sink)
        {
            Span<byte> pcm = stackalloc byte[320];
            return sink.TryWrite(new AudioFrameView(pcm, OutputFormat, 160))
                ? AudioCodecStatus.Success
                : AudioCodecStatus.SinkBackpressure;
        }
    }

    private sealed class PassthroughEncoder(AudioFormat inputFormat, EncodedAudioFormat outputFormat) : IRealtimeAudioEncoder
    {
        public AudioFormat InputFormat { get; } = inputFormat;

        public EncodedAudioFormat OutputFormat { get; } = outputFormat;

        public AudioCodecStatus Encode(in AudioFrameView frame, IEncodedAudioFrameViewSink sink)
        {
            ReadOnlySpan<byte> payload = [0x7F];
            return sink.TryWrite(new EncodedAudioFrameView(OutputFormat, payload, frame.Duration))
                ? AudioCodecStatus.Success
                : AudioCodecStatus.SinkBackpressure;
        }
    }

    private sealed class CountingFrameSink : IAudioFrameViewSink
    {
        public int FrameCount { get; private set; }

        public bool TryWrite(in AudioFrameView frame)
        {
            FrameCount++;
            return true;
        }
    }

    private sealed class CapturingProtectedPacketSink : IWebRtcProtectedPacketSink
    {
        public int PacketCount { get; private set; }

        public bool TryWrite(ReadOnlySpan<byte> packet)
        {
            PacketCount++;
            return true;
        }
    }

    private sealed class ReadyDatagramPath : IDatagramPath
    {
        public IPEndPoint LocalEndPoint { get; } = new(IPAddress.Loopback, 10000);

        public IPEndPoint RemoteEndPoint { get; } = new(IPAddress.Loopback, 10001);

        public PathState State => PathState.Ready;

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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
