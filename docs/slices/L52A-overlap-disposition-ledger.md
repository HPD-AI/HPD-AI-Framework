# L52A overlap disposition ledger

Baselines:

- branch point: `dcd6350b`
- current-main authority: `660ae3d0`
- selective Studio donor: `6af21b0e`

The overlap set is the sorted intersection of paths changed by `dcd6350b..660ae3d0` and `dcd6350b..6af21b0e`. It contains 119 paths. The rules below are disjoint and exhaustive, so every overlapping path has exactly one disposition.

| Matching overlapping path | Count | Disposition | Reason |
| --- | ---: | --- | --- |
| `dotnet/HPD-Base.Framework/**` | 106 | `main` | L47–L53 BASE, provider, SQLite, lifecycle, search, scheduling, activation, semantic-activation, and conformance authority remains current main. Studio projections are additive files outside this overlap set. |
| `dotnet/HPD-Graph.Framework/src/HPD-Graph.Hosting/Lifecycle/ExecutionManager.cs` | 1 | `main` | Main's deletion wins. The obsolete execution manager must not be resurrected. |
| `typescript/hpd-base-client-generator/test/generator.test.mjs` | 1 | `main` | Current L53 generated-client contract authority wins. |
| `typescript/hpd-base-client/test/client.test.mjs` | 1 | `main` | Current L53 client semantics and codecs win. |
| `dotnet/HPD-Agent.Framework/src/HPD-Agent.Audio.LiveKit/**` and `dotnet/HPD-Agent.Framework/test/HPD-Agent.Audio.LiveKit.SourceGenerator.Tests/LiveKitRuntimeB4Tests.cs` | 3 | `main` | Current main's later provider-neutral S2/voice-activity architecture supersedes the donor's removed live-session runtime. Reapplying those files would restore the obsolete authority path. |
| `typescript/hpd-ai-studio/modules/hpd-base-studio/package.json` | 1 | `Studio addition reapplied` | Unified module package surface wins while its BASE client authority remains current main. |
| `typescript/hpd-ai-studio/modules/hpd-base-studio/src/index.ts` and `src/module.ts` | 2 | `Studio addition reapplied` | Unified module activation and exact page bindings win, adapted to current L53. |
| `typescript/hpd-ai-studio/shell/package.json`, `src/main.ts`, and `src/studio/composition.test.ts` | 3 | `Studio addition reapplied` | Unified shell source and composition tests win. |
| `typescript/hpd-ai-studio/shell/package-lock.json` | 1 | `generated` | Regenerated from the resolved manifest; neither historical lock is manually merged. |

Totals: `main` 112, `Studio addition reapplied` 6, `generated` 1, total 119.

## Reproduction and completeness check

```sh
main_paths="$(mktemp)"
studio_paths="$(mktemp)"
git diff --name-only dcd6350b..660ae3d0 | LC_ALL=C sort > "$main_paths"
git diff --name-only dcd6350b..6af21b0e | LC_ALL=C sort > "$studio_paths"
comm -12 "$main_paths" "$studio_paths"
```

The output must contain exactly 119 paths. Apply the table from top to bottom; the match count must be 119 with no unmatched or multiply matched path.

Files added only by the donor are not overlap entries. Their authority is governed by the L52A ownership map and their individual forward-port commits. Generated bundles, embedded manifests, and other locks are regenerated after authoritative source resolution even when they are outside the overlap intersection.
