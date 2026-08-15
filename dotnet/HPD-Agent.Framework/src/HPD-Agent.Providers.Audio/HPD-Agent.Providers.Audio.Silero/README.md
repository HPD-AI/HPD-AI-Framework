# HPD-Agent.Providers.Audio.Silero

Digest-pinned Silero v6.2 voice activity source provider for HPD Agent Audio.

## Install

```bash
dotnet add package HPD-Agent.Providers.Audio.Silero
```

## Use When

Use this package for local 8 kHz or 16 kHz streaming Voice Activity inference.
The package never downloads a model while constructing a session. Provision the
official MIT-licensed v6.2 ONNX artifact explicitly, verify its SHA-256, and pass
its local path through `SileroVadOptions`.

The qualified ONNX Runtime 1.23.0 CPU assets cover `linux-arm64`, `linux-x64`,
`osx-arm64`, `osx-x64`, `win-arm64`, and `win-x64`. The provider validates and
prewarms the model before returning a source. Every audio session receives
isolated recurrent state while the immutable inference host is shared.

For repository qualification only, run `eng/fetch-silero-vad-v6.2.sh`. It pins
upstream commit `be95df9152c0d7618fa1edfeb296fc3dae32376f` and SHA-256
`1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3`.
