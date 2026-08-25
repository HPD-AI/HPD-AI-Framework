# L52A Audio donor disposition

The selected Audio commits were reviewed oldest-to-newest against current main rather than cherry-picked. Their live-session runtime (`AudioSessionInputRuntime`, `AudioTransportRuntimeBridge`, `ProductionAudioSessionLifecycleBackend`, endpoint evidence normalization, and related tests) no longer exists on main. Current main replaces it with the provider-neutral S2 media authority and the deterministic voice-activity composition stack.

| Donor commit | Disposition | Current-main authority |
| --- | --- | --- |
| `cb9cde49` provider-neutral live barge-in | Excluded as superseded | S2 media ownership/residence/work plus deterministic voice-activity promotion, lifecycle, observation, and status projection. |
| `535511be` production managed VAD turns | Excluded as superseded | Typed voice-activity source providers, compiled plans, supervised source graph streams, readiness coordination, and composition host. |
| `a62e53c4` exact prepared VAD graph owner | Excluded as superseded | Graph/media authority vectors and S2 residence ownership bind the exact graph generation. |
| `f235654e` host lifecycle failure diagnostics | Excluded as superseded | Voice-activity health, warnings, observation, status projection, and HPD event diagnostics. |
| `6168ea7f` retain lifecycle observer | Excluded as superseded | Lifecycle and observation are explicit retained authorities in the current composition host instead of mutable attachment options. |
| `54f519ad` preserve provider preparation failure | Excluded as superseded | Readiness coordination distinguishes participant start, admission, retryable, and cleanup failure results. |
| `dcc673e1` report managed VAD frame failures | Excluded as superseded | Source outcomes include faulted/unobservable/discontinuous states that flow into promotion facts, health, warnings, and diagnostics. |

Attempting the donor series directly produces modify/delete conflicts precisely on the removed runtime types above. Restoring them would create a parallel Audio authority, contradict the pre-1.0 hard break, and regress current main. No donor file from these commits is therefore restored.

The separate Silero builder convenience added by L52A targets the current typed provider configuration and does not revive the donor runtime.

## Post-L52A retained streaming STT disposition

V9 continued after the selected Studio donor with six Audio commits:

| Donor commit | Disposition | Integration rule |
| --- | --- | --- |
| `437a037c` retained ElevenLabs STT participant | Already forward-ported | L52A commit `4eaa597e` carries the resulting provider contract, protocol, socket, participant, and tests. Keep the L52A versions. |
| `0765c2c2` retained streaming STT transport foundation | Selectively forward-ported | The provider-neutral participant configuration, contribution-safety declaration, operation-identified updates, fingerprints, and closed outcomes were carried. V9 readiness, endpoint-plan, fanout, and deleted managed-session types remain excluded because current S2 owns those concerns. |
| `6d639b6a` transcript observation normalization | Forward-ported and adapted | `StreamingTranscriptObservationNormalizerV1` now lowers provider observations directly into the current closed `TranscriptObservationV1` union and current `ExpectedAuthorityVectorV1`; obsolete transport-readiness and settled-range wrappers were removed. |
| `1f7944e4` persistent managed streaming STT endpoint | Excluded as superseded | The implementation depends on deleted process-local lifecycle, endpoint-reducer, Agent-input, and semantic-handoff authority. Current S2 work execution, current endpoint observations, and current Agent dispatch remain authoritative. |
| `6c9712f7` retained streaming STT lifecycle completion | Excluded as superseded | Its cleanup, telemetry, and required-branch implementation is inseparable from the deleted managed-session host. Current retained S2 work/cleanup authority is preserved; no parallel host was restored. |
| `dfcf2882` OpenAI retained streaming transcription | Forward-ported and adapted | The OpenAI participant, bounded socket, options, provider/client contribution, capability metadata, and regression tests were carried. The donor README example was not carried because it names the superseded managed-input configuration surface. |

The ten paths changed by L52A's Silero and ElevenLabs commits are compatible at the selected donor point, but they are not all byte-identical to the V9 tip. V9 later changed eight of them as part of its retained-STT lifecycle and older runtime composition. Only the ElevenLabs protocol and socket remain unchanged. In particular, V9 extends the shared participant factory with configuration and contribution-safety declarations, adds update operation identity and fingerprints, and expands update outcomes. Those contract changes are design input that must be reconciled with current S2 authority rather than copied automatically. L52A's Silero builder convenience remains authoritative even though V9 does not contain it.

Therefore the integration is not a V9 merge, a six-commit cherry-pick, or reconstruction of the old managed-audio foundation. It is a semantic forward-port of retained streaming STT onto the current S2 media, voice-activity, Agent dispatch, observation, and cleanup authorities.

## Required implementation sequence

1. Start from `codex/l52a-unified-studio-integration` in an isolated worktree and prove the untouched Agent and Audio baseline.
2. Map each retained-STT requirement to its current S2, voice-activity, Agent dispatch, observation, and cleanup owner. Any requirement without a current owner is a design gap, not permission to restore a deleted V9 runtime type.
3. Introduce the smallest provider-neutral streaming-STT contract and transcript normalization required by both ElevenLabs and OpenAI.
4. Integrate persistent streaming input, endpoint evidence, cancellation, cleanup, and telemetry through current authorities in reviewable commits.
5. Keep the existing L52A ElevenLabs implementation, adapting it only to the finalized shared contract.
6. Port the OpenAI realtime participant and transport after the shared lifecycle passes its tests.
7. Regenerate only artifacts affected by the resolved source authority and run the complete acceptance suite.

## Prohibited imports

- No merge commit from `codex/unified-runasync-v9`.
- No wholesale replacement of `dotnet/HPD-Agent.Framework/**`.
- No restoration of deleted managed-session, endpoint, lifecycle, Graph, Base, or semantic-handoff authority.
- No V9 changes under Base, Payments, Graph, Gateway, or Studio as a side effect of Audio integration.
- No compatibility layer whose only purpose is to preserve a superseded V9 runtime shape.

## Verification gates

- Untouched L52A Agent and Audio baseline builds before the first source port.
- Focused tests pass after every contract, lifecycle, provider, and cleanup layer.
- Silero, ElevenLabs, OpenAI, LiveKit, Audio V2, core Agent, serialization/AOT, and current Graph integration tests pass in the final tree.
- A final path audit proves that the Audio series changed no Base, Payments, Graph, Gateway, or Studio authority.

## Completion evidence

The retained-provider forward-port is commit `cb558c8e` on
`codex/l52a-v9-audio-forward-port`.

- Release compilation succeeds for the Audio V2 closure on .NET 8, .NET 9, and .NET 10 with zero errors.
- OpenAI retained-STT, ElevenLabs retained-STT, and transcript-normalization focus: 33 passed and two credentialed live tests skipped per target framework.
- Complete Audio V2 suite excluding the external Silero model fixture: 369 passed and three credentialed live tests skipped per target framework.
- The ten Silero model tests remain an environmental baseline prerequisite and require `eng/fetch-silero-vad-v6.2.sh` or `HPD_SILERO_VAD_MODEL_PATH`; no failure was introduced by this forward-port.
- The implementation changes only `dotnet/HPD-Agent.Framework/**`; it changes no Base, Payments, Graph, Gateway, or Studio path.
- Added-line audit contains none of `AudioSessionInputRuntime`, `AudioTransportRuntimeBridge`, `ProductionAudioSessionLifecycleBackend`, or `ExecutionManager`.

This completes the V9 retained-STT disposition. Provider transports and current-authority normalization are retained; the obsolete process-local lifecycle is rejected rather than recreated.
