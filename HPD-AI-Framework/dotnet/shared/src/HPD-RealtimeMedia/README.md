# HPD-RealtimeMedia

HPD-RealtimeMedia is the shared realtime media infrastructure area for HPD.
It contains the low-level contracts and implementations for allocation-conscious
audio, codec, datagram, RTP, RTCP, SRTP, SDP, and WebRTC infrastructure.
The WebRTC layer includes manual signaling/SDP/ICE helpers plus STUN binding
support for server-reflexive candidate gathering.

This area is intentionally separate from `HPD.Agent.Audio`. Agent audio owns
agent-facing policy, provider integration, VAD, STT, TTS, turn-taking, and
middleware. HPD-RealtimeMedia owns reusable media movement and protocol layers.

## Initial Package Map

Source projects:

```text
dotnet/shared/src/HPD-RealtimeMedia/src/
  HPD.Buffers
  HPD.Audio.Primitives
  HPD.Audio.Connections
  HPD.Audio.Codecs
  HPD.Audio.Codecs.G711
  HPD.Audio.Codecs.Opus
  HPD.Audio.WebRTC
  HPD.Media.Transport
  HPD.Media.Rtp
  HPD.Media.Rtp.Audio
  HPD.Media.Rtp.Audio.Sdp
  HPD.Media.Rtcp
  HPD.Media.Rtcp.Feedback
  HPD.Media.Rtcp.Twcc
  HPD.Media.Rtp.Repair
  HPD.Media.Diagnostics
  HPD.Media.Sdp
  HPD.Media.Srtp
  HPD.Media.WebRTC
```

Test projects:

```text
dotnet/shared/test/HPD-RealtimeMedia/
  HPD.Buffers.Tests.Ownership
  HPD.Audio.Codecs.Tests.Allocation
  HPD.Audio.Pump.Tests.Allocation
  HPD.Audio.WebRTC.Tests.Pipeline
  HPD.Media.Diagnostics.Tests.Telemetry
  HPD.Media.Rtp.Tests.Vectors
  HPD.Media.Rtp.Audio.Tests.Vectors
  HPD.Media.Rtp.Audio.Sdp.Tests.Vectors
  HPD.Media.Rtp.Repair.Tests.Vectors
  HPD.Media.Rtcp.Tests.Vectors
  HPD.Media.Rtcp.Feedback.Tests.Vectors
  HPD.Media.Rtcp.Twcc.Tests.Vectors
  HPD.Media.Sdp.Tests.Vectors
  HPD.Media.Srtp.Tests.Vectors
  HPD.Media.Transport.Tests.Datagrams
  HPD.Media.WebRTC.Tests.AotSmoke
  HPD.Media.AotSmoke.App
```

## Contract Rules

- Media hot paths use span, memory, caller-provided buffers, and sink APIs.
- Retained pooled memory crossing async, queue, reorder, or fanout boundaries
  carries explicit ownership.
- `HPD.Events.Struct` is the realtime telemetry lane.
- Semantic `HPD.Events` is for lifecycle, integration, diagnostics summaries,
  replay, and tests, not packet or frame movement.
- Public contracts must remain Native AOT-compatible.

## Native AOT Smoke

`HPD.Media.AotSmoke.App` is a publishable console smoke artifact that exercises
the AOT-sensitive WebRTC/media surfaces without xUnit or reflection-based test
infrastructure. It covers WebRTC signaling JSON, SDP parse/write and typed
negotiation, SDP-to-RTP-audio payload mapping, STUN binding parse/write,
RTP/RTCP parse/write, SRTP protect/unprotect, DTLS-SRTP key schedule
derivation, secure WebRTC audio context setup, WebRTC audio SDP/ICE setup, and
inbound/outbound WebRTC RTP-audio pumps.

Warning-filtered local macOS ARM64 validation:

```bash
dotnet publish './HPD-AI-Framework/dotnet/shared/test/HPD-RealtimeMedia/HPD.Media.AotSmoke.App/HPD.Media.AotSmoke.App.csproj' --framework net10.0 -c Release -r osx-arm64 /p:PublishAot=true --nologo -m:1 2>&1 | awk 'tolower($0) !~ /warning/ { print }'; exit ${pipestatus[1]}
```
