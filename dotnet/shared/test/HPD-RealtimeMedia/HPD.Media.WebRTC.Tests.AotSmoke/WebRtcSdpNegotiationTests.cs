#nullable enable

using System.Net;
using HPD.Media.Sdp;
using HPD.Media.Transport;
using HPD.Media.WebRTC;

namespace HPD.Media.WebRTC.Tests.AotSmoke;

public sealed class WebRtcSdpNegotiationTests
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
m=audio 9 UDP/TLS/RTP/SAVPF 111 0 8
c=IN IP4 0.0.0.0
a=mid:0
a=sendrecv
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:111 opus/48000/2
a=fmtp:111 minptime=10;useinbandfec=1
a=rtcp-fb:111 transport-cc
a=extmap:1 urn:ietf:params:rtp-hdrext:ssrc-audio-level
a=setup:actpass
a=candidate:1 1 udp 2122260223 192.0.2.1 54400 typ host generation 0
a=end-of-candidates
a=ssrc:1234 cname:test-cname
a=msid:stream track
""";

    [Fact]
    public void TryParse_DerivesTypedWebRtcNegotiationValuesFromSdp()
    {
        var description = new WebRtcSessionDescription
        {
            Type = WebRtcSessionDescriptionType.Offer,
            Sdp = BrowserAudioOffer
        };
        var parser = new SdpParser();

        bool parsed = WebRtcSdpNegotiation.TryParse(
            description,
            parser,
            out WebRtcParsedSessionDescription negotiation,
            out SdpStatus status);

        Assert.True(parsed);
        Assert.Equal(SdpStatus.Success, status);
        Assert.Equal(WebRtcSessionDescriptionType.Offer, negotiation.Description.Type);
        Assert.Equal(["0"], negotiation.Sdp.BundleMids.ToArray());

        WebRtcMediaDescription media = Assert.Single(negotiation.MediaDescriptions.ToArray());
        Assert.Equal("0", media.Mid);
        Assert.Equal(SdpMediaKind.Audio, media.SdpMedia.Kind);
        Assert.Equal(SdpMediaDirection.SendRecv, media.SdpMedia.Direction);
        Assert.Equal(WebRtcDtlsSetup.ActPass, media.Setup);
        Assert.NotNull(media.IceCredentials);
        Assert.Equal("ufrag123", media.IceCredentials.Value.UsernameFragment);
        Assert.Equal("password456", media.IceCredentials.Value.Password);
        Assert.NotNull(media.ExpectedPeerIdentity);
        Assert.Equal(CertificateFingerprintAlgorithm.Sha256, media.ExpectedPeerIdentity.Value.FingerprintAlgorithm);
        Assert.Equal(32, media.ExpectedPeerIdentity.Value.Fingerprint.Length);
        Assert.Equal([111, 0, 8], media.SdpMedia.PayloadTypes.ToArray());
        Assert.Equal("opus", Assert.Single(media.SdpMedia.RtpMaps.ToArray()).EncodingName);
        Assert.Equal("minptime=10;useinbandfec=1", Assert.Single(media.SdpMedia.Fmtps.ToArray()).Parameters);
        Assert.Equal("transport-cc", Assert.Single(media.SdpMedia.RtcpFeedback.ToArray()).Type);
        Assert.Equal(1, Assert.Single(media.SdpMedia.ExtMaps.ToArray()).Id);
        Assert.True(media.SdpMedia.RtcpMux);
        Assert.True(media.SdpMedia.RtcpReducedSize);

        IceCandidate candidate = Assert.Single(media.IceCandidates.ToArray());
        Assert.Equal(IceCandidateType.Host, candidate.CandidateType);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 54400), candidate.EndPoint);
        Assert.Equal("0", candidate.SdpMid);
    }

    [Fact]
    public void TryParse_UsesMediaLevelIceAndFingerprintWhenPresent()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
a=ice-ufrag:sessionUfrag
a=ice-pwd:sessionPassword
a=fingerprint:sha-256 01:02:03:04
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=mid:audio
a=ice-ufrag:mediaUfrag
a=ice-pwd:mediaPassword
a=fingerprint:sha-512 01:02:03:04
a=setup:passive
a=rtcp-mux
a=rtpmap:111 opus/48000/2
""";
        var description = new WebRtcSessionDescription
        {
            Type = WebRtcSessionDescriptionType.Answer,
            Sdp = Sdp
        };

        bool parsed = WebRtcSdpNegotiation.TryParse(description, new SdpParser(), out WebRtcParsedSessionDescription negotiation, out _);

        Assert.True(parsed);
        WebRtcMediaDescription media = Assert.Single(negotiation.MediaDescriptions.ToArray());
        Assert.NotNull(media.IceCredentials);
        Assert.Equal("mediaUfrag", media.IceCredentials.Value.UsernameFragment);
        Assert.Equal("mediaPassword", media.IceCredentials.Value.Password);
        Assert.NotNull(media.ExpectedPeerIdentity);
        Assert.Equal(CertificateFingerprintAlgorithm.Sha512, media.ExpectedPeerIdentity.Value.FingerprintAlgorithm);
        Assert.Equal(WebRtcDtlsSetup.Passive, media.Setup);
    }

    [Theory]
    [InlineData((WebRtcSessionDescriptionType)99, "v=0\r\n")]
    [InlineData(WebRtcSessionDescriptionType.Rollback, "v=0\r\n")]
    [InlineData(WebRtcSessionDescriptionType.Offer, "")]
    [InlineData(WebRtcSessionDescriptionType.Answer, "   ")]
    public void TryParse_RejectsInvalidBrowserFacingDescription(
        WebRtcSessionDescriptionType type,
        string sdp)
    {
        var description = new WebRtcSessionDescription
        {
            Type = type,
            Sdp = sdp
        };

        bool parsed = WebRtcSdpNegotiation.TryParse(description, new ThrowingSdpParser(), out _, out SdpStatus status);

        Assert.False(parsed);
        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Fact]
    public void TryParse_RejectsMalformedIceCandidateInMediaSection()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
a=ice-ufrag:ufrag
a=ice-pwd:password
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=mid:audio
a=rtpmap:111 opus/48000/2
a=candidate:not enough
""";
        var description = new WebRtcSessionDescription
        {
            Type = WebRtcSessionDescriptionType.Offer,
            Sdp = Sdp
        };

        bool parsed = WebRtcSdpNegotiation.TryParse(description, new SdpParser(), out _, out SdpStatus status);

        Assert.False(parsed);
        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Fact]
    public void TryParse_RejectsInvalidDtlsSetupValue()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
a=ice-ufrag:ufrag
a=ice-pwd:password
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=mid:audio
a=setup:banana
a=rtpmap:111 opus/48000/2
""";
        var description = new WebRtcSessionDescription
        {
            Type = WebRtcSessionDescriptionType.Offer,
            Sdp = Sdp
        };

        bool parsed = WebRtcSdpNegotiation.TryParse(description, new SdpParser(), out _, out SdpStatus status);

        Assert.False(parsed);
        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Theory]
    [InlineData("""
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
a=fingerprint:sha-256 00:01:02:03
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=mid:audio
a=setup:actpass
a=rtpmap:111 opus/48000/2
""")]
    [InlineData("""
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
a=ice-ufrag:ufrag
a=ice-pwd:password
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=mid:audio
a=setup:actpass
a=rtpmap:111 opus/48000/2
""")]
    [InlineData("""
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
a=ice-ufrag:ufrag
a=ice-pwd:password
a=fingerprint:sha-256 00:01:02:03
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=mid:audio
a=rtpmap:111 opus/48000/2
""")]
    public void TryParse_RejectsWebRtcMediaWithoutRequiredSecurityAndIceFacts(string sdp)
    {
        var description = new WebRtcSessionDescription
        {
            Type = WebRtcSessionDescriptionType.Offer,
            Sdp = sdp
        };

        bool parsed = WebRtcSdpNegotiation.TryParse(description, new SdpParser(), out _, out SdpStatus status);

        Assert.False(parsed);
        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    private sealed class ThrowingSdpParser : ISdpParser
    {
        public SdpStatus TryParse(ReadOnlySpan<char> sdp, out SdpSessionDescription description)
        {
            throw new InvalidOperationException("Invalid browser-facing descriptions should be rejected before SDP parsing.");
        }
    }
}
