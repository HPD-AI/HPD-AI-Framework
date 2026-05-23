# Apple Virtualization Real Acceptance Terminal Runbook

This runbook is the terminal-facing handoff for the Apple Virtualization real-container acceptance path.

For first-time local setup, start with `docs/apple-virtualization/developer-setup.md`. Use this runbook when you need the lower-level terminal commands and acceptance details.

## Current Status

The fake/helper protocol, provider mapping, engine-status, provisioning evidence, revocation evidence, real-harness gating, bounded virtio-socket guest-agent hello/ready handshake, and real guest process path are in place. The local Swift helper builds, launches a real Apple Virtualization Linux guest, and runs the real container smoke through guest-agent authority projection.

The helper also owns explicit host-local TCP forwarding for ordinary `PublishedEndpoint` resources. This is not automatic listener discovery. HPD must create a `PublishedEndpoint`, and `hpd-vz` binds only a host loopback listener such as `127.0.0.1:<port>`. For Apple NAT guests, the accepted practical route is helper-owned host listener -> guest-agent bounded TCP proxy -> guest loopback/server; direct host routing to a guest NAT address is not assumed.

The prepared-image path has passed real acceptance for these engines on this host:

- Docker rootful: `DockerCompatible`, `/var/run/docker.sock`;
- containerd rootful: `ContainerdApi`, `/run/containerd/containerd.sock`;
- Podman rootful: `PodmanApi`, `/run/podman/podman.sock`;
- BuildKit rootful: `BuildKitApi`, `/run/buildkit/buildkitd.sock`.

The harness is still explicit opt-in. A skipped real test is not proof of real VM behavior.

`HPD-Execution.AppleVirtualization.DevKit` is the embeddable .NET surface for prepared-image discovery, env parsing, validation, matrix planning, image-prep command execution, real matrix execution, cleanup execution, and host platform diagnostics. `HPD-Execution.AppleVirtualization.Cli` is the thin developer/product command wrapper over that DevKit surface. The `.sh` files are current macOS backend helpers for QEMU/cloud-init/image-prep and remain useful terminal entry points, but product code should prefer the DevKit API or CLI wrapper over shelling out directly.

## Required Host Tools

The minimum terminal path needs:

- `swift` for building `hpd-vz`;
- `curl` for downloading image artifacts when needed;
- `bsdtar` for archive inspection/extraction;
- `qemu-img` for qcow2/raw conversion;
- `hdiutil` for NoCloud seed ISO creation;
- `zstd` and `xz` for compressed image artifacts.

`guestfish` is optional and not required for the current QEMU/cloud-init preparation path.

## Image Source Guidance

Use Ubuntu 24.04 arm64 as the first practical base:

```text
https://cloud-images.ubuntu.com/releases/noble/release/ubuntu-24.04-server-cloudimg-arm64.img
```

This is a base input, not a finished HPD guest. The finished bundle must satisfy `guest-image-contract.md`.

Apple Container's Kata kernel and `vminit` flow are useful references for how another system packages minimal VM support, but HPD should not copy that lifecycle. Lima's Ubuntu LTS cloud-image path is the closer reference for the base OS choice.

## Prepare Images

The image-prep script downloads/converts Ubuntu, injects the HPD guest agent and `/hpd/container-smoke` through cloud-init, installs the selected engine, exports direct Apple VZ boot artifacts, converts the prepared disk to raw, and writes `hpd-applevz-real.env`.

The CLI dry-run form shows the exact backend helper command without starting QEMU:

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- \
  prepare \
  --framework-root HPD-AI-Framework/dotnet/HPD-Agent.Framework \
  --output-root "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-docker" \
  --engine docker \
  --disk-size 8G \
  --force \
  --dry-run
```

Containerd:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force \
  --install-containerd \
  --output-root "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64" \
  --timeout 1200
```

Rootful Podman:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force \
  --install-podman \
  --output-root "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-podman" \
  --timeout 1200
```

Rootful BuildKit:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force \
  --install-buildkit \
  --output-root "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-buildkit" \
  --timeout 1200
```

Rootful Docker:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force \
  --install-docker \
  --disk-size 8G \
  --output-root "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-docker" \
  --timeout 1200
```

## Environment File

Generate a manual env file with:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/scripts/write-real-container-env.sh \
  --kernel /path/to/vmlinux \
  --initrd /path/to/initrd \
  --disk /path/to/writable-linux-disk.img \
  --serial-log /tmp/hpd-applevz-serial.log \
  --guest-agent-version 0.1.0 \
  --engine-kind DockerCompatible \
  --engine-api DockerCompatible \
  --authority-mode Rootless \
  --engine-socket /run/user/1000/docker.sock \
  --smoke-image alpine:3.20 \
  --output /tmp/hpd-applevz-real.env
```

Check the environment before running tests:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/scripts/check-real-acceptance-prereqs.sh /tmp/hpd-applevz-real.env
```

## Single-Env Test Command

Run only the explicit real container smoke:

```bash
set -a
source "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-docker/hpd-applevz-real.env"
set +a

dotnet test HPD-AI-Framework/dotnet/HPD-Agent.Framework/test/HPD-Execution/HPD-Execution.AppleVirtualization.Tests/HPD-Execution.AppleVirtualization.Tests.csproj \
  -f net10.0 \
  --filter "FullyQualifiedName~Real_container_smoke_acceptance_observes_real_engine_status_only_with_explicit_env" \
  -v minimal 2>&1 |
rg -v "warning|Warning\\(s\\)|^\\s*$|Determining projects to restore|All projects are up-to-date| -> |CSSM_ModuleLoad|Skipping project"
```

The real test should pass and assert that stdout contains:

```text
hpd-container-smoke: ok
```

Run only the explicit real guest HTTP endpoint acceptance:

```bash
set -a
source "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-docker/hpd-applevz-real.env"
set +a

dotnet test HPD-AI-Framework/dotnet/HPD-Agent.Framework/test/HPD-Execution/HPD-Execution.AppleVirtualization.Tests/HPD-Execution.AppleVirtualization.Tests.csproj \
  -f net10.0 \
  --filter "FullyQualifiedName~Real_guest_http_endpoint_acceptance_publishes_guest_server_to_macos_loopback" \
  -v minimal 2>&1 |
rg -v "warning|Warning\\(s\\)|^\\s*$|Determining projects to restore|All projects are up-to-date| -> |CSSM_ModuleLoad|Skipping project"
```

The HTTP endpoint test starts `python3 -m http.server` inside the VM, publishes it through `PublishedEndpoint`, fetches `http://127.0.0.1:<hostPort>` from macOS, and releases the endpoint. The real Apple NAT path uses the guest-agent bounded TCP proxy when the guest is not directly host-routable. Rerun image preparation after guest-agent changes so the prepared image contains `NetworkStatus` and TCP proxy support.

## Matrix Command

Run every prepared env under `~/.hpd/applevz/images`:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/scripts/run-real-container-acceptance-matrix.sh --keep-going
```

The CLI equivalent is:

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- \
  matrix \
  --framework-root HPD-AI-Framework/dotnet/HPD-Agent.Framework \
  --keep-going
```

Preview what will run:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/scripts/run-real-container-acceptance-matrix.sh --dry-run
```

CLI preview:

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- \
  matrix \
  --framework-root HPD-AI-Framework/dotnet/HPD-Agent.Framework \
  --dry-run
```

Run one specific env:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/scripts/run-real-container-acceptance-matrix.sh \
  --env-file "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-buildkit/hpd-applevz-real.env"
```

The current matrix on this host reports `4 passed, 0 failed` for BuildKit, Docker, Podman, and containerd.

## Endpoint Publication Smoke

The helper path can be smoke-tested without booting a VM by publishing a loopback endpoint to a local echo socket. This verifies the real `hpd-vz` listener and direct byte-copy path, but it does not prove guest routing.

The real VM HTTP endpoint acceptance proves guest routing:

1. boot a prepared guest;
2. start a tiny HTTP server inside the guest;
3. publish the guest port through `PublishedEndpoint`;
4. fetch `http://127.0.0.1:<hostPort>` from macOS through the helper/guest-agent proxy path;
5. release the endpoint and verify the listener closes.

Do not use this mechanism for engine sockets, credential proxies, trust mutation endpoints, or debug endpoints. Those remain sensitive authority surfaces and must be represented through `AuthorityBinding`.

## Next Code Work

The next implementation targets are:

- add product/UI affordances for explicit "Open localhost endpoint" actions;
- optionally add guest-agent listener suggestions, without automatic publication;
- persist authority audit events beyond single-operation responses;
- extend the same explicit sensitive endpoint model beyond container-engine sockets;
- add real rootless prepared-image coverage for Podman and BuildKit if product workflows need rootless engines;
- package or host the CLI from the product command surface where needed.
