#nullable enable

using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HPD.Media.Sdp;
using HPD.Media.Transport;

namespace HPD.Media.WebRTC.Tests.AotSmoke;

public sealed class WebRtcSessionDescriptionBuilderTests
{
    [Fact]
    public void TryCreateOffer_WritesBrowserFacingAudioSdp()
    {
        WebRtcAudioSessionDescriptionOptions options = CreateOptions();
        var writer = new SdpWriter();

        bool created = WebRtcSessionDescriptionBuilder.TryCreateOffer(
            options,
            writer,
            out WebRtcSessionDescription offer,
            out SdpStatus status);

        Assert.True(created);
        Assert.Equal(SdpStatus.Success, status);
        Assert.Equal(WebRtcSessionDescriptionType.Offer, offer.Type);

        var parser = new SdpParser();
        Assert.Equal(SdpStatus.Success, parser.TryParse(offer.Sdp, out SdpSessionDescription parsed));
        Assert.Equal(["0"], parsed.BundleMids.ToArray());
        Assert.Equal(options.LocalIceCredentials.UsernameFragment, parsed.IceUsernameFragment);
        Assert.Equal(options.LocalIceCredentials.Password, parsed.IcePassword);
        Assert.Equal("sha-256", Assert.Single(parsed.Fingerprints.ToArray()).Algorithm);

        SdpMediaSection audio = Assert.Single(parsed.MediaSections.ToArray());
        Assert.Equal(SdpMediaKind.Audio, audio.Kind);
        Assert.Equal("0", audio.Mid);
        Assert.Equal("actpass", audio.Setup);
        Assert.True(audio.RtcpMux);
        Assert.True(audio.RtcpReducedSize);
        Assert.Equal([111, 0], audio.PayloadTypes.ToArray());
        Assert.Equal("opus", audio.RtpMaps.Span[0].EncodingName);
        Assert.Equal("PCMU", audio.RtpMaps.Span[1].EncodingName);
        Assert.Equal("transport-cc", Assert.Single(audio.RtcpFeedback.ToArray()).Type);
        Assert.Equal("1 1 udp 2122260223 192.0.2.10 54400 typ host generation 0", Assert.Single(audio.IceCandidates.ToArray()));
        Assert.True(audio.EndOfCandidates);
    }

    [Fact]
    public void TryCreateAnswer_IntersectsPayloadsAndResolvesDtlsSetup()
    {
        const string RemoteOffer = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE audio
a=ice-ufrag:remoteUfrag
a=ice-pwd:remotePassword
a=fingerprint:sha-256 01:23:45:67:89:AB:CD:EF
m=audio 9 UDP/TLS/RTP/SAVPF 111 8
c=IN IP4 0.0.0.0
a=mid:audio
a=sendonly
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:111 opus/48000/2
a=rtpmap:8 PCMA/8000
a=rtcp-fb:111 transport-cc
a=extmap:1 urn:ietf:params:rtp-hdrext:ssrc-audio-level
a=setup:actpass
a=candidate:1 1 udp 2122260223 192.0.2.20 54400 typ host generation 0
a=end-of-candidates
""";
        var parser = new SdpParser();
        var writer = new SdpWriter();
        var remoteDescription = new WebRtcSessionDescription
        {
            Type = WebRtcSessionDescriptionType.Offer,
            Sdp = RemoteOffer
        };
        Assert.True(WebRtcSdpNegotiation.TryParse(
            remoteDescription,
            parser,
            out WebRtcParsedSessionDescription remoteOffer,
            out SdpStatus parseStatus));
        Assert.Equal(SdpStatus.Success, parseStatus);

        WebRtcAudioSessionDescriptionOptions options = CreateOptions(mid: "local", setup: WebRtcDtlsSetup.Active);
        bool created = WebRtcSessionDescriptionBuilder.TryCreateAnswer(
            remoteOffer,
            options,
            writer,
            out WebRtcSessionDescription answer,
            out SdpStatus status);

        Assert.True(created);
        Assert.Equal(SdpStatus.Success, status);
        Assert.Equal(WebRtcSessionDescriptionType.Answer, answer.Type);
        Assert.Equal(SdpStatus.Success, parser.TryParse(answer.Sdp, out SdpSessionDescription parsed));

        SdpMediaSection audio = Assert.Single(parsed.MediaSections.ToArray());
        Assert.Equal("audio", audio.Mid);
        Assert.Equal("active", audio.Setup);
        Assert.Equal(SdpMediaDirection.RecvOnly, audio.Direction);
        Assert.Equal([111], audio.PayloadTypes.ToArray());
        Assert.Equal("opus", Assert.Single(audio.RtpMaps.ToArray()).EncodingName);
        Assert.Single(audio.ExtMaps.ToArray());
        Assert.DoesNotContain(audio.RtpMaps.ToArray(), static map => map.PayloadType == 0);
    }

    private static WebRtcAudioSessionDescriptionOptions CreateOptions(
        string mid = "0",
        WebRtcDtlsSetup setup = WebRtcDtlsSetup.ActPass)
    {
        return new WebRtcAudioSessionDescriptionOptions
        {
            Mid = mid,
            LocalIceCredentials = new IceCredentials
            {
                UsernameFragment = "localUfrag",
                Password = "localPassword"
            },
            LocalCertificate = CreateCertificate(),
            Setup = setup,
            PayloadTypes = new byte[] { 111, 0 },
            RtpMaps = new[]
            {
                new SdpRtpMap
                {
                    PayloadType = 111,
                    EncodingName = "opus",
                    ClockRate = 48000,
                    ChannelCount = 2
                },
                new SdpRtpMap
                {
                    PayloadType = 0,
                    EncodingName = "PCMU",
                    ClockRate = 8000
                }
            },
            Fmtps = new[]
            {
                new SdpFmtp
                {
                    PayloadType = 111,
                    Parameters = "minptime=10;useinbandfec=1"
                }
            },
            RtcpFeedback = new[]
            {
                new SdpRtcpFeedback
                {
                    PayloadType = 111,
                    Type = "transport-cc"
                }
            },
            ExtMaps = new[]
            {
                new SdpExtMap
                {
                    Id = 1,
                    Uri = "urn:ietf:params:rtp-hdrext:ssrc-audio-level"
                }
            },
            LocalCandidates = new[]
            {
                new IceCandidate
                {
                    Foundation = "1",
                    ComponentId = 1,
                    Transport = "UDP",
                    Priority = 2122260223,
                    EndPoint = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 54400),
                    CandidateType = IceCandidateType.Host,
                    ExtensionAttributes = new[]
                    {
                        new IceCandidateAttribute
                        {
                            Name = "generation",
                            Value = "0"
                        }
                    }
                }
            },
            EndOfCandidates = true
        };
    }

    private static LocalCertificate CreateCertificate()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=HPD WebRTC Test",
            key,
            HashAlgorithmName.SHA256);
        X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        return new LocalCertificate { Certificate = certificate };
    }
}
