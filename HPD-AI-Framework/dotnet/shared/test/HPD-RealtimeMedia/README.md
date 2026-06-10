# HPD-RealtimeMedia Tests

Realtime media test projects live here. The first validation suites should track
the contract proposal's pre-freeze gates:

```text
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
HPD.Media.Diagnostics.Tests.Telemetry
HPD.Media.Architecture.Tests
HPD.Audio.Codecs.Tests.Allocation
HPD.Audio.Pump.Tests.Allocation
HPD.Media.WebRTC.Tests.AotSmoke
HPD.Media.AotSmoke.App
```

Build and test output should filter warning lines when possible so failures
remain easy to read during implementation.

The AOT smoke app is intentionally separate from xUnit. Use it when validating
Native AOT publishability:

```bash
dotnet publish './HPD-AI-Framework/dotnet/shared/test/HPD-RealtimeMedia/HPD.Media.AotSmoke.App/HPD.Media.AotSmoke.App.csproj' --framework net10.0 -c Release -r osx-arm64 /p:PublishAot=true --nologo -m:1 2>&1 | awk 'tolower($0) !~ /warning/ { print }'; exit ${pipestatus[1]}
```
