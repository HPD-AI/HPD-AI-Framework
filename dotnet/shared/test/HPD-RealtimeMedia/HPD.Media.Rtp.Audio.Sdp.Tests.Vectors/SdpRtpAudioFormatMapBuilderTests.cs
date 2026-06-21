#nullable enable

using HPD.Audio.Codecs;
using HPD.Media.Rtp.Audio;
using HPD.Media.Rtp.Audio.Sdp;
using HPD.Media.Sdp;

namespace HPD.Media.Rtp.Audio.Sdp.Tests.Vectors;

public sealed class SdpRtpAudioFormatMapBuilderTests
{
    [Fact]
    public void TryBuild_CreatesVersionedAudioMapFromWebRtcAudioSdp()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 9 UDP/TLS/RTP/SAVPF 111 0 8
a=mid:0
a=sendrecv
a=rtpmap:111 opus/48000/2
a=fmtp:111 minptime=10;useinbandfec=1;usedtx=1;maxplaybackrate=16000;stereo=1
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
""";
        var parser = new SdpParser();
        Assert.Equal(SdpStatus.Success, parser.TryParse(Sdp, out SdpSessionDescription description));
        SdpMediaSection media = Assert.Single(description.MediaSections.ToArray());

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(media, version: 42, out RtpAudioFormatMap map);

        Assert.True(built);
        Assert.Equal((ulong)42, map.Version);
        Assert.True(map.TryGetFormat(111, out RtpAudioFormatBinding opus));
        Assert.Equal(AudioEncoding.Opus, opus.EncodedFormat.Encoding);
        Assert.Equal(48000, opus.EncodedFormat.SampleRate);
        Assert.Equal(2, opus.EncodedFormat.ChannelCount);
        Assert.Equal(48000, opus.EncodedFormat.RtpClockRate);
        Assert.Equal(TimeSpan.FromMilliseconds(10), opus.DefaultPacketTime);
        Assert.NotNull(opus.EncodedFormat.Parameters);
        Assert.True(opus.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.OpusUseInBandFec, out EncodedAudioFormatParameter fec));
        Assert.True(fec.BooleanValue);
        Assert.True(opus.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.OpusDtx, out EncodedAudioFormatParameter dtx));
        Assert.True(dtx.BooleanValue);
        Assert.True(opus.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.OpusMaxPlaybackRate, out EncodedAudioFormatParameter maxRate));
        Assert.Equal(16000, maxRate.Int32Value);
        Assert.True(opus.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.OpusStereo, out EncodedAudioFormatParameter stereo));
        Assert.True(stereo.BooleanValue);

        Assert.True(map.TryGetFormat(0, out RtpAudioFormatBinding pcmu));
        Assert.Equal(AudioEncoding.Pcmu, pcmu.EncodedFormat.Encoding);
        Assert.Equal(8000, pcmu.EncodedFormat.SampleRate);
        Assert.Equal(1, pcmu.EncodedFormat.ChannelCount);
    }

    [Fact]
    public void TryBuild_UsesPtimeAttributeForDefaultPacketTime()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 9 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=ptime:20
""";
        var parser = new SdpParser();
        Assert.Equal(SdpStatus.Success, parser.TryParse(Sdp, out SdpSessionDescription description));

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(
            description.MediaSections.Span[0],
            version: 7,
            out RtpAudioFormatMap map);

        Assert.True(built);
        Assert.True(map.TryGetFormat(0, out RtpAudioFormatBinding binding));
        Assert.Equal(TimeSpan.FromMilliseconds(20), binding.DefaultPacketTime);
        Assert.NotNull(binding.EncodedFormat.Parameters);
        Assert.True(binding.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.PacketTimeMilliseconds, out EncodedAudioFormatParameter packetTime));
        Assert.Equal(20, packetTime.Int32Value);
    }

    [Fact]
    public void TryBuild_ExposesMaxPtimeAttributeAsTypedParameter()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 9 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=maxptime:60
""";
        var parser = new SdpParser();
        Assert.Equal(SdpStatus.Success, parser.TryParse(Sdp, out SdpSessionDescription description));

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(
            description.MediaSections.Span[0],
            version: 8,
            out RtpAudioFormatMap map);

        Assert.True(built);
        Assert.True(map.TryGetFormat(0, out RtpAudioFormatBinding binding));
        Assert.NotNull(binding.EncodedFormat.Parameters);
        Assert.True(binding.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.MaxPacketTimeMilliseconds, out EncodedAudioFormatParameter maxPacketTime));
        Assert.Equal(60, maxPacketTime.Int32Value);
    }

    [Fact]
    public void TryBuild_MediaLevelPtimeOverridesFmtpPacketTimeParameter()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 9 RTP/AVP 111
a=rtpmap:111 opus/48000/2
a=fmtp:111 minptime=10
a=ptime:20
""";
        var parser = new SdpParser();
        Assert.Equal(SdpStatus.Success, parser.TryParse(Sdp, out SdpSessionDescription description));

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(
            description.MediaSections.Span[0],
            version: 9,
            out RtpAudioFormatMap map);

        Assert.True(built);
        Assert.True(map.TryGetFormat(111, out RtpAudioFormatBinding binding));
        Assert.Equal(TimeSpan.FromMilliseconds(20), binding.DefaultPacketTime);
        Assert.NotNull(binding.EncodedFormat.Parameters);
        Assert.True(binding.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.PacketTimeMilliseconds, out EncodedAudioFormatParameter packetTime));
        Assert.Equal(20, packetTime.Int32Value);
    }

    [Fact]
    public void TryBuild_IgnoresInvalidTypedFmtpParameters()
    {
        const string Sdp = """
v=0
o=- 1 1 IN IP4 127.0.0.1
s=-
t=0 0
m=audio 9 RTP/AVP 111
a=rtpmap:111 opus/48000/2
a=fmtp:111 useinbandfec=2;usedtx=-1;stereo=0;minptime=0;maxptime=-20;maxplaybackrate=0
""";
        var parser = new SdpParser();
        Assert.Equal(SdpStatus.Success, parser.TryParse(Sdp, out SdpSessionDescription description));

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(
            description.MediaSections.Span[0],
            version: 12,
            out RtpAudioFormatMap map);

        Assert.True(built);
        Assert.True(map.TryGetFormat(111, out RtpAudioFormatBinding binding));
        Assert.NotNull(binding.EncodedFormat.Parameters);
        Assert.False(binding.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.OpusUseInBandFec, out _));
        Assert.False(binding.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.OpusDtx, out _));
        Assert.True(binding.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.OpusStereo, out EncodedAudioFormatParameter stereo));
        Assert.False(stereo.BooleanValue);
        Assert.False(binding.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.PacketTimeMilliseconds, out _));
        Assert.False(binding.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.MaxPacketTimeMilliseconds, out _));
        Assert.False(binding.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.OpusMaxPlaybackRate, out _));
        Assert.Null(binding.DefaultPacketTime);
    }

    [Fact]
    public void TryBuild_IgnoresRtpMapPayloadTypesNotListedByMediaLine()
    {
        var media = new SdpMediaSection
        {
            Kind = SdpMediaKind.Audio,
            PayloadTypes = new byte[] { 0 },
            RtpMaps = new SdpRtpMap[]
            {
                new() { PayloadType = 0, EncodingName = "PCMU", ClockRate = 8000 },
                new() { PayloadType = 111, EncodingName = "opus", ClockRate = 48000, ChannelCount = 2 }
            },
            Fmtps = new SdpFmtp[]
            {
                new() { PayloadType = 111, Parameters = "useinbandfec=1" }
            }
        };

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(
            media,
            version: 10,
            out RtpAudioFormatMap map);

        Assert.True(built);
        Assert.True(map.TryGetFormat(0, out RtpAudioFormatBinding pcmu));
        Assert.Equal(AudioEncoding.Pcmu, pcmu.EncodedFormat.Encoding);
        Assert.False(map.TryGetFormat(111, out _));
    }

    [Fact]
    public void TryBuild_ReturnsFalseWhenNoListedPayloadTypeHasSupportedRtpMap()
    {
        var media = new SdpMediaSection
        {
            Kind = SdpMediaKind.Audio,
            PayloadTypes = new byte[] { 99 },
            RtpMaps = new SdpRtpMap[]
            {
                new() { PayloadType = 111, EncodingName = "opus", ClockRate = 48000, ChannelCount = 2 }
            }
        };

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(
            media,
            version: 11,
            out _);

        Assert.False(built);
    }

    [Fact]
    public void TryBuild_SkipsUnusableRtpMapValues()
    {
        byte[] payloadTypes = [0, 111, 200, 9];
        SdpRtpMap[] rtpMaps =
        [
            new() { PayloadType = 0, EncodingName = "PCMU", ClockRate = 8000 },
            new() { PayloadType = 111, EncodingName = "opus", ClockRate = 0, ChannelCount = 2 },
            new() { PayloadType = 200, EncodingName = "PCMA", ClockRate = 8000 },
            new() { PayloadType = 9, EncodingName = "opus", ClockRate = 48000, ChannelCount = 0 }
        ];
        var media = new SdpMediaSection
        {
            Kind = SdpMediaKind.Audio,
            PayloadTypes = payloadTypes,
            RtpMaps = rtpMaps
        };

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(media, version: 13, out RtpAudioFormatMap map);

        Assert.True(built);
        Assert.True(map.TryGetFormat(0, out RtpAudioFormatBinding pcmu));
        Assert.Equal(AudioEncoding.Pcmu, pcmu.EncodedFormat.Encoding);
        Assert.False(map.TryGetFormat(111, out _));
        Assert.False(map.TryGetFormat(200, out _));
        Assert.False(map.TryGetFormat(9, out _));
    }

    [Fact]
    public void TryBuild_ReturnsFalseWhenOnlyRtpMapsAreUnusable()
    {
        byte[] payloadTypes = [111];
        SdpRtpMap[] rtpMaps =
        [
            new() { PayloadType = 111, EncodingName = "opus", ClockRate = 48000, ChannelCount = 0 }
        ];
        var media = new SdpMediaSection
        {
            Kind = SdpMediaKind.Audio,
            PayloadTypes = payloadTypes,
            RtpMaps = rtpMaps
        };

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(media, version: 14, out _);

        Assert.False(built);
    }

    [Fact]
    public void TryBuild_ReturnsFalseForDuplicateUsablePayloadTypeBindings()
    {
        byte[] payloadTypes = [111];
        SdpRtpMap[] rtpMaps =
        [
            new() { PayloadType = 111, EncodingName = "opus", ClockRate = 48000, ChannelCount = 2 },
            new() { PayloadType = 111, EncodingName = "PCMU", ClockRate = 8000 }
        ];
        var media = new SdpMediaSection
        {
            Kind = SdpMediaKind.Audio,
            PayloadTypes = payloadTypes,
            RtpMaps = rtpMaps
        };

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(media, version: 15, out _);

        Assert.False(built);
    }

    [Fact]
    public void TryBuild_IgnoresOversizedPtimeAttribute()
    {
        SdpRtpMap[] rtpMaps =
        [
            new() { PayloadType = 0, EncodingName = "PCMU", ClockRate = 8000 }
        ];
        SdpAttribute[] attributes =
        [
            new() { Name = "ptime", Value = "999999999999999999999999999999999999999" }
        ];
        var media = new SdpMediaSection
        {
            Kind = SdpMediaKind.Audio,
            PayloadTypes = new byte[] { 0 },
            RtpMaps = rtpMaps,
            Attributes = attributes
        };

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(media, version: 16, out RtpAudioFormatMap map);

        Assert.True(built);
        Assert.True(map.TryGetFormat(0, out RtpAudioFormatBinding binding));
        Assert.Null(binding.DefaultPacketTime);
    }

    [Fact]
    public void TryBuild_ReturnsFalseWhenMediaPayloadTypesAreEmpty()
    {
        SdpRtpMap[] rtpMaps =
        [
            new() { PayloadType = 0, EncodingName = "PCMU", ClockRate = 8000 }
        ];
        var media = new SdpMediaSection
        {
            Kind = SdpMediaKind.Audio,
            RtpMaps = rtpMaps
        };

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(media, version: 17, out _);

        Assert.False(built);
    }

    [Fact]
    public void TryBuild_ReturnsFalseForNonAudioMedia()
    {
        var media = new SdpMediaSection
        {
            Kind = SdpMediaKind.Application
        };

        bool built = SdpRtpAudioFormatMapBuilder.TryBuild(media, version: 1, out _);

        Assert.False(built);
    }
}
