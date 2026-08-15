# Voice activity composition example

This executable runs the production `VoiceActivityPlanCompilerV1`,
`VoiceActivityCompositionHostV1` and sole promoter with deterministic source
descriptors. It performs no provider discovery and contains no vendor-specific
branch.

Run every operational shape:

```bash
dotnet run --project HPD-Agent.Audio.VoiceActivity.CompositionExample.csproj -- all
```

Or run one of `microphone`, `webrtc`, `telephony`, `provider`, `split`,
`fusion`, `manual`, `finite`, `provider-unknown`, or
`provider-not-observable`. Output is a stable, redacted requested/effective
support summary suitable for troubleshooting; it contains no audio, score,
provider payload, session ID or tenant ID.
