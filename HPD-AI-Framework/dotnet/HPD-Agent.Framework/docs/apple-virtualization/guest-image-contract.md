# Apple Virtualization Guest Image Contract

This document defines the prepared Linux guest image contract for the HPD Apple Virtualization provider real acceptance path.

The contract is intentionally narrow. HPD does not own rootfs import, image building, block-volume provisioning, or default Docker/containerd/Podman/BuildKit installation in this slice. A macOS host that opts into real acceptance must provide a bootable Linux guest image that already contains the HPD guest agent and, for container smoke acceptance, an in-guest container or build engine plus `/hpd/container-smoke`.

Do not assume macOS supplies a built-in Linux guest image. Apple Virtualization supplies VM APIs, but the caller must provide Linux boot/storage inputs. HPD's first blessed prepared guest base for this contract is an Ubuntu 24.04 arm64 cloud image adapted for Apple Virtualization, HPD guest-agent readiness, and `/hpd/container-smoke`. Apple Container's Kata kernel and `vminit` OCI image flow are reference symptoms only, not a generic macOS Linux VM image. Lima's Ubuntu LTS cloud-image templates are the practical VM reference for this prepared-image shape.

## Boundary Rules

- The VM is the HPD isolation boundary.
- Real acceptance is disabled unless `HPD_APPLEVZ_REAL_CONTAINER_SMOKE=1` and all required environment variables are present.
- Host Docker, Podman, containerd, BuildKit, SSH-agent, or arbitrary host sockets cannot satisfy the engine path.
- Engine API sockets are guest/runtime-host resources and become usable only through `AuthorityBinding`.
- Engine sockets must not be exposed through ordinary `PublishedEndpoint` resources.
- Docker/containerd/Podman/BuildKit must not be installed by HPD by default.
- `/hpd/container-smoke` is a test-contract command, not a public container workload API.

## Host Inputs

The prepared macOS host supplies these files and values for real container acceptance:

| Variable | Required | Meaning |
| --- | --- | --- |
| `HPD_APPLEVZ_REAL_CONTAINER_SMOKE` | Yes | Must be exactly `1` to opt into real VM/container acceptance. |
| `HPD_APPLEVZ_REAL_HELPER_PATH` | Yes | Existing executable `hpd-vz` helper with Apple Virtualization entitlement. |
| `HPD_APPLEVZ_GUEST_KERNEL` | Yes | Existing Linux kernel for `VZLinuxBootLoader`. |
| `HPD_APPLEVZ_GUEST_INITRD` | Yes | Existing initial ramdisk for `VZLinuxBootLoader`. |
| `HPD_APPLEVZ_GUEST_DISK` | Yes | Existing writable Linux disk image attached as VM storage. |
| `HPD_APPLEVZ_GUEST_SERIAL_LOG` | Yes | Host path where helper may write bounded serial diagnostics. Parent directory must be creatable. |
| `HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION` | Yes | HPD guest-agent version expected during readiness. |
| `HPD_APPLEVZ_CONTAINER_ENGINE_KIND` | Yes | `DockerCompatible`, `Containerd`, `Podman`, or `BuildKit` for the current real smoke path. |
| `HPD_APPLEVZ_CONTAINER_ENGINE_API` | Yes | `DockerCompatible`, `ContainerdApi`, `PodmanApi`, or `BuildKitApi`, matching the engine kind. |
| `HPD_APPLEVZ_CONTAINER_ENGINE_AUTHORITY_MODE` | Yes | `Rootless` or `Rootful`. `Mixed` and provider-defined modes are not accepted for real smoke. |
| `HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_LOCUS` | Optional | `runtime-host` or `guest`. Defaults to `runtime-host`. `host` and `execution-unit` are rejected. |
| `HPD_APPLEVZ_CONTAINER_ENGINE_SOCKET_PATH` | Yes | Absolute in-guest Unix socket path for the engine daemon. |
| `HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE` | Yes | Image reference for the smoke command. The image must already be available locally or pullable by the in-guest engine. |
| `HPD_APPLEVZ_GUEST_BUNDLE_ROOT` | Optional | Host bundle root for helper diagnostics/configuration. |
| `HPD_APPLEVZ_GUEST_KERNEL_CMDLINE` | Optional | Additional Linux kernel command line. |
| `HPD_APPLEVZ_VIRTIOFS_HOST_PATH` | Optional | Existing host path for explicit virtiofs smoke wiring when a test requires it. |
| `HPD_APPLEVZ_VIRTIOFS_TAG` | Optional | Virtiofs tag corresponding to the host path. |
| `HPD_APPLEVZ_ENGINE_PROVISIONING_ENABLED` | Optional | Boolean readiness gate only for this harness slice. Defaults to `false`; must be `true` or `false` when present. |
| `HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_PACKAGE_INSTALL` | Optional | Boolean readiness gate for later opt-in provisioning evidence. Defaults to `false`; must be `true` or `false` when present. |
| `HPD_APPLEVZ_ENGINE_PROVISIONING_ALLOW_SERVICE_ENABLEMENT` | Optional | Boolean readiness gate for later opt-in provisioning evidence. Defaults to `false`; must be `true` or `false` when present. |

## Boot And Storage Requirements

The required boot shape is `VZLinuxBootLoader` with a kernel, initrd, optional command line, and a writable disk image. EFI boot may be supported by later packaging work, but this real acceptance contract requires the Linux boot-loader environment variables above.

The guest disk must contain a Linux userspace compatible with the host Mac architecture selected by the helper. The disk must be writable because HPD readiness, engine state, and smoke cleanup can require guest-local runtime directories such as `/run`, `/run/user/<uid>`, `/var/lib`, `/var/tmp`, and `/tmp`.

The Linux kernel must support:

- virtio block storage;
- virtio network as configured by the helper;
- virtio socket transport for the HPD guest-agent control channel;
- virtiofs when host directory projection is configured;
- Unix domain sockets.

## HPD Guest Agent Requirements

The image must start the HPD guest agent automatically before real acceptance can claim `RuntimeHost` readiness. The agent must be reachable through the helper-mediated guest-control transport and must implement these capabilities:

- `hello`, `health`, `ready`, protocol version, guest boot generation, and agent generation reporting;
- capability advertisement with bounded missing-capability diagnostics;
- projection mount/status/observe/sync/finalization operations used by existing HPD projection tests;
- process start, stdin close/write, signal/stop, wait, and read-output operations;
- byte-oriented stdout/stderr output chunks with bounded capture accounting;
- authority bind/status/revoke for engine socket projection;
- engine status probing for the configured in-guest engine socket;
- engine provisioning status fields when provisioning is explicitly requested, without default package installation.

Real container acceptance currently requires the guest agent to advertise at least:

```text
engine.status
authority.bind
authority.revoke
process.start
process.readOutput
```

The guest agent must not report host-locus Docker, Podman, containerd, or BuildKit observations as ready engine state.

### Virtio-Socket Readiness Wire Protocol

The helper connects to the configured guest-agent virtio-socket port with `VZVirtioSocketDevice.connect(toPort:)`.
If the helper request omits an endpoint port, the HPD default is `7777`.

Readiness uses newline-delimited JSON envelopes. Each request and response is one UTF-8 JSON object followed by `\n`. The helper bounds connect, write, read, and frame size; the current frame limit is 64 KiB.

The helper first sends `Hello`:

```json
{"ProtocolVersion":"1.0","MessageType":0,"Operation":0,"RequestId":"guest-hello-...","SequenceNumber":1,"HostId":"..."}
```

The guest agent must return a response envelope containing `Hello`:

```json
{
  "ProtocolVersion": "1.0",
  "MessageType": 1,
  "Operation": 0,
  "ResponseStatus": 0,
  "Hello": {
    "AgentVersion": "0.1.0",
    "ProtocolVersion": "1.0",
    "GuestBootId": "guest-boot-id",
    "GuestBootGeneration": 1,
    "GuestAgentGeneration": 1,
    "Capabilities": {
      "ProcessStart": true,
      "ProcessReadOutput": true,
      "NetworkStatus": true,
      "AuthorityProjection": true,
      "AuthorityRevocation": true,
      "EngineStatus": true
    }
  }
}
```

The helper then sends `Ready`:

```json
{"ProtocolVersion":"1.0","MessageType":0,"Operation":2,"RequestId":"guest-ready-...","SequenceNumber":2,"HostId":"..."}
```

The guest agent must return a response envelope containing `Ready`:

```json
{
  "ProtocolVersion": "1.0",
  "MessageType": 1,
  "Operation": 2,
  "ResponseStatus": 0,
  "Ready": {
    "IsReady": true,
    "GuestBootId": "guest-boot-id",
    "GuestBootGeneration": 1,
    "GuestAgentGeneration": 1
  }
}
```

If protocol version, expected agent version, readiness, or required capability checks fail, `RuntimeHost` readiness must fail with a bounded diagnostic. Host-locus engine state must never be used to satisfy these checks.

## User And systemd Assumptions

Rootless mode assumes a non-root guest user with a stable numeric UID. The current acceptance harness uses UID `1000` unless configured otherwise by the prepared image contract. The rootless runtime directory must be:

```text
/run/user/1000
```

For rootless Docker-compatible smoke, the accepted socket path is:

```text
/run/user/1000/docker.sock
```

For rootful Docker-compatible smoke, the accepted socket path is:

```text
/var/run/docker.sock
```

For rootful containerd smoke, the accepted socket path is:

```text
/run/containerd/containerd.sock
```

For rootless Podman smoke, the accepted socket path is:

```text
/run/user/1000/podman/podman.sock
```

For rootful Podman smoke, the accepted socket path is:

```text
/run/podman/podman.sock
```

For rootless BuildKit smoke, the accepted socket path is:

```text
/run/user/1000/buildkit-default/buildkitd.sock
```

For rootful BuildKit smoke, the accepted socket path is:

```text
/run/buildkit/buildkitd.sock
```

Rootless engines require user-session support equivalent to `systemd --user` plus a valid `XDG_RUNTIME_DIR`. Rootful engines require system `systemd` service management or an equivalent prepared service already running inside the guest. If systemd is not present, the guest may still pass only when the engine and guest agent are already started by another prepared-image mechanism and report honest status.

## Engine Requirements

The prepared image may contain Docker-compatible, containerd, Podman, or BuildKit engine bits, but HPD must not assume they exist by default. Real acceptance succeeds only when the guest agent observes the configured engine from inside the guest boundary.

The engine status response must include:

- observation locus `RuntimeHost` or guest-equivalent, never host;
- observed socket path;
- ready/degraded/not-installed/unavailable phase;
- API kind and authority mode;
- bounded version/status text;
- bounded observed container list, if any;
- bounded diagnostics and truncation flags when limits are reached;
- sensitive engine socket endpoint metadata with `GuestVisibleOnly=true`.

The engine socket is source authority only. HPD projects it to the smoke execution unit at one of these harness paths:

```text
/run/hpd/engine/docker.sock
/run/hpd/engine/containerd.sock
/run/hpd/engine/podman.sock
/run/hpd/engine/podman-rootful.sock
/run/hpd/engine/buildkitd.sock
/run/hpd/engine/buildkitd-rootful.sock
```

The projected path is a test-harness socket path and does not make the engine a public endpoint.

## Network Requirements

The guest must have enough network support for:

- guest-agent control over the helper-mediated channel;
- guest-agent `NetworkStatus` and bounded TCP proxy operations for explicit
  `PublishedEndpoint` acceptance over Apple NAT;
- engine image pulls only when the prepared test image expects a pull;
- normal loopback and Unix socket operations.

The real smoke command runs with HPD process isolation network egress blocked. If the smoke image is not already available locally, the prepared image or engine setup must arrange the pull before the smoke command or explicitly document that the test requires guest network egress during engine preparation.

## `/hpd/container-smoke` Command Contract

The prepared guest image must provide an executable regular file:

```text
/hpd/container-smoke
```

It must be executable by the HPD process identity used for the smoke run. It must not require host shell state, host sockets, or unbounded interactive input.

### Invocation

The current real acceptance harness invokes:

```text
/hpd/container-smoke run --rm --image <image-ref> --engine-socket /run/hpd/engine/docker.sock
```

For containerd, the harness uses:

```text
/hpd/container-smoke run --rm --image <image-ref> --engine-socket /run/hpd/engine/containerd.sock
```

For Podman, the harness uses `/run/hpd/engine/podman.sock` for rootless mode and `/run/hpd/engine/podman-rootful.sock` for rootful mode:

```text
/hpd/container-smoke run --rm --image <image-ref> --engine-socket /run/hpd/engine/podman-rootful.sock
```

For BuildKit, the harness uses `/run/hpd/engine/buildkitd.sock` for rootless mode and `/run/hpd/engine/buildkitd-rootful.sock` for rootful mode. The `<image-ref>` argument is still required by the common harness, but the BuildKit path performs a local scratch build and does not require pulling that image:

```text
/hpd/container-smoke run --rm --image <image-ref> --engine-socket /run/hpd/engine/buildkitd-rootful.sock
```

Required arguments:

- `run`: execute a single smoke workload.
- `--rm`: remove the smoke workload/container after completion.
- `--image <image-ref>`: image reference supplied by `HPD_APPLEVZ_CONTAINER_SMOKE_IMAGE`.
- `--engine-socket <path>`: projected engine API socket path supplied by HPD authority binding.

Supported optional arguments:

- `--timeout-ms <integer>`: internal command timeout. If omitted, the script must complete within the HPD process timeout.
- `--label <key=value>`: may be used by future harnesses to tag transient smoke workload state.

Unknown arguments must fail with exit code `64`.

### Environment

The command must treat these environment variables as optional hints only:

- `HPD_CONTAINER_SMOKE_ENGINE_API`
- `HPD_CONTAINER_SMOKE_AUTHORITY_MODE`
- `HPD_CONTAINER_SMOKE_MAX_OUTPUT_BYTES`
- `XDG_RUNTIME_DIR`

The command must not require `DOCKER_HOST`, `CONTAINER_HOST`, `PODMAN_HOST`, `CONTAINERD_ADDRESS`, or `BUILDKIT_HOST`. If those host-shaped variables are present, `/hpd/container-smoke` must ignore them unless they exactly match the projected `--engine-socket` path inside the guest.

### stdin/stdout/stderr

stdin is closed or empty. The command must not block waiting for stdin.

stdout is for a bounded success summary. On success it must write at most 64 KiB and should include:

```text
hpd-container-smoke: ok
engine=<docker-compatible|containerd|podman|buildkit>
image=<image-ref>
```

stderr is for bounded diagnostics. On failure it must write at most 64 KiB and should include:

```text
hpd-container-smoke: failed
reason=<stable-reason>
detail=<bounded detail>
```

The command must not print unbounded engine logs, image pull streams, container logs, stack traces, or JSON dumps. If engine output is useful, tail or summarize it inside the 64 KiB per-stream bound.

### Exit Codes

| Exit code | Meaning |
| --- | --- |
| `0` | Smoke workload ran and cleanup completed. |
| `2` | Engine socket path missing, not a Unix socket, or permission denied. |
| `3` | Engine daemon not ready or API handshake failed. |
| `4` | Image unavailable and the prepared image did not permit or complete a pull. |
| `5` | Smoke workload failed or returned a nonzero result. |
| `6` | Cleanup failed or left known transient smoke state behind. |
| `7` | Timeout while running or cleaning up. |
| `64` | Command-line usage error. |
| `70` | Internal smoke harness error. |

HPD preserves the process exit code in `ProcessInvocationResult`. A nonzero exit code is not rewritten; HPD adds a bounded `AppleVirtualization.ContainerSmokeNonZeroExit` diagnostic around the existing process accounting.

### Cleanup

`/hpd/container-smoke run --rm` must remove the transient container/task/pod it created before exiting. Cleanup is required on both success and failure. Repeated cleanup must be idempotent.

The command must not remove unrelated user containers, images, volumes, BuildKit cache, containerd namespaces, Podman state, or HPD authority projection sockets. HPD owns revocation of `/run/hpd/engine/docker.sock`, `/run/hpd/engine/containerd.sock`, `/run/hpd/engine/podman.sock`, `/run/hpd/engine/podman-rootful.sock`, `/run/hpd/engine/buildkitd.sock`, and `/run/hpd/engine/buildkitd-rootful.sock` through `AuthorityBinding`; the smoke command may close its own client connection but must not delete the projected socket path.

## Failure Diagnostics

Prepared-host failures must be distinguishable without inspecting host Docker state:

- missing env vars: harness skip diagnostics name the missing `HPD_APPLEVZ_*` variable;
- missing files: harness skip diagnostics name the helper/kernel/initrd/disk variable;
- host-locus socket: `AppleVirtualization.RealContainerHostEngineSocketPassthroughRejected`;
- execution-unit-locus source socket: `AppleVirtualization.RealContainerEngineSocketLocusUnsupported`;
- invalid socket path: `AppleVirtualization.RealContainerEngineSocketPathInvalid`;
- authority mode mismatch: `AppleVirtualization.RealContainerEngineSocketPathModeMismatch`;
- smoke image missing: `AppleVirtualization.RealContainerSmokeImageInvalid`;
- guest-agent missing capability: `AppleVirtualization.GuestAgentReadiness.MissingCapability`;
- engine not ready: `AppleVirtualization.ContainerSmokeEngineNotReady`;
- authority missing/revoked: `AppleVirtualization.ContainerSmokeEngineAuthorityRequired` or `AppleVirtualization.ContainerSmokeEngineAuthorityRevoked`;
- nonzero smoke command: `AppleVirtualization.ContainerSmokeNonZeroExit`.

## Acceptance Harness Contract

The acceptance harness must fail closed or skip before real VM work when the contract is not satisfied. It must not infer readiness from host runtime environment variables.

Before booting a VM, the harness validates:

- explicit env gate;
- macOS Apple Virtualization host support;
- helper executable, kernel, initrd, writable disk paths, optional bundle/share support paths, and writable serial log file target;
- guest-agent expected version;
- engine kind/API/authority mode;
- socket locus and socket path;
- smoke image reference.

After booting a VM, the harness validates:

- helper hello/preflight;
- VM reaches running phase;
- guest agent is verified ready with required capabilities;
- engine status is guest-derived and ready;
- engine endpoint is sensitive and guest-visible;
- engine authority binding is projected;
- `/hpd/container-smoke` runs through HPD process execution and bounded output accounting;
- authority revocation and host stop/delete cleanup are requested.

## Reference Grounding

Apple Virtualization constrains this contract as follows:

- `VZLinuxBootLoader` requires explicit kernel configuration and optional initrd/command line for Linux boot.
- `VZEFIBootLoader` is a different boot path and is not the current real smoke contract.
- Apple storage APIs model disk/block attachments; HPD does not claim rootfs or block-volume ownership in this slice.
- Apple shared-directory APIs require Linux `CONFIG_VIRTIO_FS` for virtiofs projection.

Lima and Apple Container were used only as symptom references:

- Lima's default template shows containerd/user/rootful setup is explicit configuration, not a universal default.
- Lima's containerd bootstrap shows systemd and user-systemd assumptions for rootless engines.
- Apple Container process configuration and process IO show process execution is argument/env/working-directory based with explicit IO handling.
