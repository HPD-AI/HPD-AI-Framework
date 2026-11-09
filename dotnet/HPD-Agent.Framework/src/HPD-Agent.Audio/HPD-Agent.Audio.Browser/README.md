# HPD Agent Audio Browser Qualification

This directory contains the private browser Web Audio transport used to qualify
bounded playout, generation fencing, virtual capture tracks, and lifecycle
transitions.

It is not currently published as an npm package or included in the
`HPD-Agent.Audio` NuGet package. Applications should provide their own browser
transport and implement the HPD `IAudioOutputSink`/input-source contracts until
this artifact is promoted to a supported package.

## Run

```bash
npm ci
npm test
```

The test suite uses Playwright Chromium and a virtual Web Audio destination. It
does not access a microphone or physical speaker.
