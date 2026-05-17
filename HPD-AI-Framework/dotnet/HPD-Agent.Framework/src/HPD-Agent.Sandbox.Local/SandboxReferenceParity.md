# Local Sandbox Reference Parity

This checklist tracks parity against the TypeScript sandbox runtime reference and
the second-mover C# implementation plan.

## Status

- [x] Explicit policy model for filesystem and network behavior
- [x] Host canonicalization and domain wildcard matching
- [x] Explicit `SandboxNetworkMode`; no nullable `AllowedDomains` compatibility path
- [x] HTTP proxy filtering through `NetworkPolicyEvaluator`
- [x] HTTP proxy strips hop-by-hop headers before upstream forwarding
- [x] SOCKS5 proxy filtering through `NetworkPolicyEvaluator`
- [x] SOCKS malformed host denial before policy matching
- [x] SOCKS unsupported commands and address types return RFC-specific failures
- [x] Parent proxy resolution and forwarding
- [x] Request filter hook for plain HTTP traffic
- [x] In-process TLS termination with ephemeral CA support
- [x] Leaf certificate mint/cache
- [x] Trust environment variable injection
- [x] Optional external MITM Unix socket routing
- [x] macOS seatbelt profile generation for read/write/network policy
- [x] macOS Mach lookup allow rules and trustd weaker-isolation gate
- [x] macOS violation parsing/store integration
- [x] Linux bubblewrap mount-plan emitter
- [x] Linux `denyRead` plus `allowRead` ordering
- [x] Linux protection for non-existent denied paths
- [x] Linux symlink replacement protection
- [x] Linux bwrap mount point cleanup
- [x] Linux filtered network through Unix socket bridges
- [x] Linux seccomp helper for Unix socket creation denial
- [x] Linux `AllowAllUnixSockets` escape hatch is integration-tested conditionally
- [x] Packaged-first seccomp helper resolution
- [x] Runtime seccomp helper compilation is opt-in only
- [x] Structured dependency issues for missing platform dependencies
- [x] `AgentBuilder.WithSandbox(...)` enables/replaces the global sandbox middleware
- [x] `[Sandboxable]` maps function-local declarations into sparse `SandboxConfigOverride`
- [x] Source-generated `[Sandboxable(...)]` metadata resolves into per-invocation sandbox overrides
- [x] Runtime capability registry exposes middleware-owned sandbox capabilities to tools
- [x] Runtime capability registry seals after runtime startup to prevent accidental late mutation
- [x] `ISandboxedProcessRunner` is the process execution boundary for new tools
- [x] Sandbox middleware publishes the process runner instead of rewriting command arguments
- [x] Sandbox middleware stops the runtime session in `BeforeStopAsync`
- [x] Generated sandbox metadata is exposed through `FunctionExecutionContext`
- [x] Default sandbox policy resolver merges global, function, and per-call config
- [x] Local process runner keys sandbox managers by infrastructure policy to avoid first-config-wins reuse
- [x] Local process runner supports cwd, environment, stdin, stdout/stderr capture, stderr merge, output limits, timeout, and cancellation
- [x] Local process runner tracks active processes and kills them during runner/session disposal
- [x] Local process runner emits process lifecycle events from the spawn boundary
- [x] Proxy and platform sandbox violations are collected into the manager violation store
- [x] Process results include sandbox violations recorded during the process run
- [x] Middleware emits sandbox events
- [x] MCP wrapping uses structured command invocation APIs

## Platform Integration Coverage

- [x] Basic sandboxed command execution is integration-tested conditionally
- [x] Linux blocked network egress is integration-tested conditionally
- [x] Linux seccomp Unix socket denial is integration-tested conditionally
- [x] Linux PID isolation is integration-tested conditionally
- [x] macOS blocked sensitive path access is integration-tested conditionally
- [x] macOS filtered network direct-bypass denial has a dedicated integration test
- [x] macOS filtered network proxy allowed/denied paths are integration-tested conditionally
- [x] Linux filtered network proxy-only egress has a dedicated integration test

## Packaging And CI

- [x] Linux package builds invoke the seccomp helper build script
- [x] x64 helper is packed under `runtimes/linux-x64/native/`
- [x] arm64 helper is packed under `runtimes/linux-arm64/native/`
- [x] CI installs Linux native helper compilers
- [x] CI artifact validation inspects the produced `.nupkg` for seccomp helpers

## Remaining Follow-Ups

1. Keep this checklist updated when platform behavior changes.
