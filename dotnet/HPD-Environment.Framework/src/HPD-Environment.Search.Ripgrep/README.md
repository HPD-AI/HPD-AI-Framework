# HPD Environment Search Ripgrep

This package owns the optional verified ripgrep binaries used by HPD
Environment workspace search.

The Agent coding harness never resolves or launches `rg` directly. It asks the
workspace file store to search. The selected Environment provider supplies the
process and filesystem boundary, while this package provisions the target
executable through that same process boundary.

## Bundled Targets

- macOS arm64
- macOS x64
- Linux x64, statically linked musl build
- Linux arm64 musl, including the pinned loader and runtime libraries

Bundles are generated during packaging from pinned upstream releases. The
manifest records the upstream version and SHA-256 digest for every bundle.
Private package builds must run:

```bash
dotnet run --file pack/prepare-ripgrep-binaries.cs -- \
  --output-root obj/RipgrepPack
```

The runtime writes the selected bundle into a random, owner-only temporary
directory inside the execution unit. It invokes the resulting absolute path
through the same `IProcessProvider` used for workspace commands. It never
trusts a workspace or host `PATH` entry when a bundle is available.

Search continues to use the managed fallback when the target has no bundle,
the execution target cannot run POSIX shell tools, or bundle provisioning
fails. The fallback preserves Grep behavior but does not provide ripgrep
acceleration.
