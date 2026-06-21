#nullable enable

using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HPD.Audio.Codecs;
using HPD.Audio.Primitives;
using HPD.Audio.WebRTC;
using HPD.Media.Rtcp;
using HPD.Media.Rtcp.Feedback;
using HPD.Media.Rtp;
using HPD.Media.Rtp.Audio;
using HPD.Media.Rtp.Audio.Sdp;
using HPD.Media.Sdp;
using HPD.Media.Srtp;
using HPD.Media.Transport;
using HPD.Media.WebRTC;

const string BrowserAudioOffer = """
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

SmokeWebRtcSignalingJson();
SmokeSdpNegotiationAndRtpAudioMap();
SmokeWebRtcSessionDescriptionBuilder();
SmokeStunBinding();
SmokeTurnChannelData();
SmokeTurnAllocationControl();
SmokeRtpAndRtcp();
SmokeRtcpFeedback();
SmokeSrtpAndKeySchedule();
SmokeWebRtcAudioInboundPump();

Console.WriteLine("HPD.Media AOT smoke passed.");
return 0;

static void SmokeWebRtcSignalingJson()
{
    var description = new WebRtcSessionDescription
    {
        Type = WebRtcSessionDescriptionType.Offer,
        Sdp = BrowserAudioOffer
    };
    var json = new ArrayBufferWriter<byte>();
    WebRtcSignalingJson.WriteSessionDescription(description, json);
    Require(
        WebRtcSignalingJson.TryParseSessionDescription(json.WrittenSpan, out WebRtcSessionDescription parsed),
        "WebRTC session-description JSON did not parse.");
    Require(parsed.Type == description.Type && parsed.Sdp == description.Sdp, "WebRTC session-description JSON did not round-trip.");

    var candidate = new WebRtcIceCandidate
    {
        Candidate = "candidate:842163049 1 udp 1677729535 192.0.2.33 54400 typ host",
        SdpMid = "audio",
        SdpMLineIndex = 0
    };
    json.Clear();
    WebRtcSignalingJson.WriteIceCandidate(candidate, json);
    Require(
        WebRtcSignalingJson.TryParseIceCandidate(json.WrittenSpan, out WebRtcIceCandidate parsedCandidate),
        "WebRTC ICE-candidate JSON did not parse.");
    Require(parsedCandidate.Candidate == candidate.Candidate, "WebRTC ICE-candidate JSON did not round-trip.");

    var signalEvent = new WebRtcSignalEvent
    {
        Kind = WebRtcSignalEventKind.RemoteDescriptionReceived,
        NegotiationId = "negotiation-1",
        Description = description
    };
    json.Clear();
    WebRtcSignalingJson.WriteSignalEvent(signalEvent, json);
    Require(
        WebRtcSignalingJson.TryParseSignalEvent(json.WrittenSpan, out WebRtcSignalEvent parsedEvent),
        "WebRTC signaling event JSON did not parse.");
    Require(parsedEvent.Kind == signalEvent.Kind, "WebRTC signaling event kind did not round-trip.");
    Require(parsedEvent.NegotiationId == signalEvent.NegotiationId, "WebRTC signaling event negotiation id did not round-trip.");
    Require(parsedEvent.Description.Sdp == description.Sdp, "WebRTC signaling event description did not round-trip.");
}

static void SmokeSdpNegotiationAndRtpAudioMap()
{
    var parser = new SdpParser();
    var writer = new SdpWriter();
    var description = new WebRtcSessionDescription
    {
        Type = WebRtcSessionDescriptionType.Offer,
        Sdp = BrowserAudioOffer
    };

    Require(
        WebRtcSdpNegotiation.TryParse(description, parser, out WebRtcParsedSessionDescription negotiation, out SdpStatus status),
        $"WebRTC SDP negotiation failed: {status}.");

    Require(negotiation.MediaDescriptions.Length == 1, "Expected one negotiated media section.");
    WebRtcMediaDescription media = negotiation.MediaDescriptions.Span[0];
    Require(media.IceCredentials is not null, "Expected resolved ICE credentials.");
    Require(media.ExpectedPeerIdentity is not null, "Expected resolved peer identity.");
    Require(media.Setup == WebRtcDtlsSetup.ActPass, "Expected actpass DTLS setup.");

    var sdpText = new ArrayBufferWriter<char>();
    Require(writer.TryWrite(negotiation.Sdp, sdpText) == SdpStatus.Success, "SDP writer failed.");

    Require(
        SdpRtpAudioFormatMapBuilder.TryBuild(media.SdpMedia, version: 1, out RtpAudioFormatMap formatMap),
        "SDP RTP-audio format map build failed.");
    Require(formatMap.TryGetFormat(111, out RtpAudioFormatBinding binding), "Opus payload type was not mapped.");
    Require(binding.EncodedFormat.Encoding == AudioEncoding.Opus, "Opus payload type mapped to the wrong encoding.");
    Require(
        binding.EncodedFormat.Parameters is not null &&
        binding.EncodedFormat.Parameters.TryGet(EncodedAudioParameter.OpusUseInBandFec, out EncodedAudioFormatParameter fec) &&
        fec.BooleanValue,
        "Opus FEC fmtp parameter was not mapped.");
}

static void SmokeWebRtcSessionDescriptionBuilder()
{
    var parser = new SdpParser();
    var writer = new SdpWriter();
    var remoteDescription = new WebRtcSessionDescription
    {
        Type = WebRtcSessionDescriptionType.Offer,
        Sdp = BrowserAudioOffer
    };
    Require(
        WebRtcSdpNegotiation.TryParse(remoteDescription, parser, out WebRtcParsedSessionDescription remoteOffer, out SdpStatus parseStatus),
        $"WebRTC remote offer parse failed before local answer generation: {parseStatus}.");

    WebRtcAudioSessionDescriptionOptions localOptions = CreateSmokeAudioDescriptionOptions();
    Require(
        WebRtcSessionDescriptionBuilder.TryCreateOffer(
            localOptions,
            writer,
            out WebRtcSessionDescription localOffer,
            out SdpStatus offerStatus),
        $"WebRTC local offer generation failed: {offerStatus}.");
    Require(localOffer.Type == WebRtcSessionDescriptionType.Offer, "WebRTC local offer had the wrong description type.");
    Require(parser.TryParse(localOffer.Sdp, out SdpSessionDescription parsedLocalOffer) == SdpStatus.Success, "WebRTC local offer SDP did not parse.");
    Require(parsedLocalOffer.MediaSections.Length == 1, "WebRTC local offer did not contain one media section.");

    Require(
        WebRtcSessionDescriptionBuilder.TryCreateAnswer(
            remoteOffer,
            localOptions,
            writer,
            out WebRtcSessionDescription localAnswer,
            out SdpStatus answerStatus),
        $"WebRTC local answer generation failed: {answerStatus}.");
    Require(localAnswer.Type == WebRtcSessionDescriptionType.Answer, "WebRTC local answer had the wrong description type.");
    Require(parser.TryParse(localAnswer.Sdp, out SdpSessionDescription parsedLocalAnswer) == SdpStatus.Success, "WebRTC local answer SDP did not parse.");
    SdpMediaSection answerAudio = parsedLocalAnswer.MediaSections.Span[0];
    Require(answerAudio.Mid == "0", "WebRTC local answer did not preserve the remote MID.");
    Require(answerAudio.Setup == "active", "WebRTC local answer did not resolve actpass to active.");
    Require(
        answerAudio.PayloadTypes.Length == 1 && answerAudio.PayloadTypes.Span[0] == 111,
        "WebRTC local answer did not select the negotiated Opus payload.");
}

static void SmokeStunBinding()
{
    Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
    for (int i = 0; i < transactionId.Length; i++)
    {
        transactionId[i] = (byte)(i + 1);
    }

    Span<byte> request = stackalloc byte[StunBindingMessage.HeaderLength];
    Require(
        StunBindingMessage.TryWriteBindingRequest(request, transactionId, out int requestBytes) &&
        requestBytes == StunBindingMessage.HeaderLength,
        "STUN binding request write failed.");

    Span<byte> response = stackalloc byte[64];
    var mappedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 54321);
    Require(
        StunBindingMessage.TryWriteBindingSuccessResponse(response, transactionId, mappedEndPoint, out int responseBytes),
        "STUN binding success response write failed.");
    Require(
        StunBindingMessage.TryParseBindingSuccessResponse(response[..responseBytes], transactionId, out IPEndPoint reparsed) &&
        reparsed.Equals(mappedEndPoint),
        "STUN binding success response did not round-trip.");
}

static void SmokeTurnChannelData()
{
    ReadOnlySpan<byte> payload = [0x80, 0x60, 0x00, 0x01, 0xCA];
    Span<byte> packet = stackalloc byte[32];
    Require(
        TurnChannelDataMessage.TryWrite(0x4001, payload, packet, out int bytesWritten) == TurnChannelDataStatus.Success,
        "TURN ChannelData write failed.");
    Require(bytesWritten == 12, "TURN ChannelData padded length was unexpected.");
    Require(
        TurnChannelDataMessage.TryParse(packet[..bytesWritten], out TurnChannelDataView channelData) == TurnChannelDataStatus.Success,
        "TURN ChannelData parse failed.");
    Require(channelData.ChannelNumber == 0x4001, "TURN ChannelData channel number did not round-trip.");
    Require(channelData.Payload.SequenceEqual(payload), "TURN ChannelData payload did not round-trip.");
}

static void SmokeTurnAllocationControl()
{
    Span<byte> transactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
    for (int i = 0; i < transactionId.Length; i++)
    {
        transactionId[i] = (byte)(0x30 + i);
    }

    byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey("user", "example.org", "pass");
    Span<byte> request = stackalloc byte[256];
    Span<byte> response = stackalloc byte[128];
    Span<byte> parsedTransactionId = stackalloc byte[StunBindingMessage.TransactionIdLength];
    var relayedEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.80"), 61237);
    var peerEndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.80"), 51347);

    Require(
        TurnAllocationMessage.TryWriteUdpAllocateRequest(request, transactionId, out int allocateRequestBytes) == TurnAllocationStatus.Success,
        "TURN Allocate request write failed.");
    Require(
        TurnAllocationMessage.TryParseUdpAllocateRequest(request[..allocateRequestBytes], parsedTransactionId) == TurnAllocationStatus.Success &&
        parsedTransactionId.SequenceEqual(transactionId),
        "TURN Allocate request did not parse.");
    Require(
        TurnAllocationMessage.TryWriteAllocateChallengeResponse(
            response,
            transactionId,
            401,
            "example.org",
            "nonce-value",
            out int challengeBytes) == TurnAllocationStatus.Success,
        "TURN Allocate challenge write failed.");
    Require(
        TurnAllocationMessage.TryParseAllocateChallengeResponse(
            response[..challengeBytes],
            transactionId,
            out TurnAllocationChallenge challenge) == TurnAllocationStatus.Unauthorized &&
        challenge.Realm == "example.org" &&
        challenge.Nonce == "nonce-value",
        "TURN Allocate challenge did not parse.");
    Require(
        TurnAllocationMessage.TryWriteAuthenticatedUdpAllocateRequest(
            request,
            transactionId,
            "user",
            "example.org",
            "nonce-value",
            key,
            out int authenticatedAllocateBytes) == TurnAllocationStatus.Success,
        "Authenticated TURN Allocate request write failed.");
    Require(
        TurnAllocationMessage.TryVerifyMessageIntegrity(request[..authenticatedAllocateBytes], key),
        "Authenticated TURN Allocate request MESSAGE-INTEGRITY failed.");
    Require(
        TurnAllocationMessage.TryWriteAllocateSuccessResponse(
            response,
            transactionId,
            relayedEndPoint,
            TimeSpan.FromSeconds(600),
            out int allocateSuccessBytes) == TurnAllocationStatus.Success,
        "TURN Allocate success write failed.");
    Require(
        TurnAllocationMessage.TryParseAllocateSuccessResponse(
            response[..allocateSuccessBytes],
            transactionId,
            out IPEndPoint parsedRelayedEndPoint,
            out TimeSpan parsedLifetime) == TurnAllocationStatus.Success &&
        parsedRelayedEndPoint.Equals(relayedEndPoint) &&
        parsedLifetime == TimeSpan.FromSeconds(600),
        "TURN Allocate success did not parse.");

    Require(
        TurnAllocationMessage.TryWriteAuthenticatedCreatePermissionRequest(
            request,
            transactionId,
            peerEndPoint,
            "user",
            "example.org",
            "nonce-value",
            key,
            out int permissionBytes) == TurnAllocationStatus.Success,
        "TURN CreatePermission request write failed.");
    Require(
        TurnAllocationMessage.TryParseCreatePermissionRequest(
            request[..permissionBytes],
            parsedTransactionId,
            out IPEndPoint parsedPermissionPeer) == TurnAllocationStatus.Success &&
        parsedPermissionPeer.Equals(peerEndPoint) &&
        TurnAllocationMessage.TryVerifyMessageIntegrity(request[..permissionBytes], key),
        "TURN CreatePermission request did not parse.");

    Require(
        TurnAllocationMessage.TryWriteAuthenticatedChannelBindRequest(
            request,
            transactionId,
            0x4001,
            peerEndPoint,
            "user",
            "example.org",
            "nonce-value",
            key,
            out int channelBindBytes) == TurnAllocationStatus.Success,
        "TURN ChannelBind request write failed.");
    Require(
        TurnAllocationMessage.TryParseChannelBindRequest(
            request[..channelBindBytes],
            parsedTransactionId,
            out ushort parsedChannelNumber,
            out IPEndPoint parsedChannelPeer) == TurnAllocationStatus.Success &&
        parsedChannelNumber == 0x4001 &&
        parsedChannelPeer.Equals(peerEndPoint) &&
        TurnAllocationMessage.TryVerifyMessageIntegrity(request[..channelBindBytes], key),
        "TURN ChannelBind request did not parse.");

    Require(
        TurnAllocationMessage.TryWriteAuthenticatedRefreshRequest(
            request,
            transactionId,
            TimeSpan.FromSeconds(300),
            "user",
            "example.org",
            "nonce-value",
            key,
            out int refreshBytes) == TurnAllocationStatus.Success,
        "TURN Refresh request write failed.");
    Require(
        TurnAllocationMessage.TryParseRefreshRequest(
            request[..refreshBytes],
            parsedTransactionId,
            out TimeSpan refreshLifetime) == TurnAllocationStatus.Success &&
        refreshLifetime == TimeSpan.FromSeconds(300) &&
        TurnAllocationMessage.TryVerifyMessageIntegrity(request[..refreshBytes], key),
        "TURN Refresh request did not parse.");

    var inner = new InMemoryDatagramPath();
    byte[] inboundPayload = [0x80, 0x60, 0x00, 0x02];
    byte[] encodedInbound = new byte[32];
    Require(
        TurnChannelDataMessage.TryWrite(0x4001, inboundPayload, encodedInbound, out int encodedInboundBytes) ==
            TurnChannelDataStatus.Success,
        "TURN ChannelData inbound test packet write failed.");
    inner.EnqueueReceive(encodedInbound.AsMemory(0, encodedInboundBytes));
    var path = new TurnChannelDataDatagramPath(inner, 0x4001);
    byte[] receiveBuffer = new byte[32];
    DatagramReceiveResult received = path.ReceiveAsync(receiveBuffer).AsTask().GetAwaiter().GetResult();
    Require(received.HasDatagram && receiveBuffer.AsSpan(0, received.BytesWritten).SequenceEqual(inboundPayload),
        "TURN ChannelData path did not unwrap inbound media.");

    byte[] outboundPayload = [0x80, 0x60, 0x00, 0x03];
    path.SendAsync(outboundPayload).AsTask().GetAwaiter().GetResult();
    Require(
        inner.SentDatagrams.Count == 1 &&
        TurnChannelDataMessage.TryParse(inner.SentDatagrams[0], out TurnChannelDataView outbound) ==
            TurnChannelDataStatus.Success &&
        outbound.ChannelNumber == 0x4001 &&
        outbound.Payload.SequenceEqual(outboundPayload),
        "TURN ChannelData path did not wrap outbound media.");
    path.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

static void SmokeRtpAndRtcp()
{
    byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];
    uint[] csrcs = [0xCAFE0001u, 0xCAFE0002u];
    var packet = new RtpPacket
    {
        Header = new RtpHeader
        {
            PayloadType = 111,
            Marker = true,
            SequenceNumber = 0x1234,
            Timestamp = 0x01020304,
            Ssrc = 0xA1A2A3A4,
            CsrcCount = 2
        },
        Csrcs = csrcs,
        Payload = payload,
        ArrivalTime = DateTimeOffset.UnixEpoch
    };

    Span<byte> rtpBuffer = stackalloc byte[64];
    Require(RtpPacketWriter.TryWrite(packet, rtpBuffer, out int rtpBytes) == RtpPacketStatus.Success, "RTP write failed.");
    Require(RtpPacketReader.TryParse(rtpBuffer[..rtpBytes], out RtpPacketView view) == RtpPacketStatus.Success, "RTP parse failed.");
    Require(view.Header.SequenceNumber == 0x1234 && view.Payload.SequenceEqual(payload), "RTP packet did not round-trip.");
    RtpCsrcEnumerator parsedCsrcs = view.GetCsrcs();
    Require(parsedCsrcs.MoveNext() && parsedCsrcs.Current == csrcs[0], "RTP first CSRC did not round-trip.");
    Require(parsedCsrcs.MoveNext() && parsedCsrcs.Current == csrcs[1], "RTP second CSRC did not round-trip.");
    Require(!parsedCsrcs.MoveNext(), "RTP CSRC traversal produced extra values.");

    Span<RtcpReceptionReportBlock> reports = stackalloc RtcpReceptionReportBlock[1];
    reports[0] = new RtcpReceptionReportBlock
    {
        Ssrc = 0xA1A2A3A4,
        FractionLost = 0,
        CumulativePacketsLost = 0,
        ExtendedHighestSequenceNumberReceived = 0x1234,
        InterarrivalJitter = 5,
        LastSenderReport = 0,
        DelaySinceLastSenderReport = 0
    };
    var receiverReport = new RtcpReceiverReport
    {
        ReporterSsrc = 0x01020304,
        Reports = reports.ToArray()
    };

    Span<byte> rtcpBuffer = stackalloc byte[64];
    Require(
        RtcpPacketWriter.TryWriteReceiverReport(receiverReport, rtcpBuffer, out int rtcpBytes) == RtcpPacketStatus.Success,
        "RTCP receiver-report write failed.");
    Require(
        RtcpPacketReader.TryReadCompound(rtcpBuffer[..rtcpBytes], out RtcpCompoundPacketEnumerator compound) == RtcpPacketStatus.Success,
        "RTCP compound read failed.");
    Require(compound.MoveNext() && compound.Current.PacketType == RtcpPacketType.ReceiverReport, "RTCP compound packet was not a receiver report.");

    ReadOnlySpan<byte> sdesPacket =
    [
        0x81, 0xCA, 0x00, 0x03,
        0x01, 0x02, 0x03, 0x04,
        0x01, 0x04,
        (byte)'t', (byte)'e', (byte)'s', (byte)'t',
        0x00, 0x00
    ];
    Require(
        RtcpPacketReader.TryReadSourceDescription(sdesPacket, out RtcpSdesChunkEnumerator sdesChunks) == RtcpPacketStatus.Success,
        "RTCP SDES view read failed.");
    Require(sdesChunks.MoveNext(), "RTCP SDES chunk was missing.");
    Require(sdesChunks.Current.Ssrc == 0x01020304, "RTCP SDES chunk SSRC did not parse.");
    RtcpSdesItemEnumerator sdesItems = sdesChunks.Current.GetItems();
    Require(sdesItems.MoveNext(), "RTCP SDES item was missing.");
    Require(sdesItems.Current.Type == 1, "RTCP SDES item type did not parse.");
    Require(sdesItems.Current.Utf8Value.SequenceEqual("test"u8), "RTCP SDES item value did not parse.");
    Require(!sdesItems.MoveNext() && !sdesChunks.MoveNext(), "RTCP SDES traversal produced extra items.");
}

static void SmokeRtcpFeedback()
{
    RtcpNackEntry[] nackEntries =
    [
        new()
        {
            PacketId = 0x1200,
            LostPacketBitmask = 0x0001
        }
    ];
    var nack = new RtcpNackPacket
    {
        SenderSsrc = 0x01020304,
        MediaSsrc = 0x05060708,
        Entries = nackEntries
    };
    Span<byte> nackBuffer = stackalloc byte[16];
    RtcpNackEntry[] parsedNackEntries = new RtcpNackEntry[1];
    Require(
        RtcpFeedbackPacketWriter.TryWriteGenericNack(nack, nackBuffer, out int nackBytes) == RtcpPacketStatus.Success,
        "RTCP Generic NACK write failed.");
    Require(
        RtcpFeedbackPacketReader.TryParseGenericNack(nackBuffer[..nackBytes], parsedNackEntries, out RtcpNackPacket parsedNack) == RtcpPacketStatus.Success,
        "RTCP Generic NACK parse failed.");
    Require(parsedNack.Entries.Span[0].Contains(0x1201), "RTCP Generic NACK bitmask did not round-trip.");

    var fir = new RtcpFullIntraRequest
    {
        SenderSsrc = 0x01020304,
        MediaSsrc = 0,
        Entries = new[]
        {
            new RtcpFullIntraRequestEntry
            {
                Ssrc = 0x10203040,
                SequenceNumber = 7
            }
        }
    };
    Span<byte> firBuffer = stackalloc byte[20];
    RtcpFullIntraRequestEntry[] parsedFirEntries = new RtcpFullIntraRequestEntry[1];
    Require(
        RtcpFeedbackPacketWriter.TryWriteFullIntraRequest(fir, firBuffer, out int firBytes) == RtcpPacketStatus.Success,
        "RTCP FIR write failed.");
    Require(
        RtcpFeedbackPacketReader.TryParseFullIntraRequest(firBuffer[..firBytes], parsedFirEntries, out RtcpFullIntraRequest parsedFir) == RtcpPacketStatus.Success,
        "RTCP FIR parse failed.");
    Require(parsedFir.Entries.Span[0].SequenceNumber == 7, "RTCP FIR sequence number did not round-trip.");

    uint[] rembSsrcs = [0x10203040];
    var remb = new RtcpReceiverEstimatedMaximumBitrate
    {
        SenderSsrc = 0x01020304,
        MediaSsrc = 0,
        BitrateBitsPerSecond = 1_000_000,
        Ssrcs = rembSsrcs
    };
    Span<byte> rembBuffer = stackalloc byte[24];
    uint[] parsedRembSsrcs = new uint[1];
    Require(
        RtcpFeedbackPacketWriter.TryWriteReceiverEstimatedMaximumBitrate(remb, rembBuffer, out int rembBytes) == RtcpPacketStatus.Success,
        "RTCP REMB write failed.");
    Require(
        RtcpFeedbackPacketReader.TryParseReceiverEstimatedMaximumBitrate(
            rembBuffer[..rembBytes],
            parsedRembSsrcs,
            out RtcpReceiverEstimatedMaximumBitrate parsedRemb) == RtcpPacketStatus.Success,
        "RTCP REMB parse failed.");
    Require(parsedRemb.BitrateBitsPerSecond == 1_000_000, "RTCP REMB bitrate did not round-trip.");
    Require(parsedRemb.Ssrcs.Span[0] == 0x10203040, "RTCP REMB SSRC did not round-trip.");

    byte[] feedbackControlInformation = [0xAA, 0xBB, 0xCC, 0xDD];
    var rawFeedback = new RtcpFeedbackPacket
    {
        PacketType = RtcpPacketType.PayloadFeedback,
        FeedbackMessageType = 15,
        SenderSsrc = 0x01020304,
        MediaSsrc = 0x05060708,
        FeedbackControlInformation = feedbackControlInformation
    };
    Span<byte> feedbackBuffer = stackalloc byte[16];
    Require(
        RtcpFeedbackPacketWriter.TryWrite(rawFeedback, feedbackBuffer, out int feedbackBytes) == RtcpPacketStatus.Success,
        "RTCP generic feedback write failed.");
    Require(
        RtcpFeedbackPacketReader.TryParse(feedbackBuffer[..feedbackBytes], out RtcpFeedbackPacketView feedbackView) == RtcpPacketStatus.Success,
        "RTCP generic feedback parse failed.");
    Require(
        feedbackView.PacketType == RtcpPacketType.PayloadFeedback &&
        feedbackView.FeedbackMessageType == 15 &&
        feedbackView.FeedbackControlInformation.SequenceEqual(feedbackControlInformation),
        "RTCP generic feedback did not round-trip.");
}

static void SmokeSrtpAndKeySchedule()
{
    byte[] masterKey = Convert.FromHexString("E1F97A0D3E018BE0D64FA32C06DE4139");
    byte[] masterSalt = Convert.FromHexString("0EC675AD498AFEEBB6960B3AABE6");
    uint ssrc = 0x11223344;
    var material = new SrtpProtectionMaterial
    {
        Profile = SrtpProtectionProfile.Aes128CmHmacSha1_80,
        OutboundMasterKey = masterKey,
        OutboundMasterSalt = masterSalt,
        InboundMasterKey = masterKey,
        InboundMasterSalt = masterSalt
    };
    IPacketProtectorFactory factory = new AesCmSha1SrtpPacketProtectorFactoryBuilder()
        .Create(material, new SrtpPacketProtectionOptions());
    IPacketProtector outbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Outbound, ssrc);
    IPacketProtector inbound = factory.Create(PacketProtectionPurpose.Rtp, PacketDirection.Inbound, ssrc);

    Span<byte> packet = stackalloc byte[64];
    WriteRtpPacket(packet, sequenceNumber: 1, ssrc);
    Require(outbound.Protect(packet, 24, out int protectedLength) == PacketProtectionStatus.Success, "SRTP protect failed.");
    Require(inbound.Unprotect(packet, protectedLength, out int unprotectedLength) == PacketProtectionStatus.Success, "SRTP unprotect failed.");
    Require(unprotectedLength == 24, "SRTP unprotected length was unexpected.");

    var keySchedule = new WebRtcSrtpKeySchedule();
    SrtpProtectionMaterial derived = keySchedule.Derive(new SecureHandshakeResult
    {
        LocalRole = DtlsRole.Client,
        NegotiatedSrtpProfile = SrtpProtectionProfile.Aes128CmHmacSha1_80,
        PeerProof = new PeerProofMaterial { CertificateDer = new byte[] { 0x01, 0x02, 0x03 } },
        KeyExporter = new FixedKeyExporter()
    });
    Require(derived.OutboundMasterKey.Length == 16, "DTLS-SRTP key schedule returned the wrong key length.");
    Require(derived.OutboundMasterSalt.Length == 14, "DTLS-SRTP key schedule returned the wrong salt length.");
}

static void SmokeWebRtcAudioInboundPump()
{
    var encodedFormat = new EncodedAudioFormat
    {
        Encoding = AudioEncoding.Pcmu,
        SampleRate = 8000,
        ChannelCount = 1,
        RtpClockRate = 8000
    };
    var formatMap = new RtpAudioFormatMap(
        1,
        [
            new RtpAudioFormatBinding
            {
                PayloadType = 0,
                EncodedFormat = encodedFormat,
                DefaultPacketTime = TimeSpan.FromMilliseconds(20)
            }
        ]);
    var pcmFormat = new AudioFormat
    {
        SampleRate = 8000,
        ChannelCount = 1,
        SampleFormat = AudioSampleFormat.Pcm16
    };
    var decoder = new SmokeRealtimeDecoder(pcmFormat);
    var sink = new SmokeFrameSink();
    var pump = new WebRtcAudioInboundPump(new SmokeNoOpPacketProtector(), formatMap, decoder, sink);

    Span<byte> packet = stackalloc byte[64];
    var rtpPacket = new RtpPacket
    {
        Header = new RtpHeader
        {
            PayloadType = 0,
            SequenceNumber = 7,
            Timestamp = 160,
            Ssrc = 0x01020304
        },
        Payload = new byte[] { 0x7F, 0x80, 0x81 },
        ArrivalTime = DateTimeOffset.UtcNow
    };
    Require(RtpPacketWriter.TryWrite(rtpPacket, packet, out int packetBytes) == RtpPacketStatus.Success, "WebRTC audio RTP write failed.");
    Require(
        pump.ProcessPacket(packet, packetBytes) == WebRtcAudioInboundStatus.Success,
        "WebRTC audio inbound pump failed.");
    Require(decoder.DecodeCount == 1, "WebRTC audio inbound pump did not decode one access unit.");
    Require(sink.FrameCount == 1, "WebRTC audio inbound pump did not emit one PCM frame.");

    var encoder = new SmokeRealtimeEncoder(pcmFormat, encodedFormat);
    var protectedSink = new SmokeProtectedPacketSink();
    var outbound = new WebRtcAudioOutboundPump(
        encoder,
        formatMap,
        new SmokeNoOpPacketProtector(),
        protectedSink,
        ssrc: 0x01020304,
        payloadType: 0,
        initialSequenceNumber: 8,
        initialTimestamp: 320);
    byte[] pcm = new byte[320];
    byte[] outboundScratch = new byte[64];
    Require(
        outbound.ProcessFrame(new AudioFrameView(pcm, pcmFormat, 160), outboundScratch) == WebRtcAudioOutboundStatus.Success,
        "WebRTC audio outbound pump failed.");
    Require(encoder.EncodeCount == 1, "WebRTC audio outbound pump did not encode one PCM frame.");
    Require(protectedSink.PacketCount == 1, "WebRTC audio outbound pump did not write one protected packet.");
    Require(
        RtpPacketReader.TryParse(protectedSink.LastPacket, out RtpPacketView outboundView) == RtpPacketStatus.Success &&
        outboundView.Header.PayloadType == 0 &&
        outboundView.Header.SequenceNumber == 8 &&
        outboundView.Header.Timestamp == 320,
        "WebRTC audio outbound pump did not write the expected RTP packet.");

    WebRtcAudioSecurityContext? context = WebRtcAudioSecurityContext.CreateAsync(new WebRtcAudioSecurityOptions
    {
        Path = new InMemoryDatagramPath(),
        SecureHandshake = new SmokeSecureHandshake(),
        LocalCertificate = new LocalCertificate { Certificate = new X509Certificate2() },
        HandshakeOptions = new SecureHandshakeOptions(),
        KeySchedule = new SmokeSrtpKeySchedule(),
        PacketProtectorFactoryProvider = new SmokePacketProtectorFactoryProvider(),
        PeerIdentityVerifier = new SmokePeerIdentityVerifier(),
        ExpectedPeerIdentity = new ExpectedPeerIdentity
        {
            FingerprintAlgorithm = CertificateFingerprintAlgorithm.Sha256,
            Fingerprint = new byte[32]
        }
    }).AsTask().GetAwaiter().GetResult();
    Require(context is not null, "WebRTC audio security context setup failed.");

    var securedDecoder = new SmokeRealtimeDecoder(pcmFormat);
    var securedSink = new SmokeFrameSink();
    WebRtcAudioInboundPump securedInbound = context.CreateInboundAudioPump(
        remoteSsrc: 0x01020304,
        formatMap,
        securedDecoder,
        securedSink);
    Span<byte> securedPacket = stackalloc byte[64];
    Require(RtpPacketWriter.TryWrite(rtpPacket, securedPacket, out int securedBytes) == RtpPacketStatus.Success, "Secured WebRTC audio RTP write failed.");
    Require(
        securedInbound.ProcessPacket(securedPacket, securedBytes) == WebRtcAudioInboundStatus.Success,
        "Secured WebRTC audio inbound pump failed.");

    var securedEncoder = new SmokeRealtimeEncoder(pcmFormat, encodedFormat);
    var securedProtectedSink = new SmokeProtectedPacketSink();
    WebRtcAudioOutboundPump securedOutbound = context.CreateOutboundAudioPump(
        localSsrc: 0x01020304,
        payloadType: 0,
        formatMap,
        securedEncoder,
        securedProtectedSink,
        initialSequenceNumber: 9,
        initialTimestamp: 480);
    Require(
        securedOutbound.ProcessFrame(new AudioFrameView(pcm, pcmFormat, 160), outboundScratch) == WebRtcAudioOutboundStatus.Success,
        "Secured WebRTC audio outbound pump failed.");
}

static WebRtcAudioSessionDescriptionOptions CreateSmokeAudioDescriptionOptions()
{
    return new WebRtcAudioSessionDescriptionOptions
    {
        LocalIceCredentials = new IceCredentials
        {
            UsernameFragment = "localUfrag",
            Password = "localPassword"
        },
        LocalCertificate = CreateSmokeCertificate(),
        Setup = WebRtcDtlsSetup.Active,
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

static LocalCertificate CreateSmokeCertificate()
{
    using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var request = new CertificateRequest(
        "CN=HPD Media AOT Smoke",
        key,
        HashAlgorithmName.SHA256);
    X509Certificate2 certificate = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddDays(1));
    return new LocalCertificate { Certificate = certificate };
}

static void WriteRtpPacket(Span<byte> packet, ushort sequenceNumber, uint ssrc)
{
    packet.Clear();
    packet[0] = 0x80;
    packet[1] = 0x60;
    packet[2] = (byte)(sequenceNumber >> 8);
    packet[3] = (byte)sequenceNumber;
    packet[4] = 0x01;
    packet[5] = 0x02;
    packet[6] = 0x03;
    packet[7] = 0x04;
    packet[8] = (byte)(ssrc >> 24);
    packet[9] = (byte)(ssrc >> 16);
    packet[10] = (byte)(ssrc >> 8);
    packet[11] = (byte)ssrc;

    for (int i = 12; i < 24; i++)
    {
        packet[i] = (byte)i;
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class FixedKeyExporter : IKeyExporter
{
    public bool TryExport(string label, ReadOnlySpan<byte> context, Span<byte> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte)(i + 1);
        }

        return label == "EXTRACTOR-dtls_srtp" && context.IsEmpty;
    }
}

internal sealed class SmokeNoOpPacketProtector : IPacketProtector
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

internal sealed class SmokePacketProtectorFactoryProvider : IWebRtcAudioPacketProtectorFactoryProvider
{
    public IPacketProtectorFactory Create(SrtpProtectionMaterial material)
    {
        return new SmokePacketProtectorFactory();
    }
}

internal sealed class SmokePacketProtectorFactory : IPacketProtectorFactory
{
    public IPacketProtector Create(PacketProtectionPurpose purpose, PacketDirection direction, uint ssrc)
    {
        return new SmokeNoOpPacketProtector();
    }
}

internal sealed class SmokeSecureHandshake : ISecureHandshake
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
            LocalRole = DtlsRole.Client,
            NegotiatedSrtpProfile = SrtpProtectionProfile.Aes128CmHmacSha1_80,
            PeerProof = new PeerProofMaterial { CertificateDer = new byte[] { 0x01, 0x02, 0x03 } },
            KeyExporter = new FixedKeyExporter()
        });
    }
}

internal sealed class SmokeSrtpKeySchedule : ISrtpKeySchedule
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

internal sealed class SmokePeerIdentityVerifier : IPeerIdentityVerifier
{
    public PeerIdentityVerificationResult Verify(PeerProofMaterial proof, ExpectedPeerIdentity expected)
    {
        return new PeerIdentityVerificationResult { IsVerified = true };
    }
}

internal sealed class SmokeRealtimeDecoder(AudioFormat outputFormat) : IRealtimeAudioDecoder
{
    public AudioFormat OutputFormat { get; } = outputFormat;

    public int DecodeCount { get; private set; }

    public AudioCodecStatus Decode(in AudioDecodeInputView input, IAudioFrameViewSink sink)
    {
        DecodeCount++;
        Span<byte> pcm = stackalloc byte[320];
        return sink.TryWrite(new AudioFrameView(pcm, OutputFormat, 160))
            ? AudioCodecStatus.Success
            : AudioCodecStatus.SinkBackpressure;
    }
}

internal sealed class SmokeRealtimeEncoder(AudioFormat inputFormat, EncodedAudioFormat outputFormat) : IRealtimeAudioEncoder
{
    public AudioFormat InputFormat { get; } = inputFormat;

    public EncodedAudioFormat OutputFormat { get; } = outputFormat;

    public int EncodeCount { get; private set; }

    public AudioCodecStatus Encode(in AudioFrameView frame, IEncodedAudioFrameViewSink sink)
    {
        EncodeCount++;
        ReadOnlySpan<byte> payload = [0x7F, 0x80, 0x81];
        return sink.TryWrite(new EncodedAudioFrameView(OutputFormat, payload, frame.Duration))
            ? AudioCodecStatus.Success
            : AudioCodecStatus.SinkBackpressure;
    }
}

internal sealed class SmokeFrameSink : IAudioFrameViewSink
{
    public int FrameCount { get; private set; }

    public bool TryWrite(in AudioFrameView frame)
    {
        FrameCount++;
        return frame.Format.SampleFormat == AudioSampleFormat.Pcm16 && frame.Data.Length > 0;
    }
}

internal sealed class SmokeProtectedPacketSink : IWebRtcProtectedPacketSink
{
    private readonly byte[] lastPacket = new byte[64];

    public int PacketCount { get; private set; }

    public ReadOnlySpan<byte> LastPacket => lastPacket.AsSpan(0, LastPacketLength);

    public int LastPacketLength { get; private set; }

    public bool TryWrite(ReadOnlySpan<byte> packet)
    {
        PacketCount++;
        LastPacketLength = packet.Length;
        packet.CopyTo(lastPacket);
        return true;
    }
}

internal sealed class InMemoryDatagramPath : IDatagramPath, IDisposable
{
    private readonly Queue<byte[]> receives = new();

    public List<byte[]> SentDatagrams { get; } = [];

    public IPEndPoint LocalEndPoint { get; } = new(IPAddress.Loopback, 50000);

    public IPEndPoint RemoteEndPoint { get; } = new(IPAddress.Loopback, 50001);

    public PathState State => PathState.Ready;

    public void EnqueueReceive(ReadOnlyMemory<byte> datagram)
    {
        receives.Enqueue(datagram.ToArray());
    }

    public ValueTask<PathStateChange?> ReadStateChangeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<PathStateChange?>((PathStateChange?)null);
    }

    public ValueTask<DatagramReceiveResult> ReceiveAsync(
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (receives.Count == 0)
        {
            return new ValueTask<DatagramReceiveResult>(new DatagramReceiveResult { HasDatagram = false });
        }

        byte[] datagram = receives.Dequeue();
        datagram.CopyTo(destination);
        return new ValueTask<DatagramReceiveResult>(new DatagramReceiveResult
        {
            HasDatagram = true,
            BytesWritten = datagram.Length,
            LocalEndPoint = LocalEndPoint,
            RemoteEndPoint = RemoteEndPoint,
            ReceivedAt = DateTimeOffset.UtcNow,
            Hint = DatagramProtocolHint.SrtpOrSrtcp
        });
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SentDatagrams.Add(payload.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose()
    {
    }
}
