# Apple Virtualization Provider Handoff

This document is the working handoff for developers taking over the Apple Virtualization execution provider.

The short version: this provider boots a Linux VM on macOS with Apple Virtualization.framework, talks to an HPD guest agent over virtio-socket, and runs the real container smoke workflow inside that VM. The VM is the isolation boundary. Docker, containerd, Podman, and BuildKit are workload engines inside the VM, not the isolation boundary and not host socket passthrough.

For first-time local setup, start with `docs/apple-virtualization/developer-setup.md`.

## Repository Areas

| Area | Path |
| --- | --- |
| Execution contracts | `src/HPD-Agent/Execution/ExecutionContracts.cs` |
| Apple VZ provider | `src/HPD-Execution/HPD-Execution.AppleVirtualization/` |
| Apple VZ DevKit | `src/HPD-Execution/HPD-Execution.AppleVirtualization.DevKit/` |
| Apple VZ CLI | `src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/` |
| Swift helper | `src/HPD-Execution/hpd-vz/` |
| Guest agent payload | `src/HPD-Execution/hpd-guest-agent/` |
| Apple VZ tests | `test/HPD-Execution/HPD-Execution.AppleVirtualization.Tests/` |
| Guest image docs/scripts | `docs/apple-virtualization/` |

## Current Capability

The real acceptance path currently proves:

- Apple Virtualization boots an Ubuntu 24.04 arm64 guest on macOS.
- The guest exposes `hpd-guest-agent` over virtio-socket port `7777`.
- `hpd-vz` performs a real guest-agent hello/ready handshake.
- `hpd-vz` forwards process start/wait/read-output to the guest agent.
- The guest agent launches real guest processes and returns `ProcessInvocationResult`.
- The real container smoke test runs `/hpd/container-smoke` inside the guest.
- The provider, guest agent, smoke command, image-prep scripts, and real-acceptance harness model Docker through the same authority-bound pattern. The rootful prepared-image path uses `/var/run/docker.sock` and projects it to `/run/hpd/engine/docker.sock`.
- The opt-in real container acceptance harness has passed against a prepared Docker guest using `DockerCompatible`, `docker run`, and `alpine:3.20`.
- The provider and harness now model containerd as the same authority-bound engine pattern, using `/run/containerd/containerd.sock` and a projected `/run/hpd/engine/containerd.sock` socket when configured.
- The opt-in real container acceptance harness has passed against a prepared containerd guest using `ContainerdApi`, `ctr`, and `docker.io/library/alpine:3.20`.
- The provider, guest agent, smoke command, image-prep scripts, and real-acceptance harness now model Podman through the same authority-bound pattern. The rootful prepared-image path uses `/run/podman/podman.sock` and projects it to `/run/hpd/engine/podman-rootful.sock`.
- The provider, guest agent, smoke command, image-prep scripts, and real-acceptance harness now model BuildKit through the same authority-bound pattern. The rootful prepared-image path uses `/run/buildkit/buildkitd.sock` and projects it to `/run/hpd/engine/buildkitd-rootful.sock`; the smoke command performs a local scratch build through `buildctl`.
- The opt-in real container acceptance harness has passed against a prepared rootful BuildKit guest using `BuildKitApi`, `buildctl`, and a local scratch build.
- `run-real-container-acceptance-matrix.sh --keep-going` has passed against all prepared env files on this host: BuildKit, Docker, Podman, and containerd.
- `HPD-Execution.AppleVirtualization.DevKit` provides an embeddable .NET surface for prepared-image discovery, env parsing, env validation, matrix planning, image-prep command execution, real matrix execution, cleanup execution, and host platform diagnostics.
- `HPD-Execution.AppleVirtualization.Cli` provides a thin command wrapper over DevKit for host checks, env discovery/validation, image preparation, single-env runs, matrix runs, and cleanup.
- The real acceptance harness uses a per-run scratch raw disk copied from the prepared base raw disk, so failed runs do not corrupt the reusable prepared image.
- Real VM configurations now include an Apple VZ NAT network device, and `hpd-vz` owns explicit host-local TCP endpoint forwarding for `PublishedEndpoint` resources. For real Apple NAT guests, endpoint publication does not assume a host-routable guest IP; the helper can route host-local HTTP-style traffic through the bounded guest-agent TCP proxy.

## Important Invariants

- The VM is the isolation boundary.
- Do not call or mount host Docker/Podman/containerd sockets.
- Engine access must be represented through `EngineControlPlane` plus `AuthorityBinding`.
- Keep host-side hot paths bounded and Native AOT friendly.
- Preserve redaction: helper/provider payloads must not casually leak sensitive host-locus paths or secret-bearing identifiers.
- Real acceptance is explicit opt-in only. A skipped real test is not proof of real VM behavior.

## Current Authority State

Basic engine socket authority projection is now guest-agent-backed.

The provider validates an `AuthorityBinding`, `hpd-vz` forwards `AuthorityBind`, `AuthorityStatus`, and `AuthorityRevoke` to the guest agent over virtio-socket, and the guest agent creates/verifies/removes the projected socket path used by `/hpd/container-smoke`. The old `/hpd/container-smoke` process-start socket fallback has been removed.

Current coverage:

- `AuthorityStatus` distinguishes missing target, unmanaged target, wrong symlink target, and projected socket;
- `AuthorityRevoke` returns before/after socket evidence and verified revocation when the projection is absent afterward;
- bind/status/revoke emit audit events with stable correlation IDs;
- guest-agent stdio tests cover the local authority edge cases;
- provider `GetStatusAsync` actively refreshes projected bindings through `AuthorityStatus`;
- provider diagnostics now surface degraded guest authority conditions including missing source, missing target, unmanaged target, wrong target, and revoke-incomplete states;
- provider preserves bounded status/revocation evidence in `AuthorityBindingStatus.Extensions` using schema `hpd.execution.apple-virtualization.authority.evidence.v1`;
- `AppleVirtualizationAuthorityEvidenceReader` provides typed access to the authority evidence extension;
- helper golden tests cover authority revocation evidence variants and real-mode guest-agent error forwarding;
- non-fake `hpd-vz` no longer returns synthetic authority success when no guest-agent response is available;
- real Docker smoke continues to pass through the guest-agent authority path;
- provider `AuthorityBind` retries retryable helper errors, which covers guest boot races where an engine socket is not present on the first guest-agent check;
- containerd authority binding uses the same redacted helper payload shape and rootful engine authority classification;
- guest-agent authority projection selects `HPD_GUEST_AGENT_CONTAINERD_SOCKET` or `/run/containerd/containerd.sock` for containerd projections;
- `/hpd/container-smoke` prefers `ctr` when the projected engine socket is containerd-shaped, so a Docker CLI present in the guest will not accidentally try to speak to containerd.
- Podman authority binding uses rootless/rootful engine authority classification; guest-agent authority projection selects `HPD_GUEST_AGENT_PODMAN_SOCKET`, `/run/user/1000/podman/podman.sock`, or `/run/podman/podman.sock`; `/hpd/container-smoke` uses `podman --url unix://...` when the projected engine socket is Podman-shaped.
- BuildKit authority binding uses rootless/rootful engine authority classification; guest-agent authority projection selects `HPD_GUEST_AGENT_BUILDKIT_SOCKET`, `/run/user/1000/buildkit-default/buildkitd.sock`, or `/run/buildkit/buildkitd.sock`; `/hpd/container-smoke` uses `buildctl --addr unix://...` when the projected engine socket is BuildKit-shaped.

Remaining work:

- persist audit events beyond single operation responses;
- extend the same sensitive endpoint model to other guest endpoints.

## Current Endpoint Publication State

Ordinary endpoint publication is now helper-backed for the explicit host-local TCP case.

The provider accepts only:

- `PublishedEndpoint` resources with `EndpointListenerKind.HostAddress`;
- `NetworkTransport.Tcp`;
- `EndpointExposureScope.HostLocal`;
- loopback listener addresses such as `127.0.0.1`;
- non-sensitive endpoint policies;
- targets that resolve to a concrete target port through a ready `NetworkMembership`, `ExecutionUnit`, or service record. A guest IPv4 observation is preferred, but the real Apple NAT acceptance path may route through guest loopback via the guest-agent TCP proxy when direct host routing is not available.

The helper path is:

1. Provider resolves the target route from ledger state.
2. Provider sends `EndpointPublish` to `hpd-vz` with listener address/port and resolved guest target address/port.
3. `hpd-vz` creates a host loopback TCP listener.
4. For each accepted host connection, `hpd-vz` uses direct TCP byte-copy when the target is host-reachable. For real Apple NAT guest targets, it can use the guest-agent bounded TCP proxy to connect inside the guest, with loopback fallback for guest-local servers.
5. `EndpointRelease` closes the listener and active sockets.

Intentional limits:

- no automatic port exposure from observed guest listeners;
- no host-LAN or external bind;
- no UDP forwarding yet;
- no Unix socket publication through `PublishedEndpoint`;
- no sensitive engine/credential/trust/debug endpoint publication through this ordinary path.
- non-host-routable guest targets currently use a bounded request/response TCP proxy suitable for HTTP-style endpoint acceptance, not an unbounded arbitrary bidirectional stream.

Sensitive endpoints remain `AuthorityBinding` work. Container-engine sockets must stay guest-visible and authority-bound, not public localhost ports by default.

Practical product effect: a frontend or HTTP server running inside the VM, including one started by a container engine inside the VM, can become visible at `http://127.0.0.1:<hostPort>` only after HPD creates an explicit `PublishedEndpoint` resource for that route.

## Prerequisites

On a macOS Apple Silicon host:

- Xcode command line tools
- Homebrew
- `qemu`
- `zstd`
- `xz`
- .NET 10 SDK

Install the Homebrew tools:

```bash
brew install qemu zstd xz
```

Expected QEMU firmware paths are usually:

```text
/opt/homebrew/share/qemu/edk2-aarch64-code.fd
/opt/homebrew/share/qemu/edk2-arm-vars.fd
```

`guestfish` is optional. The current preparation flow does not require it.

## Build And Sign The Helper

The helper must be signed with the virtualization entitlement before real VM tests:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/hpd-vz/packaging/sign-hpd-vz.sh
```

This builds and signs:

```text
HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/hpd-vz/.build/arm64-apple-macosx/debug/hpd-vz
```

Swift `.build/` output is ignored by git.

## Prepare The Guest Image

Generate the Ubuntu image, inject the HPD guest agent, install Docker, pre-pull the smoke image, export boot artifacts, and write the real acceptance env file:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force \
  --install-docker \
  --output-root ~/.hpd/applevz/images/ubuntu-24.04-arm64-docker \
  --timeout 1200
```

For a containerd image/env instead, use:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force \
  --install-containerd \
  --output-root ~/.hpd/applevz/images/ubuntu-24.04-arm64-containerd \
  --timeout 1200
```

For a rootful Podman image/env instead, use:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force \
  --install-podman \
  --output-root ~/.hpd/applevz/images/ubuntu-24.04-arm64-podman \
  --timeout 1200
```

For a rootful BuildKit image/env instead, use:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force \
  --install-buildkit \
  --output-root ~/.hpd/applevz/images/ubuntu-24.04-arm64-buildkit \
  --timeout 1200
```

Generated files live under the selected output root, for example:

```text
~/.hpd/applevz/images/ubuntu-24.04-arm64-docker/
```

Important outputs:

```text
hpd-ubuntu-24.04-arm64.qcow2
hpd-ubuntu-24.04-arm64.raw
vmlinux
initrd.img
hpd-applevz-real.env
apple-vz.serial.log
```

These are local machine artifacts and must not be committed.

## Check Prerequisites

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/scripts/check-real-acceptance-prereqs.sh
```

A warning about missing `guestfish` is acceptable.

## Run Tests

Use `net10.0` for the focused Apple VZ tests. Do not run the real VM test across multiple target frameworks concurrently; that can race VM resources.

Useful warning/noise filter:

```bash
rg -v "warning|Warning\(s\)|^\s*$|Determining projects to restore|All projects are up-to-date| -> |CSSM_ModuleLoad|Skipping project"
```

Run related non-real suites:

```bash
dotnet test HPD-AI-Framework/dotnet/HPD-Agent.Framework/test/HPD-Execution/HPD-Execution.AppleVirtualization.Tests/HPD-Execution.AppleVirtualization.Tests.csproj \
  -f net10.0 \
  --filter "FullyQualifiedName~AppleVirtualizationRealContainerAcceptanceHarnessTests|FullyQualifiedName~AppleVirtualizationProcessProviderTests|FullyQualifiedName~AppleVirtualizationContainerSmokeWorkflowTests|FullyQualifiedName~AppleVirtualizationHelperGoldenTests.DotNet_generated_process_control_operations_receive_swift_structured_results" \
  -v minimal
```

Run the explicit real container smoke:

```bash
set -a
source ~/.hpd/applevz/images/ubuntu-24.04-arm64-docker/hpd-applevz-real.env
set +a

dotnet test HPD-AI-Framework/dotnet/HPD-Agent.Framework/test/HPD-Execution/HPD-Execution.AppleVirtualization.Tests/HPD-Execution.AppleVirtualization.Tests.csproj \
  -f net10.0 \
  --filter "FullyQualifiedName~Real_container_smoke_acceptance_observes_real_engine_status_only_with_explicit_env" \
  -v minimal
```

Run the explicit real guest HTTP endpoint acceptance:

```bash
set -a
source ~/.hpd/applevz/images/ubuntu-24.04-arm64-docker/hpd-applevz-real.env
set +a

dotnet test HPD-AI-Framework/dotnet/HPD-Agent.Framework/test/HPD-Execution/HPD-Execution.AppleVirtualization.Tests/HPD-Execution.AppleVirtualization.Tests.csproj \
  -f net10.0 \
  --filter "FullyQualifiedName~Real_guest_http_endpoint_acceptance_publishes_guest_server_to_macos_loopback" \
  -v minimal
```

This test requires a guest image prepared with a guest agent that advertises and implements `NetworkStatus` and the bounded TCP proxy operation; rerun image preparation after guest-agent changes.

Run every prepared real container env under `~/.hpd/applevz/images`:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/scripts/run-real-container-acceptance-matrix.sh --keep-going
```

Preview the matrix without booting VMs:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/scripts/run-real-container-acceptance-matrix.sh --dry-run
```

The real test should pass and assert that stdout contains:

```text
hpd-container-smoke: ok
```

## Environment Contract

The generated env file sets the real acceptance variables. The key ones are:

```text
HPD_APPLEVZ_REAL_CONTAINER_SMOKE=1
HPD_APPLEVZ_REAL_HELPER_PATH=...
HPD_APPLEVZ_GUEST_KERNEL=...
HPD_APPLEVZ_GUEST_INITRD=...
HPD_APPLEVZ_GUEST_DISK=...
HPD_APPLEVZ_GUEST_SERIAL_LOG=...
HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION=0.1.0
HPD_APPLEVZ_CONTAINER_ENGINE_KIND=DockerCompatible
HPD_APPLEVZ_CONTAINER_ENGINE_API=DockerCompatible
HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE=Rootful
HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS=runtime-host
HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH=/var/run/docker.sock
HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE=alpine:3.20
HPD_APPLEVZ_GUEST_KERNEL_CMDLINE=root=LABEL=cloudimg-rootfs\ ro\ rootwait\ console=hvc0
```

For a containerd-oriented run, set:

```text
HPD_APPLEVZ_CONTAINER_ENGINE_KIND=Containerd
HPD_APPLEVZ_CONTAINER_ENGINE_API=ContainerdApi
HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE=Rootful
HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH=/run/containerd/containerd.sock
```

For a rootful Podman-oriented run, set:

```text
HPD_APPLEVZ_CONTAINER_ENGINE_KIND=Podman
HPD_APPLEVZ_CONTAINER_ENGINE_API=PodmanApi
HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE=Rootful
HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH=/run/podman/podman.sock
HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE=docker.io/library/alpine:3.20
```

For a rootful BuildKit-oriented run, set:

```text
HPD_APPLEVZ_CONTAINER_ENGINE_KIND=BuildKit
HPD_APPLEVZ_CONTAINER_ENGINE_API=BuildKitApi
HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE=Rootful
HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH=/run/buildkit/buildkitd.sock
HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE=hpd-buildkit-smoke:local
```

Do not commit this env file. It contains local absolute paths.

## How The Current Real Smoke Works

1. Test starts `hpd-vz`.
2. Test starts a real VZ VM using `VZLinuxBootLoader`.
3. VM boots Ubuntu from a per-run scratch copy of the prepared raw disk.
4. Test waits for `RuntimeHostPhase.Running`.
5. Test sends guest-agent readiness probe over virtio-socket.
6. Test observes engine status.
7. Test creates `EngineControlPlane` and `AuthorityBinding` state.
8. Test dispatches `/hpd/container-smoke run --rm --image alpine:3.20 --engine-socket /run/hpd/engine/docker.sock` for Docker, `/run/hpd/engine/containerd.sock` for containerd, `/run/hpd/engine/podman-rootful.sock` for rootful Podman, or `/run/hpd/engine/buildkitd-rootful.sock` for rootful BuildKit.
9. `hpd-vz` forwards process start/wait to the guest agent.
10. Guest agent starts the process inside the VM.
11. The selected in-guest engine client runs inside the VM.
12. Test receives a real `ProcessInvocationResult` and asserts output.

## Debugging Notes

If the VM stops booting or readiness times out:

- inspect the serial log under the selected prepared image output root, for example `~/.hpd/applevz/images/ubuntu-24.04-arm64-docker/apple-vz.serial.log`;
- rebuild/sign `hpd-vz`;
- rerun `prepare-ubuntu-qemu-image.sh --force --install-docker --timeout 1200`;
- confirm `vmlinux` is an uncompressed arm64 kernel image;
- confirm kernel cmdline uses `console=hvc0`;
- confirm the helper has the virtualization entitlement.

If Docker or containerd fails with registry DNS errors during the Apple VZ test:

- the prepared image probably did not pre-pull `alpine:3.20`;
- rerun the image preparation script;
- check QEMU prep serial log if cloud-init failed.

If the process returns exit `69`:

- `/hpd/container-smoke` did not find the requested engine socket.

If the process returns exit `70`:

- Docker was invoked but `docker run` failed. The test failure message should include captured stdout/stderr.

For containerd failures, also check whether the prepared guest has `ctr`, `containerd.service` is active, and `/run/containerd/containerd.sock` exists in the guest.

For Podman failures, also check whether the prepared guest has `podman`, `podman.socket` is active, and `/run/podman/podman.sock` exists in the guest.

For BuildKit failures, also check whether the prepared guest has `buildctl`, `buildkitd`, `buildkit.service` is active, and `/run/buildkit/buildkitd.sock` exists in the guest.

## Git Ignore Expectations

Source should be tracked:

- `docs/apple-virtualization/**/*.md`
- `docs/apple-virtualization/**/*.sh`
- `src/HPD-Execution/hpd-vz/Sources/**`
- `src/HPD-Execution/hpd-guest-agent/**`
- `test/HPD-Execution/HPD-Execution.AppleVirtualization.Tests/**`

Generated artifacts should not be tracked:

- `hpd-applevz-real.env`
- `apple-vz.serial.log`
- `qemu-prep.serial.log`
- `cidata/`
- `cidata.iso`
- `hpdboot.raw`
- `edk2-vars.fd`
- `base-ubuntu-24.04-arm64.img`
- `hpd-ubuntu-24.04-arm64.qcow2`
- `hpd-ubuntu-24.04-arm64.raw`
- `vmlinuz`
- `vmlinux`
- `initrd.img`
- `.hpd-real-acceptance-scratch/`

These ignore rules are in the repo root `.gitignore`.

## DevKit Execution APIs

The DevKit now owns the developer-facing orchestration surface:

- `AppleVirtualizationImagePreparation` builds and runs the selected prepared-image backend command.
- `AppleVirtualizationRealAcceptanceExecutor` validates an env, optionally runs prereqs, injects env variables into `dotnet test`, and can run a full matrix sequentially.
- `AppleVirtualizationCleanupExecutor` deletes only planned transient targets: serial logs and `.hpd-real-acceptance-scratch/`.
- `IAppleVirtualizationDevKitProcessRunner` is injectable, so CLI/product wrappers can test command generation without starting QEMU or Apple VZ.

The existing shell scripts remain backend helpers and terminal conveniences. New product commands should be thin wrappers over these DevKit APIs or should shell to `hpd-applevz`; they should not reimplement shell parsing, matrix discovery, validation, or cleanup planning.

## CLI Wrapper

The CLI project builds the `hpd-applevz` assembly:

```bash
dotnet build HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj
```

Common commands:

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- host

dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- \
  prepare \
  --framework-root HPD-AI-Framework/dotnet/HPD-Agent.Framework \
  --output-root ~/.hpd/applevz/images/ubuntu-24.04-arm64-docker \
  --engine docker \
  --disk-size 8G \
  --force

dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- \
  matrix \
  --framework-root HPD-AI-Framework/dotnet/HPD-Agent.Framework \
  --keep-going
```

Use `--dry-run` with `prepare`, `run`, or `matrix` to preview the DevKit commands or matrix entries without starting QEMU, Apple VZ, or `dotnet test`.

## Next Recommended Slice

Productize explicit endpoint opening from HPDOS now that the real helper and real guest acceptance paths exist.

Acceptance target:

- HPDOS shows publishable guest listeners as suggestions only;
- user action creates a `PublishedEndpoint`;
- HPDOS opens `http://127.0.0.1:<hostPort>` only after the endpoint reaches `Bound`;
- release/removal is visible and deterministic;
- sensitive sockets continue to route through `AuthorityBinding`, not ordinary endpoints.

After that, product work can add listener suggestions from guest-agent observations, but suggestions must not auto-publish ports.
