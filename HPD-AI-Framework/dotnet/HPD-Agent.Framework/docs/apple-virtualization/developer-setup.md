# Apple Virtualization Developer Setup

This is the setup path for developers working on the HPD Apple Virtualization provider.

The intended developer experience is:

1. Build and sign the Swift helper.
2. Prepare one reusable Linux guest image.
3. Run opt-in real acceptance tests against per-run scratch disk copies.
4. Iterate on provider/helper/guest-agent code.
5. Rebuild only the layer that changed.

The VM is the isolation boundary. Container engines run inside the VM. Host Docker, Podman, containerd, and BuildKit sockets are not mounted into the guest.

## What The DX Looks Like

There are three layers:

- `HPD-Execution.AppleVirtualization.DevKit`: embeddable .NET setup and acceptance API.
- `HPD-Execution.AppleVirtualization.Cli`: thin command wrapper for developers and product tooling.
- shell scripts under `docs/apple-virtualization/`: current macOS backend helpers for QEMU, cloud-init, and image preparation.

Prefer the CLI or DevKit from product code. Use the shell scripts when you need direct terminal control or are debugging image preparation.

The feedback loop is intentionally split:

- Provider or .NET contract changes: run `dotnet test`.
- Swift helper changes: run `packaging/sign-hpd-vz.sh`.
- Guest-agent changes: rerun image preparation, because the prepared guest disk must contain the new agent.
- Real Apple VZ behavior: run the explicit real acceptance tests after sourcing a prepared env file.

## Host Requirements

Use a macOS host with Apple Virtualization support. Apple Silicon is the tested path.

Required tools:

```bash
xcode-select --install
brew install qemu zstd xz
```

Optional:

```bash
brew install libguestfs
```

`guestfish` is not required for the current QEMU/cloud-init preparation path.

## Build And Sign hpd-vz

From the repository root:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/hpd-vz/packaging/sign-hpd-vz.sh
```

This builds and ad-hoc signs:

```text
HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/hpd-vz/.build/arm64-apple-macosx/debug/hpd-vz
```

The signature includes `com.apple.security.virtualization`. Rerun this after Swift helper changes because `swift build` can replace the signed binary.

## Check The CLI

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- --help
```

Useful commands:

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- host

dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- discover --check-files
```

## Prepare A Guest Image

Prepare the default Docker guest:

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- \
  prepare \
  --framework-root HPD-AI-Framework/dotnet/HPD-Agent.Framework \
  --engine docker \
  --output-root "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-docker" \
  --force
```

Equivalent direct script:

```bash
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force \
  --install-docker \
  --disk-size 8G \
  --output-root "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-docker" \
  --timeout 1200
```

Other engines:

```bash
# containerd
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force --install-containerd --disk-size 8G \
  --output-root "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-containerd" \
  --timeout 1200

# Podman rootful
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force --install-podman --disk-size 8G \
  --output-root "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-podman" \
  --timeout 1200

# BuildKit rootful
HPD-AI-Framework/dotnet/HPD-Agent.Framework/docs/apple-virtualization/guest-image/prepare-ubuntu-qemu-image.sh \
  --force --install-buildkit --disk-size 8G \
  --output-root "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-buildkit" \
  --timeout 1200
```

Each prepared image writes:

```text
hpd-applevz-real.env
hpd-ubuntu-24.04-arm64.raw
hpd-ubuntu-24.04-arm64.qcow2
cidata.iso
hpdboot.raw
qemu-prep.serial.log
```

The `.env` file is the contract used by real acceptance tests.

## Validate A Prepared Env

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- \
  validate \
  --framework-root HPD-AI-Framework/dotnet/HPD-Agent.Framework \
  --env-file "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-docker/hpd-applevz-real.env" \
  --check-files
```

## Run Managed Tests

These do not require a real VM unless real acceptance env vars are set:

```bash
dotnet test HPD-AI-Framework/dotnet/HPD-Agent.Framework/test/HPD-Execution/HPD-Execution.AppleVirtualization.Tests/HPD-Execution.AppleVirtualization.Tests.csproj \
  -f net10.0 \
  -v minimal 2>&1 |
rg -v "warning|Warning\\(s\\)|^\\s*$|Determining projects to restore|All projects are up-to-date| -> |CSSM_ModuleLoad|Skipping project"
```

Expected normal result is the full managed suite passing with real acceptance tests skipped.

## Run Real Container Acceptance

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

This proves:

- real VM boot;
- guest-agent readiness;
- engine status observed from inside the VM;
- engine socket projected through `AuthorityBinding`;
- smoke command runs inside the VM boundary.

## Run Real Guest HTTP Endpoint Acceptance

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

This proves:

- real VM boot;
- guest HTTP server starts inside the VM;
- HPD publishes a `PublishedEndpoint`;
- macOS can fetch `http://127.0.0.1:<hostPort>`;
- endpoint release closes access.

Apple NAT is treated as guest egress, not documented host ingress. The real endpoint path is:

```text
macOS loopback listener
  -> hpd-vz
  -> guest-agent bounded TCP proxy over virtio-socket
  -> guest loopback/server
```

## Run The Prepared Matrix

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- \
  matrix \
  --framework-root HPD-AI-Framework/dotnet/HPD-Agent.Framework \
  --keep-going
```

Preview without running:

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- \
  matrix \
  --framework-root HPD-AI-Framework/dotnet/HPD-Agent.Framework \
  --dry-run
```

## Cleanup

The acceptance tests use per-run scratch disks, but failed runs can leave helper state or temporary files. Use:

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- \
  cleanup \
  --framework-root HPD-AI-Framework/dotnet/HPD-Agent.Framework \
  --env-file "$HOME/.hpd/applevz/images/ubuntu-24.04-arm64-docker/hpd-applevz-real.env"
```

## When To Rebuild What

| Change | Required action |
| --- | --- |
| C# provider/test/DevKit code | `dotnet test` |
| Swift helper code | `packaging/sign-hpd-vz.sh` |
| Guest-agent Python or smoke script | rerun image preparation |
| Image-prep script | rerun image preparation |
| Engine setup | prepare the affected engine image and rerun real acceptance |
| Endpoint forwarding code | sign `hpd-vz`, then rerun real HTTP endpoint acceptance |

## Common Failures

If real tests skip, check:

- env vars are sourced from `hpd-applevz-real.env`;
- `HPD_APPLEVZ_REAL_ACCEPTANCE=1`;
- `HPD_APPLEVZ_REAL_HELPER_PATH` points to the signed helper;
- kernel, initrd, raw disk, and boot transfer disk paths exist.

If VM start fails, rebuild/sign `hpd-vz` and check host support:

```bash
dotnet run --project HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution/HPD-Execution.AppleVirtualization.Cli/HPD-Execution.AppleVirtualization.Cli.csproj -- host
```

If guest readiness fails, rerun image preparation. The guest disk probably does not contain the current guest-agent payload.

If container smoke fails, check that the selected engine socket exists inside the guest and that the smoke image/build dependency was prepared.

If endpoint acceptance fails, check that:

- the prepared image includes the current guest agent;
- `hpd-vz` was rebuilt and signed after endpoint-forwarder changes;
- the guest server starts successfully;
- endpoint release is not racing a previous failed run.

## Related Docs

- `docs/apple-virtualization/apple-virtualization-provider-handoff.md`
- `docs/apple-virtualization/real-acceptance-terminal-runbook.md`
- `docs/apple-virtualization/guest-image-contract.md`
- `src/HPD-Execution/hpd-vz/README.md`
- `src/HPD-Execution/hpd-guest-agent/README.md`
