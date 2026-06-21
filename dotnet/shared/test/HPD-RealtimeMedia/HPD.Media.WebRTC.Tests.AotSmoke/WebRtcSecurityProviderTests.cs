#nullable enable

using System.Buffers;
using System.Security.Cryptography;
using HPD.Media.Sdp;
using HPD.Media.Transport;
using HPD.Media.WebRTC;

namespace HPD.Media.WebRTC.Tests.AotSmoke;

public sealed class WebRtcSecurityProviderTests
{
    [Fact]
    public void ProviderRegistry_StoresExplicitRegistrations()
    {
        var registry = new WebRtcProviderRegistry();
        var parser = new StubSdpParser();
        var writer = new StubSdpWriter();
        var handshake = new StubSecureHandshake();
        var verifier = new CertificateFingerprintPeerIdentityVerifier();
        var keySchedule = new WebRtcSrtpKeySchedule();
        var protectorProvider = new StubPacketProtectorFactoryProvider();

        registry.UseSdpParser(parser);
        registry.UseSdpWriter(writer);
        registry.UseSecureHandshake(handshake);
        registry.UsePeerIdentityVerifier(verifier);
        registry.UseSrtpKeySchedule(keySchedule);
        registry.UsePacketProtectorFactory(protectorProvider);

        Assert.Same(parser, registry.SdpParser);
        Assert.Same(writer, registry.SdpWriter);
        Assert.Same(handshake, registry.SecureHandshake);
        Assert.Same(verifier, registry.PeerIdentityVerifier);
        Assert.Same(keySchedule, registry.SrtpKeySchedule);
        Assert.Same(protectorProvider, registry.PacketProtectorFactoryProvider);
    }

    [Fact]
    public void CertificateFingerprintVerifier_AcceptsMatchingSha256Fingerprint()
    {
        byte[] certificateDer = [0x30, 0x82, 0x01, 0x0A, 0x02, 0x01, 0x01];
        byte[] fingerprint = SHA256.HashData(certificateDer);
        var verifier = new CertificateFingerprintPeerIdentityVerifier();

        PeerIdentityVerificationResult result = verifier.Verify(
            new PeerProofMaterial { CertificateDer = certificateDer },
            new ExpectedPeerIdentity
            {
                FingerprintAlgorithm = CertificateFingerprintAlgorithm.Sha256,
                Fingerprint = fingerprint
            });

        Assert.True(result.IsVerified);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void CertificateFingerprintVerifier_RejectsMismatchedFingerprint()
    {
        byte[] certificateDer = [0x30, 0x82, 0x01, 0x0A, 0x02, 0x01, 0x01];
        byte[] fingerprint = SHA256.HashData(certificateDer);
        fingerprint[0] ^= 0xFF;
        var verifier = new CertificateFingerprintPeerIdentityVerifier();

        PeerIdentityVerificationResult result = verifier.Verify(
            new PeerProofMaterial { CertificateDer = certificateDer },
            new ExpectedPeerIdentity
            {
                FingerprintAlgorithm = CertificateFingerprintAlgorithm.Sha256,
                Fingerprint = fingerprint
            });

        Assert.False(result.IsVerified);
        Assert.Contains("did not match", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void WebRtcSrtpKeySchedule_DerivesClientRoleMaterialFromExporter()
    {
        var exporter = new SequenceKeyExporter();
        var schedule = new WebRtcSrtpKeySchedule();

        SrtpProtectionMaterial material = schedule.Derive(new SecureHandshakeResult
        {
            PeerProof = new PeerProofMaterial { CertificateDer = new byte[] { 0x01 } },
            LocalRole = DtlsRole.Client,
            NegotiatedSrtpProfile = SrtpProtectionProfile.Aes128CmHmacSha1_80,
            KeyExporter = exporter
        });

        Assert.Equal("EXTRACTOR-dtls_srtp", exporter.LastLabel);
        Assert.Equal(0, exporter.LastContextLength);
        Assert.Equal(Bytes(0, 16), material.OutboundMasterKey.ToArray());
        Assert.Equal(Bytes(16, 16), material.InboundMasterKey.ToArray());
        Assert.Equal(Bytes(32, 14), material.OutboundMasterSalt.ToArray());
        Assert.Equal(Bytes(46, 14), material.InboundMasterSalt.ToArray());
    }

    [Fact]
    public void WebRtcSrtpKeySchedule_DerivesServerRoleMaterialFromExporter()
    {
        var exporter = new SequenceKeyExporter();
        var schedule = new WebRtcSrtpKeySchedule();

        SrtpProtectionMaterial material = schedule.Derive(new SecureHandshakeResult
        {
            PeerProof = new PeerProofMaterial { CertificateDer = new byte[] { 0x01 } },
            LocalRole = DtlsRole.Server,
            NegotiatedSrtpProfile = SrtpProtectionProfile.Aes128CmHmacSha1_32,
            KeyExporter = exporter
        });

        Assert.Equal(Bytes(16, 16), material.OutboundMasterKey.ToArray());
        Assert.Equal(Bytes(0, 16), material.InboundMasterKey.ToArray());
        Assert.Equal(Bytes(46, 14), material.OutboundMasterSalt.ToArray());
        Assert.Equal(Bytes(32, 14), material.InboundMasterSalt.ToArray());
    }

    private static byte[] Bytes(int start, int length)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(start + i);
        }

        return bytes;
    }

    private sealed class SequenceKeyExporter : IKeyExporter
    {
        public string? LastLabel { get; private set; }

        public int LastContextLength { get; private set; }

        public bool TryExport(string label, ReadOnlySpan<byte> context, Span<byte> destination)
        {
            LastLabel = label;
            LastContextLength = context.Length;
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = (byte)i;
            }

            return true;
        }
    }

    private sealed class StubSdpParser : ISdpParser
    {
        public SdpStatus TryParse(ReadOnlySpan<char> sdp, out SdpSessionDescription description)
        {
            description = default;
            return SdpStatus.InvalidSyntax;
        }
    }

    private sealed class StubSdpWriter : ISdpWriter
    {
        public SdpStatus TryWrite(in SdpSessionDescription description, IBufferWriter<char> destination)
        {
            return SdpStatus.Success;
        }
    }

    private sealed class StubSecureHandshake : ISecureHandshake
    {
        public ValueTask<SecureHandshakeResult> HandshakeAsync(
            IDatagramPath path,
            LocalCertificate localCertificate,
            SecureHandshakeOptions options,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubPacketProtectorFactoryProvider : IWebRtcPacketProtectorFactoryProvider
    {
        public IPacketProtectorFactory Create(SrtpProtectionMaterial material)
        {
            throw new NotSupportedException();
        }
    }
}
