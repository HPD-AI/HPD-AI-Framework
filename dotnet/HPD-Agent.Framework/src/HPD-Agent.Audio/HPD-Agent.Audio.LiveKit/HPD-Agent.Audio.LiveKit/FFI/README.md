# LiveKit FFI reviewed inputs

These three `AdditionalFiles` are the accepted B2 input boundary for
`livekit-ffi/v0.12.60`:

- `livekit-ffi-protocol-v0.12.60.txt` is the reviewed projection of the exact
  13-file official descriptor set.
- `livekit-ffi-native-v0.12.60.txt` is the reviewed native header, export,
  response/callback memory, handle-release and RID-artifact inventory.
- `livekit-ffi-binding-v0.12.60.txt` is HPD's admitted mechanical Audio surface.

`HPD-Agent.Audio.LiveKit.SourceGenerator` consumes all three atomically. It is
inert with no inputs only for an unrelated analyzer consumer. The product shell
sets `HpdLiveKitFfiManifestRequired=true`, so it requires exactly one of each
and fails closed if all three disappear.

The generator cross-binds:

- tag, commit and descriptor digest;
- native-header digest and exact `nuint` ABI widths;
- immediate response copying/release and borrowed callback copying/bounds;
- the four required exports;
- all six artifact hashes, with only `osx-arm64` execution-qualified and
  advertised;
- each admitted request/response/completion/correlation tuple;
- bounded admission and cancellation-after-issue for every async operation;
- every operation- and event-carried native handle to explicit release;
- panic and unknown-event quarantine;
- the local-source-queue-only proof boundary for `ClearAudioBuffer`.

The test project independently rebuilds the official descriptor set from the
pinned mechanical protocol assembly and compares its digest and admitted cases,
then compares every protocol-source and native-artifact hash to the accepted B1
locks. This prevents three mutually consistent but fabricated input files from
satisfying the gate.

The operation row is:

```text
operation|name|request|response|completion-or-none|correlation-or-none|handles-or-none|async-or-sync|admission|cancellation|release-or-proof-boundary
```

The event row is:

```text
observe-event|case|disposition|handles-or-none|release-or-none
```

B3 generates the closed request, response, event, correlation, routing, handle,
ABI, export and artifact inventories from this exact admitted boundary. It also
routes completion projections into the handwritten bounded
`LiveKitIssuedOperationRegistry`; policy such as retries, deadlines and session
lifetime remains outside generated code.

The four `LibraryImport` declarations use a two-stage generated artifact at
`Generated/LiveKitFfiNative.g.cs`. This is required because Roslyn generators
do not consume source emitted by another generator in the same compilation, so
the SDK `LibraryImport` generator cannot see a declaration emitted by the
LiveKit generator. The checked generated file is compiled as ordinary input;
the LiveKit generator validates its exact symbols, entry points, Cdecl callback
ABI, `nuint` widths and I1 bool return before emitting protocol code. Tests pin
its exact bytes. There is no `DllImport`, runtime export lookup or compatibility
fallback.

B3 cannot add an operation, event, native export, handle inference, RID, retry,
timeout, reconciliation or readiness rule through attributes or handwritten
fallback. B4 remains responsible for the process-global native host, copied
callback admission, media ownership and session runtime.

