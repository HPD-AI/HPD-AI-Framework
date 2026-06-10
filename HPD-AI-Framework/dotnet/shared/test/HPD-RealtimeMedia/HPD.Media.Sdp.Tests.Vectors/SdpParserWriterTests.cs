#nullable enable

using System.Buffers;
using HPD.Media.Sdp;

namespace HPD.Media.Sdp.Tests.Vectors;

public sealed class SdpParserWriterTests
{
    private const string BrowserAudioOffer = """
v=0
o=- 4611733057959812032 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0
a=ice-ufrag:ufrag123
a=ice-pwd:password456
a=fingerprint:sha-256 01:23:45:67:89:AB:CD:EF
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
    public void TryParse_ReadsWebRtcAudioProfile()
    {
        var parser = new SdpParser();

        SdpStatus status = parser.TryParse(BrowserAudioOffer, out SdpSessionDescription description);

        Assert.Equal(SdpStatus.Success, status);
        Assert.Equal("- 4611733057959812032 2 IN IP4 127.0.0.1", description.Origin);
        Assert.Equal("-", description.SessionName);
        Assert.Equal(["0"], description.BundleMids.ToArray());
        Assert.Equal("ufrag123", description.IceUsernameFragment);
        Assert.Equal("password456", description.IcePassword);
        Assert.Single(description.Fingerprints.ToArray());
        Assert.Equal([0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF], description.Fingerprints.Span[0].Fingerprint.ToArray());

        SdpMediaSection audio = Assert.Single(description.MediaSections.ToArray());
        Assert.Equal(SdpMediaKind.Audio, audio.Kind);
        Assert.Equal(9, audio.Port);
        Assert.Equal("0", audio.Mid);
        Assert.Equal("UDP/TLS/RTP/SAVPF", audio.Protocol);
        Assert.Equal(SdpMediaDirection.SendRecv, audio.Direction);
        Assert.Equal([111, 0, 8], audio.PayloadTypes.ToArray());
        Assert.Equal("opus", Assert.Single(audio.RtpMaps.ToArray()).EncodingName);
        Assert.Equal(48000, audio.RtpMaps.Span[0].ClockRate);
        Assert.Equal(2, audio.RtpMaps.Span[0].ChannelCount);
        Assert.Equal("minptime=10;useinbandfec=1", Assert.Single(audio.Fmtps.ToArray()).Parameters);
        Assert.Equal("transport-cc", Assert.Single(audio.RtcpFeedback.ToArray()).Type);
        Assert.Equal(1, Assert.Single(audio.ExtMaps.ToArray()).Id);
        Assert.True(audio.RtcpMux);
        Assert.True(audio.RtcpReducedSize);
        Assert.Equal("actpass", audio.Setup);
        Assert.Equal("1 1 udp 2122260223 192.0.2.1 54400 typ host generation 0", Assert.Single(audio.IceCandidates.ToArray()));
        Assert.True(audio.EndOfCandidates);
        Assert.Equal((uint)1234, Assert.Single(audio.SsrcAttributes.ToArray()).Ssrc);
        Assert.Equal("cname", audio.SsrcAttributes.Span[0].Attribute);
        Assert.Equal("test-cname", audio.SsrcAttributes.Span[0].Value);
        Assert.Equal("stream", Assert.Single(audio.Msids.ToArray()).StreamId);
        Assert.Equal("track", audio.Msids.Span[0].TrackId);
    }

    [Fact]
    public void TryParse_RejectsUnsupportedVersion()
    {
        var parser = new SdpParser();
        const string Sdp = """
v=1
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
""";

        SdpStatus status = parser.TryParse(Sdp, out _);

        Assert.Equal(SdpStatus.UnsupportedVersion, status);
    }

    [Fact]
    public void TryParse_RequiresVersionAndTimingLines()
    {
        var parser = new SdpParser();
        const string MissingVersion = """
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
""";
        const string MissingTiming = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
""";
        const string MalformedTiming = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0
""";

        SdpStatus missingVersionStatus = parser.TryParse(MissingVersion, out _);
        SdpStatus missingTimingStatus = parser.TryParse(MissingTiming, out _);
        SdpStatus malformedTimingStatus = parser.TryParse(MalformedTiming, out _);

        Assert.Equal(SdpStatus.MissingRequiredAttribute, missingVersionStatus);
        Assert.Equal(SdpStatus.MissingRequiredAttribute, missingTimingStatus);
        Assert.Equal(SdpStatus.InvalidSyntax, malformedTimingStatus);
    }

    [Fact]
    public void TryParse_RejectsMediaPortOutsideUdpRange()
    {
        var parser = new SdpParser();
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 70000 UDP/TLS/RTP/SAVPF 111
a=rtpmap:111 opus/48000/2
""";

        SdpStatus status = parser.TryParse(Sdp, out _);

        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Fact]
    public void TryParse_RejectsMediaPayloadTypeOutsideRtpRange()
    {
        var parser = new SdpParser();
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 9 UDP/TLS/RTP/SAVPF 128
""";

        SdpStatus status = parser.TryParse(Sdp, out _);

        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Fact]
    public void TryParse_RejectsEmptyFingerprintBytes()
    {
        var parser = new SdpParser();
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
a=fingerprint:sha-256 
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=rtpmap:111 opus/48000/2
""";

        SdpStatus status = parser.TryParse(Sdp, out _);

        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Fact]
    public void TryParse_RejectsFingerprintWithTrailingSeparator()
    {
        var parser = new SdpParser();
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
a=fingerprint:sha-256 01:
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=rtpmap:111 opus/48000/2
""";

        SdpStatus status = parser.TryParse(Sdp, out _);

        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Theory]
    [InlineData("a=rtpmap:128 opus/48000")]
    [InlineData("a=rtpmap:111  /48000")]
    [InlineData("a=rtpmap:111 opus/0")]
    [InlineData("a=rtpmap:111 opus/48000/0")]
    [InlineData("a=fmtp:128 minptime=10")]
    [InlineData("a=fmtp:111 ")]
    [InlineData("a=rtcp-fb:128 nack")]
    [InlineData("a=extmap:0 urn:ietf:params:rtp-hdrext:ssrc-audio-level")]
    [InlineData("a=extmap:1/sideways urn:ietf:params:rtp-hdrext:ssrc-audio-level")]
    [InlineData("a=rtpmap:0 PCMU/8000")]
    [InlineData("a=fmtp:0 minptime=10")]
    [InlineData("a=rtcp-fb:0 nack")]
    public void TryParse_RejectsInvalidMediaParameterAttributes(string attribute)
    {
        var parser = new SdpParser();
        string sdp = string.Join(
            "\r\n",
            "v=0",
            "o=- 1 1 IN IP4 127.0.0.1",
            "s=-",
            "t=0 0",
            "m=audio 9 UDP/TLS/RTP/SAVPF 111",
            attribute,
            string.Empty);

        SdpStatus status = parser.TryParse(sdp, out _);

        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Fact]
    public void TryParse_RejectsDuplicateMediaPayloadTypes()
    {
        var parser = new SdpParser();
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 9 UDP/TLS/RTP/SAVPF 111 111
a=rtpmap:111 opus/48000/2
""";

        SdpStatus status = parser.TryParse(Sdp, out _);

        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Theory]
    [InlineData("a=rtpmap:111 opus/48000/2\r\na=rtpmap:111 PCMU/8000")]
    [InlineData("a=rtpmap:111 opus/48000/2\r\na=fmtp:111 minptime=10\r\na=fmtp:111 useinbandfec=1")]
    [InlineData("a=rtpmap:111 opus/48000/2\r\na=extmap:1 urn:ietf:params:rtp-hdrext:ssrc-audio-level\r\na=extmap:1 urn:ietf:params:rtp-hdrext:sdes:mid")]
    public void TryParse_RejectsDuplicateMediaBindingKeys(string attributes)
    {
        var parser = new SdpParser();
        string sdp = string.Join(
            "\r\n",
            "v=0",
            "o=- 1 1 IN IP4 127.0.0.1",
            "s=-",
            "t=0 0",
            "m=audio 9 UDP/TLS/RTP/SAVPF 111",
            attributes,
            string.Empty);

        SdpStatus status = parser.TryParse(sdp, out _);

        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Theory]
    [InlineData("a=ice-ufrag:")]
    [InlineData("a=ice-pwd:")]
    [InlineData("a=setup:sideways")]
    [InlineData("a=mid:")]
    [InlineData("a=candidate: ")]
    [InlineData("a=:value")]
    [InlineData("a=ssrc:1234 :value")]
    [InlineData("a=ssrc:1234 cname:")]
    public void TryParse_RejectsInvalidIceAndSetupAttributes(string attribute)
    {
        var parser = new SdpParser();
        string sdp = string.Join(
            "\r\n",
            "v=0",
            "o=- 1 1 IN IP4 127.0.0.1",
            "s=-",
            "t=0 0",
            "m=audio 9 UDP/TLS/RTP/SAVPF 111",
            attribute,
            "a=rtpmap:111 opus/48000/2",
            string.Empty);

        SdpStatus status = parser.TryParse(sdp, out _);

        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Theory]
    [InlineData("a=bad name:value")]
    [InlineData("a=ssrc:1234 c name:value")]
    public void TryParse_RejectsWhitespaceInsideAttributeNames(string attribute)
    {
        var parser = new SdpParser();
        string sdp = string.Join(
            "\r\n",
            "v=0",
            "o=- 1 1 IN IP4 127.0.0.1",
            "s=-",
            "t=0 0",
            "m=audio 9 UDP/TLS/RTP/SAVPF 111",
            attribute,
            "a=rtpmap:111 opus/48000/2",
            string.Empty);

        SdpStatus status = parser.TryParse(sdp, out _);

        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Fact]
    public void TryParse_RejectsEmptyBundleGroup()
    {
        var parser = new SdpParser();
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=mid:audio
a=rtpmap:111 opus/48000/2
""";

        SdpStatus status = parser.TryParse(Sdp, out _);

        Assert.Equal(SdpStatus.InvalidSyntax, status);
    }

    [Fact]
    public void TryWrite_WritesDescriptionThatParsesBack()
    {
        var parser = new SdpParser();
        var writer = new SdpWriter();
        Assert.Equal(SdpStatus.Success, parser.TryParse(BrowserAudioOffer, out SdpSessionDescription description));
        var buffer = new ArrayBufferWriter<char>();

        SdpStatus writeStatus = writer.TryWrite(description, buffer);
        string written = new(buffer.WrittenSpan);
        SdpStatus parseStatus = parser.TryParse(written, out SdpSessionDescription reparsed);

        Assert.Equal(SdpStatus.Success, writeStatus);
        Assert.Equal(SdpStatus.Success, parseStatus);
        Assert.Contains("\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111 0 8\r\n", written);
        Assert.Contains("\r\na=rtcp-mux\r\n", written);
        Assert.Contains("\r\na=rtcp-rsize\r\n", written);
        Assert.Contains("\r\na=candidate:1 1 udp 2122260223 192.0.2.1 54400 typ host generation 0\r\n", written);
        Assert.Contains("\r\na=end-of-candidates\r\n", written);
        Assert.Contains("\r\na=ssrc:1234 cname:test-cname\r\n", written);
        Assert.Contains("\r\na=msid:stream track\r\n", written);
        Assert.Equal(description.Origin, reparsed.Origin);
        Assert.Equal(description.IceUsernameFragment, reparsed.IceUsernameFragment);
        Assert.Equal(description.MediaSections.Span[0].RtpMaps.Span[0].EncodingName, reparsed.MediaSections.Span[0].RtpMaps.Span[0].EncodingName);
        Assert.True(reparsed.MediaSections.Span[0].RtcpMux);
        Assert.True(reparsed.MediaSections.Span[0].EndOfCandidates);
    }

    [Fact]
    public void TryWrite_RejectsInvalidMediaPortAndEmptyFingerprint()
    {
        var writer = new SdpWriter();
        var validDescription = new SdpSessionDescription
        {
            Origin = "- 1 1 IN IP4 127.0.0.1",
            SessionName = "-",
            MediaSections = new[]
            {
                new SdpMediaSection
                {
                    Kind = SdpMediaKind.Audio,
                    Port = 9,
                    Protocol = "UDP/TLS/RTP/SAVPF",
                    PayloadTypes = new byte[] { 111 },
                    RtpMaps = new[]
                    {
                        new SdpRtpMap
                        {
                            PayloadType = 111,
                            EncodingName = "opus",
                            ClockRate = 48000,
                            ChannelCount = 2
                        }
                    }
                }
            }
        };

        SdpStatus badPort = writer.TryWrite(
            validDescription with
            {
                MediaSections = new[]
                {
                    validDescription.MediaSections.Span[0] with { Port = 70000 }
                }
            },
            new ArrayBufferWriter<char>());
        SdpStatus emptyFingerprint = writer.TryWrite(
            validDescription with
            {
                Fingerprints = new[]
                {
                    new SdpFingerprint { Algorithm = "sha-256", Fingerprint = ReadOnlyMemory<byte>.Empty }
                }
            },
            new ArrayBufferWriter<char>());

        Assert.Equal(SdpStatus.InvalidSyntax, badPort);
        Assert.Equal(SdpStatus.InvalidSyntax, emptyFingerprint);
    }

    [Fact]
    public void TryWrite_RejectsInvalidMediaParameterAttributes()
    {
        var writer = new SdpWriter();
        var validDescription = new SdpSessionDescription
        {
            Origin = "- 1 1 IN IP4 127.0.0.1",
            SessionName = "-",
            MediaSections = new[]
            {
                new SdpMediaSection
                {
                    Kind = SdpMediaKind.Audio,
                    Port = 9,
                    Protocol = "UDP/TLS/RTP/SAVPF",
                    PayloadTypes = new byte[] { 111 },
                    RtpMaps = new[]
                    {
                        new SdpRtpMap
                        {
                            PayloadType = 111,
                            EncodingName = "opus",
                            ClockRate = 48000,
                            ChannelCount = 2
                        }
                    }
                }
            }
        };
        SdpMediaSection media = validDescription.MediaSections.Span[0];

        SdpStatus badMediaPayloadType = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { PayloadTypes = new byte[] { 128 } } } },
            new ArrayBufferWriter<char>());
        SdpStatus zeroClockRate = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { RtpMaps = new[] { media.RtpMaps.Span[0] with { ClockRate = 0 } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus badRtpMapPayloadType = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { RtpMaps = new[] { media.RtpMaps.Span[0] with { PayloadType = 128 } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus zeroChannels = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { RtpMaps = new[] { media.RtpMaps.Span[0] with { ChannelCount = 0 } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus badFmtpPayloadType = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Fmtps = new[] { new SdpFmtp { PayloadType = 128, Parameters = "minptime=10" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus emptyFmtp = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Fmtps = new[] { new SdpFmtp { PayloadType = 111, Parameters = " " } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus badFeedbackPayloadType = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { RtcpFeedback = new[] { new SdpRtcpFeedback { PayloadType = 128, Type = "nack" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus emptyFeedbackType = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { RtcpFeedback = new[] { new SdpRtcpFeedback { PayloadType = 111, Type = "" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus badExtMap = writer.TryWrite(
            validDescription with
            {
                MediaSections = new[]
                {
                    media with
                    {
                        ExtMaps = new[]
                        {
                            new SdpExtMap
                            {
                                Id = 1,
                                Direction = "sideways",
                                Uri = "urn:ietf:params:rtp-hdrext:ssrc-audio-level"
                            }
                        }
                    }
                }
            },
            new ArrayBufferWriter<char>());
        SdpStatus unlistedRtpMap = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { RtpMaps = new[] { media.RtpMaps.Span[0] with { PayloadType = 0 } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus unlistedFmtp = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Fmtps = new[] { new SdpFmtp { PayloadType = 0, Parameters = "minptime=10" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus unlistedFeedback = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { RtcpFeedback = new[] { new SdpRtcpFeedback { PayloadType = 0, Type = "nack" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus duplicateMediaPayloadType = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { PayloadTypes = new byte[] { 111, 111 } } } },
            new ArrayBufferWriter<char>());
        SdpStatus duplicateRtpMapPayloadType = writer.TryWrite(
            validDescription with
            {
                MediaSections = new[]
                {
                    media with
                    {
                        RtpMaps = new[]
                        {
                            media.RtpMaps.Span[0],
                            media.RtpMaps.Span[0] with { EncodingName = "PCMU", ClockRate = 8000, ChannelCount = null }
                        }
                    }
                }
            },
            new ArrayBufferWriter<char>());
        SdpStatus duplicateFmtpPayloadType = writer.TryWrite(
            validDescription with
            {
                MediaSections = new[]
                {
                    media with
                    {
                        Fmtps = new[]
                        {
                            new SdpFmtp { PayloadType = 111, Parameters = "minptime=10" },
                            new SdpFmtp { PayloadType = 111, Parameters = "useinbandfec=1" }
                        }
                    }
                }
            },
            new ArrayBufferWriter<char>());
        SdpStatus duplicateExtMapId = writer.TryWrite(
            validDescription with
            {
                MediaSections = new[]
                {
                    media with
                    {
                        ExtMaps = new[]
                        {
                            new SdpExtMap { Id = 1, Uri = "urn:ietf:params:rtp-hdrext:ssrc-audio-level" },
                            new SdpExtMap { Id = 1, Uri = "urn:ietf:params:rtp-hdrext:sdes:mid" }
                        }
                    }
                }
            },
            new ArrayBufferWriter<char>());

        Assert.Equal(SdpStatus.InvalidSyntax, badMediaPayloadType);
        Assert.Equal(SdpStatus.InvalidSyntax, zeroClockRate);
        Assert.Equal(SdpStatus.InvalidSyntax, badRtpMapPayloadType);
        Assert.Equal(SdpStatus.InvalidSyntax, zeroChannels);
        Assert.Equal(SdpStatus.InvalidSyntax, badFmtpPayloadType);
        Assert.Equal(SdpStatus.InvalidSyntax, emptyFmtp);
        Assert.Equal(SdpStatus.InvalidSyntax, badFeedbackPayloadType);
        Assert.Equal(SdpStatus.InvalidSyntax, emptyFeedbackType);
        Assert.Equal(SdpStatus.InvalidSyntax, badExtMap);
        Assert.Equal(SdpStatus.InvalidSyntax, unlistedRtpMap);
        Assert.Equal(SdpStatus.InvalidSyntax, unlistedFmtp);
        Assert.Equal(SdpStatus.InvalidSyntax, unlistedFeedback);
        Assert.Equal(SdpStatus.InvalidSyntax, duplicateMediaPayloadType);
        Assert.Equal(SdpStatus.InvalidSyntax, duplicateRtpMapPayloadType);
        Assert.Equal(SdpStatus.InvalidSyntax, duplicateFmtpPayloadType);
        Assert.Equal(SdpStatus.InvalidSyntax, duplicateExtMapId);
    }

    [Fact]
    public void TryWrite_RejectsInvalidIceAndSetupValues()
    {
        var writer = new SdpWriter();
        var validDescription = new SdpSessionDescription
        {
            Origin = "- 1 1 IN IP4 127.0.0.1",
            SessionName = "-",
            MediaSections = new[]
            {
                new SdpMediaSection
                {
                    Kind = SdpMediaKind.Audio,
                    Port = 9,
                    Protocol = "UDP/TLS/RTP/SAVPF",
                    PayloadTypes = new byte[] { 111 },
                    RtpMaps = new[]
                    {
                        new SdpRtpMap
                        {
                            PayloadType = 111,
                            EncodingName = "opus",
                            ClockRate = 48000,
                            ChannelCount = 2
                        }
                    }
                }
            }
        };
        SdpMediaSection media = validDescription.MediaSections.Span[0];

        SdpStatus blankSessionUfrag = writer.TryWrite(
            validDescription with { IceUsernameFragment = " " },
            new ArrayBufferWriter<char>());
        SdpStatus blankMediaPwd = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { IcePassword = "" } } },
            new ArrayBufferWriter<char>());
        SdpStatus invalidSetup = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Setup = "sideways" } } },
            new ArrayBufferWriter<char>());
        SdpStatus blankBundleMid = writer.TryWrite(
            validDescription with { BundleMids = new[] { " " } },
            new ArrayBufferWriter<char>());
        SdpStatus blankMediaMid = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Mid = "" } } },
            new ArrayBufferWriter<char>());
        SdpStatus blankCandidate = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { IceCandidates = new[] { " " } } } },
            new ArrayBufferWriter<char>());
        SdpStatus blankSsrcAttribute = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { SsrcAttributes = new[] { new SdpSsrcAttribute { Ssrc = 1234, Attribute = "" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus blankSsrcValue = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { SsrcAttributes = new[] { new SdpSsrcAttribute { Ssrc = 1234, Attribute = "cname", Value = " " } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus blankMsidStream = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Msids = new[] { new SdpMsid { StreamId = "" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus blankMsidTrack = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Msids = new[] { new SdpMsid { StreamId = "stream", TrackId = " " } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus blankSessionAttributeName = writer.TryWrite(
            validDescription with { Attributes = new[] { new SdpAttribute { Name = "", Value = "value" } } },
            new ArrayBufferWriter<char>());
        SdpStatus spacedSessionAttributeName = writer.TryWrite(
            validDescription with { Attributes = new[] { new SdpAttribute { Name = "bad name", Value = "value" } } },
            new ArrayBufferWriter<char>());
        SdpStatus mediaAttributeLineBreak = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Attributes = new[] { new SdpAttribute { Name = "ptime", Value = "20\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus spacedSsrcAttributeName = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { SsrcAttributes = new[] { new SdpSsrcAttribute { Ssrc = 1234, Attribute = "c name", Value = "test" } } } } },
            new ArrayBufferWriter<char>());

        Assert.Equal(SdpStatus.InvalidSyntax, blankSessionUfrag);
        Assert.Equal(SdpStatus.InvalidSyntax, blankMediaPwd);
        Assert.Equal(SdpStatus.InvalidSyntax, invalidSetup);
        Assert.Equal(SdpStatus.InvalidSyntax, blankBundleMid);
        Assert.Equal(SdpStatus.InvalidSyntax, blankMediaMid);
        Assert.Equal(SdpStatus.InvalidSyntax, blankCandidate);
        Assert.Equal(SdpStatus.InvalidSyntax, blankSsrcAttribute);
        Assert.Equal(SdpStatus.InvalidSyntax, blankSsrcValue);
        Assert.Equal(SdpStatus.InvalidSyntax, blankMsidStream);
        Assert.Equal(SdpStatus.InvalidSyntax, blankMsidTrack);
        Assert.Equal(SdpStatus.InvalidSyntax, blankSessionAttributeName);
        Assert.Equal(SdpStatus.InvalidSyntax, spacedSessionAttributeName);
        Assert.Equal(SdpStatus.InvalidSyntax, mediaAttributeLineBreak);
        Assert.Equal(SdpStatus.InvalidSyntax, spacedSsrcAttributeName);
    }

    [Fact]
    public void TryWrite_RejectsLineBreaksInModeledTextFields()
    {
        var writer = new SdpWriter();
        var validDescription = new SdpSessionDescription
        {
            Origin = "- 1 1 IN IP4 127.0.0.1",
            SessionName = "-",
            BundleMids = new[] { "audio" },
            IceUsernameFragment = "sessionUfrag",
            IcePassword = "sessionPassword",
            Fingerprints = new[]
            {
                new SdpFingerprint { Algorithm = "sha-256", Fingerprint = new byte[] { 0xAA } }
            },
            MediaSections = new[]
            {
                new SdpMediaSection
                {
                    Kind = SdpMediaKind.Audio,
                    Port = 9,
                    Protocol = "UDP/TLS/RTP/SAVPF",
                    Mid = "audio",
                    PayloadTypes = new byte[] { 111 },
                    RtpMaps = new[]
                    {
                        new SdpRtpMap
                        {
                            PayloadType = 111,
                            EncodingName = "opus",
                            ClockRate = 48000,
                            ChannelCount = 2
                        }
                    },
                    Fmtps = new[] { new SdpFmtp { PayloadType = 111, Parameters = "minptime=10" } },
                    RtcpFeedback = new[] { new SdpRtcpFeedback { PayloadType = 111, Type = "nack", Parameters = "pli" } },
                    ExtMaps = new[]
                    {
                        new SdpExtMap
                        {
                            Id = 1,
                            Uri = "urn:ietf:params:rtp-hdrext:ssrc-audio-level",
                            Attributes = "vad=on"
                        }
                    },
                    IceUsernameFragment = "mediaUfrag",
                    IcePassword = "mediaPassword",
                    Setup = "actpass",
                    IceCandidates = new[] { "1 1 udp 1 192.0.2.1 5000 typ host" },
                    SsrcAttributes = new[] { new SdpSsrcAttribute { Ssrc = 1234, Attribute = "cname", Value = "test" } },
                    Msids = new[] { new SdpMsid { StreamId = "stream", TrackId = "track" } }
                }
            }
        };
        SdpMediaSection media = validDescription.MediaSections.Span[0];

        SdpStatus origin = writer.TryWrite(validDescription with { Origin = "-\r\na=sendonly" }, new ArrayBufferWriter<char>());
        SdpStatus sessionName = writer.TryWrite(validDescription with { SessionName = "-\na=sendonly" }, new ArrayBufferWriter<char>());
        SdpStatus bundleMid = writer.TryWrite(validDescription with { BundleMids = new[] { "audio\r\na=sendonly" } }, new ArrayBufferWriter<char>());
        SdpStatus sessionIce = writer.TryWrite(validDescription with { IceUsernameFragment = "ufrag\r\na=sendonly" }, new ArrayBufferWriter<char>());
        SdpStatus fingerprintAlgorithm = writer.TryWrite(
            validDescription with { Fingerprints = new[] { validDescription.Fingerprints.Span[0] with { Algorithm = "sha-256\r\na=sendonly" } } },
            new ArrayBufferWriter<char>());
        SdpStatus protocol = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Protocol = "UDP/TLS/RTP/SAVPF\r\na=sendonly" } } },
            new ArrayBufferWriter<char>());
        SdpStatus mid = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Mid = "audio\r\na=sendonly" } } },
            new ArrayBufferWriter<char>());
        SdpStatus rtpMap = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { RtpMaps = new[] { media.RtpMaps.Span[0] with { EncodingName = "opus\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus fmtp = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Fmtps = new[] { media.Fmtps.Span[0] with { Parameters = "minptime=10\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus feedback = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { RtcpFeedback = new[] { media.RtcpFeedback.Span[0] with { Type = "nack\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus feedbackParameters = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { RtcpFeedback = new[] { media.RtcpFeedback.Span[0] with { Parameters = "pli\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus extMap = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { ExtMaps = new[] { media.ExtMaps.Span[0] with { Uri = "urn:test\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus extMapAttributes = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { ExtMaps = new[] { media.ExtMaps.Span[0] with { Attributes = "vad=on\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus mediaIce = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { IcePassword = "pwd\r\na=sendonly" } } },
            new ArrayBufferWriter<char>());
        SdpStatus candidate = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { IceCandidates = new[] { "1 1 udp 1 192.0.2.1 5000 typ host\r\na=sendonly" } } } },
            new ArrayBufferWriter<char>());
        SdpStatus ssrcAttribute = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { SsrcAttributes = new[] { media.SsrcAttributes.Span[0] with { Attribute = "cname\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus ssrcValue = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { SsrcAttributes = new[] { media.SsrcAttributes.Span[0] with { Value = "test\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus msid = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Msids = new[] { media.Msids.Span[0] with { StreamId = "stream\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());
        SdpStatus msidTrack = writer.TryWrite(
            validDescription with { MediaSections = new[] { media with { Msids = new[] { media.Msids.Span[0] with { TrackId = "track\r\na=sendonly" } } } } },
            new ArrayBufferWriter<char>());

        Assert.All(
            [
                origin,
                sessionName,
                bundleMid,
                sessionIce,
                fingerprintAlgorithm,
                protocol,
                mid,
                rtpMap,
                fmtp,
                feedback,
                feedbackParameters,
                extMap,
                extMapAttributes,
                mediaIce,
                candidate,
                ssrcAttribute,
                ssrcValue,
                msid,
                msidTrack
            ],
            status => Assert.Equal(SdpStatus.InvalidSyntax, status));
    }

    [Fact]
    public void TryWrite_PreservesGenericAudioTimingAttributes()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=mid:audio
a=sendrecv
a=rtpmap:111 opus/48000/2
a=ptime:20
a=maxptime:60
""";
        var parser = new SdpParser();
        var writer = new SdpWriter();
        Assert.Equal(SdpStatus.Success, parser.TryParse(Sdp, out SdpSessionDescription description));
        var buffer = new ArrayBufferWriter<char>();

        SdpStatus writeStatus = writer.TryWrite(description, buffer);
        string written = new(buffer.WrittenSpan);
        SdpStatus parseStatus = parser.TryParse(written, out SdpSessionDescription reparsed);

        Assert.Equal(SdpStatus.Success, writeStatus);
        Assert.Equal(SdpStatus.Success, parseStatus);
        Assert.Contains("\r\na=ptime:20\r\n", written);
        Assert.Contains("\r\na=maxptime:60\r\n", written);
        SdpMediaSection reparsedAudio = Assert.Single(reparsed.MediaSections.ToArray());
        Assert.Contains(reparsedAudio.Attributes.ToArray(), attribute => attribute.Name == "ptime" && attribute.Value == "20");
        Assert.Contains(reparsedAudio.Attributes.ToArray(), attribute => attribute.Name == "maxptime" && attribute.Value == "60");
    }

    [Fact]
    public void TryWrite_PreservesMediaPort()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 49170 RTP/AVP 0
c=IN IP4 203.0.113.10
a=mid:audio
a=sendrecv
a=rtpmap:0 PCMU/8000
""";
        var parser = new SdpParser();
        var writer = new SdpWriter();
        Assert.Equal(SdpStatus.Success, parser.TryParse(Sdp, out SdpSessionDescription description));
        var buffer = new ArrayBufferWriter<char>();

        SdpStatus writeStatus = writer.TryWrite(description, buffer);
        string written = new(buffer.WrittenSpan);
        SdpStatus parseStatus = parser.TryParse(written, out SdpSessionDescription reparsed);

        Assert.Equal(SdpStatus.Success, writeStatus);
        Assert.Equal(SdpStatus.Success, parseStatus);
        Assert.Contains("\r\nm=audio 49170 RTP/AVP 0\r\n", written);
        Assert.Contains("\r\nc=IN IP4 203.0.113.10\r\n", written);
        Assert.Equal(49170, Assert.Single(reparsed.MediaSections.ToArray()).Port);
    }

    [Fact]
    public void TryWrite_PreservesExtMapDirectionAndAttributes()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=mid:audio
a=sendrecv
a=rtpmap:111 opus/48000/2
a=extmap:3/recvonly urn:ietf:params:rtp-hdrext:ssrc-audio-level vad=on
""";
        var parser = new SdpParser();
        var writer = new SdpWriter();
        Assert.Equal(SdpStatus.Success, parser.TryParse(Sdp, out SdpSessionDescription description));
        var buffer = new ArrayBufferWriter<char>();

        SdpExtMap extMap = Assert.Single(description.MediaSections.Span[0].ExtMaps.ToArray());
        SdpStatus writeStatus = writer.TryWrite(description, buffer);
        string written = new(buffer.WrittenSpan);
        SdpStatus parseStatus = parser.TryParse(written, out SdpSessionDescription reparsed);
        SdpExtMap reparsedExtMap = Assert.Single(reparsed.MediaSections.Span[0].ExtMaps.ToArray());

        Assert.Equal(3, extMap.Id);
        Assert.Equal("recvonly", extMap.Direction);
        Assert.Equal("urn:ietf:params:rtp-hdrext:ssrc-audio-level", extMap.Uri);
        Assert.Equal("vad=on", extMap.Attributes);
        Assert.Equal(SdpStatus.Success, writeStatus);
        Assert.Equal(SdpStatus.Success, parseStatus);
        Assert.Contains("\r\na=extmap:3/recvonly urn:ietf:params:rtp-hdrext:ssrc-audio-level vad=on\r\n", written);
        Assert.Equal(extMap.Direction, reparsedExtMap.Direction);
        Assert.Equal(extMap.Attributes, reparsedExtMap.Attributes);
    }

    [Fact]
    public void TryParse_ReadsMediaLevelSecurityAndIceAttributes()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 9 UDP/TLS/RTP/SAVPF 111
a=mid:audio
a=ice-ufrag:mediaUfrag
a=ice-pwd:mediaPassword
a=fingerprint:sha-256 AA:BB:CC:DD
a=setup:active
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:111 opus/48000/2
""";
        var parser = new SdpParser();

        SdpStatus status = parser.TryParse(Sdp, out SdpSessionDescription description);

        Assert.Equal(SdpStatus.Success, status);
        SdpMediaSection media = Assert.Single(description.MediaSections.ToArray());
        Assert.Equal("mediaUfrag", media.IceUsernameFragment);
        Assert.Equal("mediaPassword", media.IcePassword);
        Assert.Equal("active", media.Setup);
        Assert.True(media.RtcpMux);
        Assert.True(media.RtcpReducedSize);
        Assert.Equal("sha-256", Assert.Single(media.Fingerprints.ToArray()).Algorithm);
        Assert.Equal([0xAA, 0xBB, 0xCC, 0xDD], media.Fingerprints.Span[0].Fingerprint.ToArray());
    }
}
