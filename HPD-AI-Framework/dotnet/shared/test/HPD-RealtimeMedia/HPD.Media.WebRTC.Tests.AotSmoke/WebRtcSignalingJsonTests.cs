#nullable enable

using System.Buffers;
using System.Text;
using HPD.Media.WebRTC;

namespace HPD.Media.WebRTC.Tests.AotSmoke;

public sealed class WebRtcSignalingJsonTests
{
    [Fact]
    public void SessionDescription_RoundTripsOffer()
    {
        var description = new WebRtcSessionDescription
        {
            Type = WebRtcSessionDescriptionType.Offer,
            Sdp = "v=0\r\no=- 1 2 IN IP4 127.0.0.1\r\ns=-\r\n"
        };
        var writer = new ArrayBufferWriter<byte>();

        WebRtcSignalingJson.WriteSessionDescription(description, writer);
        bool parsed = WebRtcSignalingJson.TryParseSessionDescription(writer.WrittenSpan, out WebRtcSessionDescription reparsed);

        Assert.True(parsed);
        Assert.Contains("\"type\":\"offer\"", Encoding.UTF8.GetString(writer.WrittenSpan));
        Assert.Equal(description.Type, reparsed.Type);
        Assert.Equal(description.Sdp, reparsed.Sdp);
    }

    [Fact]
    public void SessionDescription_RejectsUnknownType()
    {
        ReadOnlySpan<byte> json = """{"type":"pranswer","sdp":"v=0"}"""u8;

        bool parsed = WebRtcSignalingJson.TryParseSessionDescription(json, out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData("""{"type":"offer","sdp":""}""")]
    [InlineData("""{"type":"offer","sdp":"   "}""")]
    public void SessionDescription_RejectsEmptySdp(string json)
    {
        bool parsed = WebRtcSignalingJson.TryParseSessionDescription(Encoding.UTF8.GetBytes(json), out _);

        Assert.False(parsed);
    }

    [Fact]
    public void SessionDescription_RejectsMalformedJsonAndTrailingTokens()
    {
        ReadOnlySpan<byte> malformed = """{"type":"offer","sdp":"""u8;
        ReadOnlySpan<byte> trailing = """{"type":"offer","sdp":"v=0"}{"type":"answer","sdp":"v=0"}"""u8;

        bool malformedParsed = WebRtcSignalingJson.TryParseSessionDescription(malformed, out _);
        bool trailingParsed = WebRtcSignalingJson.TryParseSessionDescription(trailing, out _);

        Assert.False(malformedParsed);
        Assert.False(trailingParsed);
    }

    [Fact]
    public void SessionDescription_WriteRejectsUnknownType()
    {
        var description = new WebRtcSessionDescription
        {
            Type = (WebRtcSessionDescriptionType)99,
            Sdp = "v=0"
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => WebRtcSignalingJson.WriteSessionDescription(description, new ArrayBufferWriter<byte>()));
    }

    [Fact]
    public void SessionDescription_WriteRejectsEmptySdp()
    {
        var description = new WebRtcSessionDescription
        {
            Type = WebRtcSessionDescriptionType.Offer,
            Sdp = ""
        };

        Assert.Throws<ArgumentException>(
            () => WebRtcSignalingJson.WriteSessionDescription(description, new ArrayBufferWriter<byte>()));
    }

    [Fact]
    public void SessionDescription_WriteValidatesBeforeRequestingDestinationStorage()
    {
        var description = new WebRtcSessionDescription
        {
            Type = (WebRtcSessionDescriptionType)99,
            Sdp = "v=0"
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => WebRtcSignalingJson.WriteSessionDescription(description, new ThrowingByteBufferWriter()));
    }

    [Fact]
    public void IceCandidate_RoundTripsCandidateWithMidAndIndex()
    {
        var candidate = new WebRtcIceCandidate
        {
            Candidate = "candidate:842163049 1 udp 1677729535 192.0.2.33 54400 typ host",
            SdpMid = "audio",
            SdpMLineIndex = 0
        };
        var writer = new ArrayBufferWriter<byte>();

        WebRtcSignalingJson.WriteIceCandidate(candidate, writer);
        bool parsed = WebRtcSignalingJson.TryParseIceCandidate(writer.WrittenSpan, out WebRtcIceCandidate reparsed);

        Assert.True(parsed);
        Assert.Contains("\"candidate\":", Encoding.UTF8.GetString(writer.WrittenSpan));
        Assert.Equal(candidate.Candidate, reparsed.Candidate);
        Assert.Equal(candidate.SdpMid, reparsed.SdpMid);
        Assert.Equal(candidate.SdpMLineIndex, reparsed.SdpMLineIndex);
    }

    [Fact]
    public void IceCandidate_ParsesNullOptionalFields()
    {
        ReadOnlySpan<byte> json = """{"candidate":"candidate:1 1 udp 1 192.0.2.1 9 typ host","sdpMid":null,"sdpMLineIndex":null}"""u8;

        bool parsed = WebRtcSignalingJson.TryParseIceCandidate(json, out WebRtcIceCandidate candidate);

        Assert.True(parsed);
        Assert.Null(candidate.SdpMid);
        Assert.Null(candidate.SdpMLineIndex);
    }

    [Fact]
    public void IceCandidate_RejectsMissingCandidate()
    {
        ReadOnlySpan<byte> json = """{"sdpMid":"audio"}"""u8;

        bool parsed = WebRtcSignalingJson.TryParseIceCandidate(json, out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData("""{"candidate":"","sdpMid":"audio","sdpMLineIndex":0}""")]
    [InlineData("""{"candidate":"   ","sdpMid":"audio","sdpMLineIndex":0}""")]
    [InlineData("""{"candidate":"candidate:1 1 udp 1 192.0.2.1 9 typ host","sdpMid":"","sdpMLineIndex":0}""")]
    [InlineData("""{"candidate":"candidate:1 1 udp 1 192.0.2.1 9 typ host","sdpMid":"   ","sdpMLineIndex":0}""")]
    [InlineData("""{"candidate":"candidate:1 1 udp 1 192.0.2.1 9 typ host","sdpMid":"audio","sdpMLineIndex":-1}""")]
    public void IceCandidate_RejectsValuesThatWriterWouldReject(string json)
    {
        bool parsed = WebRtcSignalingJson.TryParseIceCandidate(Encoding.UTF8.GetBytes(json), out _);

        Assert.False(parsed);
    }

    [Fact]
    public void IceCandidate_RejectsMalformedJsonAndTrailingTokens()
    {
        ReadOnlySpan<byte> malformed = """{"candidate":"""u8;
        ReadOnlySpan<byte> trailing = """{"candidate":"candidate:1 1 udp 1 192.0.2.1 9 typ host"}[]"""u8;

        bool malformedParsed = WebRtcSignalingJson.TryParseIceCandidate(malformed, out _);
        bool trailingParsed = WebRtcSignalingJson.TryParseIceCandidate(trailing, out _);

        Assert.False(malformedParsed);
        Assert.False(trailingParsed);
    }

    [Fact]
    public void IceCandidate_WriteValidatesBeforeRequestingDestinationStorage()
    {
        var candidate = new WebRtcIceCandidate
        {
            Candidate = "",
            SdpMid = "audio",
            SdpMLineIndex = 0
        };

        Assert.Throws<ArgumentException>(
            () => WebRtcSignalingJson.WriteIceCandidate(candidate, new ThrowingByteBufferWriter()));
    }

    [Fact]
    public void SignalEvent_RoundTripsRemoteDescription()
    {
        var signalEvent = new WebRtcSignalEvent
        {
            Kind = WebRtcSignalEventKind.RemoteDescriptionReceived,
            NegotiationId = "negotiation-1",
            Description = new WebRtcSessionDescription
            {
                Type = WebRtcSessionDescriptionType.Offer,
                Sdp = "v=0\r\ns=-\r\n"
            }
        };
        var writer = new ArrayBufferWriter<byte>();

        WebRtcSignalingJson.WriteSignalEvent(signalEvent, writer);
        bool parsed = WebRtcSignalingJson.TryParseSignalEvent(writer.WrittenSpan, out WebRtcSignalEvent reparsed);

        Assert.True(parsed);
        Assert.Contains("\"kind\":\"remoteDescriptionReceived\"", Encoding.UTF8.GetString(writer.WrittenSpan));
        Assert.Equal(signalEvent.Kind, reparsed.Kind);
        Assert.Equal(signalEvent.NegotiationId, reparsed.NegotiationId);
        Assert.Equal(signalEvent.Description.Type, reparsed.Description.Type);
        Assert.Equal(signalEvent.Description.Sdp, reparsed.Description.Sdp);
    }

    [Fact]
    public void SignalEvent_RoundTripsRemoteIceCandidate()
    {
        var signalEvent = new WebRtcSignalEvent
        {
            Kind = WebRtcSignalEventKind.RemoteIceCandidateReceived,
            Candidate = new WebRtcIceCandidate
            {
                Candidate = "candidate:842163049 1 udp 1677729535 192.0.2.33 54400 typ host",
                SdpMid = "audio",
                SdpMLineIndex = 0
            }
        };
        var writer = new ArrayBufferWriter<byte>();

        WebRtcSignalingJson.WriteSignalEvent(signalEvent, writer);
        bool parsed = WebRtcSignalingJson.TryParseSignalEvent(writer.WrittenSpan, out WebRtcSignalEvent reparsed);

        Assert.True(parsed);
        Assert.Equal(signalEvent.Kind, reparsed.Kind);
        Assert.Equal(signalEvent.Candidate.Candidate, reparsed.Candidate.Candidate);
        Assert.Equal(signalEvent.Candidate.SdpMid, reparsed.Candidate.SdpMid);
        Assert.Equal(signalEvent.Candidate.SdpMLineIndex, reparsed.Candidate.SdpMLineIndex);
    }

    [Fact]
    public void SignalEvent_RoundTripsProtocolErrorMessage()
    {
        var signalEvent = new WebRtcSignalEvent
        {
            Kind = WebRtcSignalEventKind.SignalingProtocolError,
            NegotiationId = "negotiation-2",
            Message = "malformed candidate"
        };
        var writer = new ArrayBufferWriter<byte>();

        WebRtcSignalingJson.WriteSignalEvent(signalEvent, writer);
        bool parsed = WebRtcSignalingJson.TryParseSignalEvent(writer.WrittenSpan, out WebRtcSignalEvent reparsed);

        Assert.True(parsed);
        Assert.Equal(signalEvent.Kind, reparsed.Kind);
        Assert.Equal(signalEvent.NegotiationId, reparsed.NegotiationId);
        Assert.Equal(signalEvent.Message, reparsed.Message);
    }

    [Fact]
    public void SignalEvent_RejectsDescriptionEventWithoutDescription()
    {
        ReadOnlySpan<byte> json = """{"kind":"remoteDescriptionReceived"}"""u8;

        bool parsed = WebRtcSignalingJson.TryParseSignalEvent(json, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void SignalEvent_RejectsUnknownKind()
    {
        ReadOnlySpan<byte> json = """{"kind":"mystery"}"""u8;

        bool parsed = WebRtcSignalingJson.TryParseSignalEvent(json, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void SignalEvent_RejectsInvalidNestedIceCandidate()
    {
        ReadOnlySpan<byte> json = """{"kind":"remoteIceCandidateReceived","candidate":{"candidate":"","sdpMid":"audio","sdpMLineIndex":0}}"""u8;

        bool parsed = WebRtcSignalingJson.TryParseSignalEvent(json, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void SignalEvent_RejectsMalformedJsonAndTrailingTokens()
    {
        ReadOnlySpan<byte> malformed = """{"kind":"""u8;
        ReadOnlySpan<byte> trailing = """{"kind":"remoteEndOfCandidatesReceived"}{"kind":"signalingDisconnected"}"""u8;

        bool malformedParsed = WebRtcSignalingJson.TryParseSignalEvent(malformed, out _);
        bool trailingParsed = WebRtcSignalingJson.TryParseSignalEvent(trailing, out _);

        Assert.False(malformedParsed);
        Assert.False(trailingParsed);
    }

    [Fact]
    public void SignalEvent_WriteRejectsUnknownKind()
    {
        var signalEvent = new WebRtcSignalEvent
        {
            Kind = (WebRtcSignalEventKind)99
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => WebRtcSignalingJson.WriteSignalEvent(signalEvent, new ArrayBufferWriter<byte>()));
    }

    [Fact]
    public void SignalEvent_WriteValidatesNestedPayloadBeforeRequestingDestinationStorage()
    {
        var signalEvent = new WebRtcSignalEvent
        {
            Kind = WebRtcSignalEventKind.RemoteIceCandidateReceived,
            Candidate = new WebRtcIceCandidate
            {
                Candidate = "",
                SdpMid = "audio",
                SdpMLineIndex = 0
            }
        };

        Assert.Throws<ArgumentException>(
            () => WebRtcSignalingJson.WriteSignalEvent(signalEvent, new ThrowingByteBufferWriter()));
    }

    private sealed class ThrowingByteBufferWriter : IBufferWriter<byte>
    {
        public void Advance(int count)
        {
            throw new InvalidOperationException("Advance should not be called.");
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            throw new InvalidOperationException("GetMemory should not be called.");
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            throw new InvalidOperationException("GetSpan should not be called.");
        }
    }
}
