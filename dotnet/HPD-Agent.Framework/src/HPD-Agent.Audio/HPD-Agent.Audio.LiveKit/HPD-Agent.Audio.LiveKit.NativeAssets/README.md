# HPD Agent Audio LiveKit native assets

This package contains the LiveKit FFI native artifact whose exact bytes were
executed and accepted for `osx-arm64`, plus the marker assembly required to make
the runtime asset a valid NuGet dependency. Other platform artifacts are not
included and are not advertised.

The package build fails unless the input dylib SHA-256 is exactly
`27782cdca31ffb3d99fd4c58b602aa72574f71d579a00a70a2376d05253ca865`.
The managed `HPD-Agent.Audio.LiveKit` package performs the same verification
before native runtime admission.

Install `HPD-Agent.Audio.LiveKit` for the managed transport. This package is a
runtime dependency and is not intended to be used directly by applications.
