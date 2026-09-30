# HPD Agent Audio LiveKit native assets

This package contains only the LiveKit FFI native artifact whose exact bytes
were executed and accepted for `osx-arm64`. Other platform artifacts are not
included and are not advertised.

The package build fails unless the input dylib SHA-256 is exactly
`cf034115fb3b94b5682151d2d36cb5ea351e97b881cd5ac0b97d0873b2a2b1da`.
The managed `HPD-Agent.Audio.LiveKit` package performs the same verification
before native runtime admission.
