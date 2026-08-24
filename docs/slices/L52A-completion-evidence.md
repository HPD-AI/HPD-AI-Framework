# L52A completion evidence

Date: 2026-08-24  
Integration branch: `codex/l52a-unified-studio-integration`  
Donor: `6af21b0e`  
Base authority: current main through L53

## Disposition

L52A is a forward integration, not a donor merge. Current Base, SQLite, lifecycle,
transactions, receipts, search, scheduling, semantic activation, Graph, and generated-client
authority remain intact. Unified Studio, selected Audio, and selected Payments work was
reapplied onto that authority. The path-by-path record is in
`L52A-overlap-disposition-ledger.md`.

The final browser smoke exposed and closed three integration gaps:

- Studio runtime routes now resolve from the current Studio directory and therefore retain
  a host-owned prefix such as `/platform/studio/` on direct and client-side navigation.
- the current L53 TypeScript Base codec now recognizes the donor's closed Studio formats
  (`sha256`, NFC text/search/summary, safe error codes, opaque cursors, and resource tokens)
  without relaxing any existing Base format;
- the edition shell consumes the Base module's source-owned page bindings, removing the
  duplicate generic Base page component while keeping independently loaded module assets as
  the authorization-neutral activation/ABI contribution.

The Base module asset is content-versioned as
`base/6c6d65420bde4c64faaaea95ec0a74b36c719d66c0edd2eec1aa7d7f461873e5.js`.
The final shell digests are:

- JavaScript: `d2c400ad6e999338ead82a1f84e0f229cfe9d5a886b0d9b05a243c157e603e5e`
- CSS: `90c4ebfe65e55bb8f1ba24479aa7b8018c3468d0502c9b5431c4803064aa9017`

## Matrix

The following passed on this integration branch:

| Surface | Result |
| --- | --- |
| Base | 1043 tests |
| Base Studio | 47 tests |
| Base SQLite | 281 tests |
| TypeScript Base client | 33 tests, including Studio format interop |
| Studio Core | 66 tests plus typecheck |
| Unified shell | 50 tests, typecheck, build |
| Base Studio TypeScript module | typecheck and build |
| Gateway Studio .NET / TypeScript | 6 / 76 tests plus typecheck and build |
| Graph | 1014 tests |
| Graph Studio TypeScript | typecheck and build |
| Silero Audio focus | 15 tests on net8.0, net9.0, and net10.0 |
| Payments | Release solution build and locked restore |
| HPD Cloud Gateway / Backend | 10 / 33 tests and full solution build |

The complete earlier matrix remains recorded by the reviewable commits; the final changed
surfaces were rerun after the browser fixes: Base Studio 47/47, TypeScript Base client 33/33,
shell 50/50 with zero typecheck diagnostics, and HPD Cloud Backend Release build with zero
warnings or errors.

## HPD Cloud smoke

HPD Cloud was rebuilt from its working integration tree and served at
`http://localhost:5087/platform/studio/` with strict self-only script/style/connect CSP.
A fresh browser session proved:

- authenticated session generation 1 and current bootstrap;
- Overview populated all three registered views;
- client-side navigation to Data populated the Base module and Cloud collections without a
  loading-screen replacement or page reload;
- the dedicated semantic-activation component rendered the bounded L53 inspection state;
- Infrastructure rendered current store/schema evidence and intentional empty backup,
  maintenance, and attention sections;
- Diagnostics rendered current accounting evidence and intentional empty incident/health
  sections;
- no browser console errors after navigation;
- served shell and module URLs used their current content digests.

Cloud owns unified Studio in the Backend. Gateway proxies `/platform/**` and does not register
a competing Studio graph. Current L53 Base control capabilities are mapped explicitly by the
Backend host.

## Removal/reference proof

- `find dotnet -name ExecutionManager.cs` returns no result; the main deletion remains final.
- the removed shell-local `BaseRegisteredPage.svelte` was not retained as a compatibility
  fallback; Base presentation comes from the unified Base Studio module source.
- remaining `*.Studio` projects are active unified module contributions (Base, Graph, Agent,
  Auth, RAG, ML), not the superseded separate host architecture. Gateway's obsolete separate
  Studio host was not resurrected.
- generated bundles and package locks were regenerated from source/manifests rather than
  manually merged.

L52A is complete with this evidence and its final fixes committed together with the prior
reviewable forward-port commits.
