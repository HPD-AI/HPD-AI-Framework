# Apple Virtualization Execution Provider

## Summary

Build `HPD.Execution.AppleVirtualization` as the first real provider for the HPD execution runtime.

The goal is to create and control a Linux microVM on macOS using Apple's Virtualization.framework, then run HPD execution units, processes, workspace projections, and eventually Docker/containerd inside that VM through the existing `HPD.Execution.Contracts` model.

This should not make HPDOS, the agent runtime, or the execution contracts macOS-specific. Apple Virtualization is the first provider implementation, not the architecture.

## Grounding

This proposal is grounded in code that already exists in this repository and in the public Apple Virtualization.framework surface. It is not a proposal to invent a new execution architecture.

Existing repo anchors:

- `src/HPD-Agent/Execution/ExecutionContracts.cs` already defines the provider-neutral resource model, including `RuntimeHost`, `ExecutionUnit`, `ProcessInvocation`, `ContentProjection`, `AuthorityBinding`, and `EngineControlPlane`.
- `src/HPD-Agent/Execution/ExecutionRuntime.cs` already has `ExecutionProviderRegistry`, `DefaultRuntimePlanner`, `InMemoryExecutionRuntime`, and `InMemoryExecutionProviderModule`.
- `src/HPD-Execution/HPD-Execution.Local/LocalProcessProvider.cs` already proves the provider-module pattern with `IProviderModule` and `IProcessProvider`.
- `src/HPD-Execution/HPD-Execution.Local/HPD-Execution.Local.csproj` already shows the packaging shape for execution providers: a separate `HPD-Execution.*` project that references `HPD-Agent`.
- `test/HPD-Agent.Tests/Execution/ExecutionContractShapeTests.cs` already tests the public contract shape.
- `test/HPD-Agent.Tests/Execution/InMemoryExecutionRuntimeTests.cs` already tests provider registration, planning, process execution, isolation preparation, projections, networking, and authority binding at the contract level.

Apple API anchors:

- Apple's `VZVirtualMachine` is the VM object that starts and manages a configured guest.
- Apple's `VZVirtualMachineConfiguration` is the configuration object for Linux and macOS guests.
- Apple requires the `com.apple.security.virtualization` entitlement to create virtual machines.
- Apple's Virtualization framework supports VIRTIO devices, including storage, networking, sockets, serial devices, and directory sharing.
- `VZVirtioFileSystemDeviceConfiguration` exposes host directories to a guest with a tag that the Linux guest mounts with `mount -t virtiofs tag directory`.
- `VZVirtioFileSystemDevice` enforces host-user file permission boundaries for shared directories.

References:

- <https://developer.apple.com/documentation/virtualization/vzvirtualmachine>
- <https://developer.apple.com/documentation/virtualization/vzvirtualmachineconfiguration>
- <https://developer.apple.com/documentation/virtualization/vzvirtiofilesystemdeviceconfiguration>
- <https://developer.apple.com/documentation/virtualization/vzvirtiofilesystemdevice>
- <https://developer.apple.com/documentation/virtualization/virtualize_linux_on_a_mac>

Therefore the first implementation should be a narrow adapter from existing HPD execution resources to a small Apple Virtualization helper. Anything outside that adapter must be justified by a failing vertical slice.

## Motivation

HPD already has a provider-neutral execution contract with typed resources for:

- `RuntimeHost`
- `ExecutionUnit`
- `ProcessInvocation`
- `ContentArtifact`
- `RootFilesystemView`
- `Workspace`
- `ContentProjection`
- `Network`
- `NetworkMembership`
- `PublishedEndpoint`
- `AuthorityBinding`
- `EngineControlPlane`

That model is already close to what a local VM orchestration layer needs. The missing piece is a concrete provider that can prove the design against a real isolated runtime.

The desired product behavior is:

```text
User selects a workspace
HPD creates a Linux microVM
HPD projects selected workspace directories into the VM
Agent commands run inside the VM
Process output and runtime state stream back to HPDOS
Docker/containerd can run inside the VM
HPDOS shows the runtime as a live work surface
```

This gives HPD a stronger isolation boundary than running commands directly on the host or talking to host Docker.

## Non-Goals

- Do not reimplement a hypervisor.
- Do not call Lima directly from HPDOS.
- Do not make HPDOS own VM lifecycle.
- Do not require Docker for the first milestone.
- Do not support Linux or Windows hosts in the first implementation.
- Do not redesign `ExecutionContracts.cs` before the first provider slice proves what is missing.
- Do not retrofit VM state into the existing chat/tool event stream. Execution resources should have their own event/resource stream.

## Architecture

```text
HPDOS
  observes execution resources and events
  sends user commands through HPD agent/runtime APIs

HPD Agent / Runtime
  uses IExecutionRuntime
  plans and owns execution resources

HPD.Execution.Contracts
  provider-neutral resource model

HPD.Execution.AppleVirtualization
  implements provider interfaces
  controls Apple Virtualization.framework through a small native bridge

Apple Virtualization.framework
  boots and runs Linux VM
```

The provider should sit behind the existing facade:

```csharp
public interface IExecutionRuntime
{
    ValueTask<RuntimePlan> PlanAsync(...);
    ValueTask<ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus>> EnsureHostAsync(...);
    ValueTask<ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus>> EnsureExecutionUnitAsync(...);
    ValueTask<IProcessInvocationHandle> StartProcessAsync(...);
    ValueTask<ProcessInvocationResult> RunProcessAsync(...);
    ValueTask<RuntimeFinalizationResult> FinalizeRuntimeAsync(...);
}
```

## Provider Boundary

Create a new provider project:

```text
HPD-AI-Framework/dotnet/HPD-Agent.Framework/src/HPD-Execution.AppleVirtualization
```

This follows the existing `src/HPD-Execution/HPD-Execution.Local` package layout instead of placing provider code under `src/HPD-Agent`. The core `HPD-Agent` project keeps the contracts and runtime facade. Provider projects reference `HPD-Agent`.

The initial provider should implement only the interfaces needed for the first vertical slice:

- `IProviderModule`
- `IRuntimeHostProvider`
- `IExecutionUnitProvider`
- `IProcessProvider`
- `IContentProjectionProvider`

Later phases can add:

- `IRootFilesystemProvider`
- `INetworkProvider`
- `INetworkMembershipProvider`
- `IEndpointPublicationProvider`
- `IAuthorityBindingProvider`
- engine control-plane support for Docker/containerd

## Native Bridge

Virtualization.framework is Apple-native, so the first implementation should use a small Swift helper process rather than trying to bind every Apple API directly into .NET.

The helper is an implementation detail of `HPD-Execution.AppleVirtualization`. HPDOS must never shell out to it directly.

Proposed helper:

```text
hpd-vz
  host create
  host start
  host stop
  host status
  exec start
  exec run
  exec signal
  exec resize
  projection mount
  endpoint publish
```

.NET communicates with `hpd-vz` using a stable JSON protocol over stdio or a Unix domain socket.

Grounded first choice: use stdio for the fake-helper tests and first process model. Move to a Unix domain socket only if long-lived streaming and lifecycle control become awkward over stdio.

This keeps the boundary simple:

```text
.NET provider
  maps HPD contracts to provider protocol

hpd-vz
  maps provider protocol to Virtualization.framework
```

## Resource Mapping

This mapping is the contract between HPD resources and the Apple provider. It is also the line that prevents this project from becoming generic VM management.

### RuntimeHost

`RuntimeHost` maps to the Linux microVM.

Responsible for:

- VM disk/image selection
- CPU and memory sizing
- boot lifecycle
- guest readiness
- guest control endpoint
- host stop/reset/delete

Relevant contract types:

- `RuntimeHostSpec`
- `RuntimeHostBootstrapSpec`
- `RuntimeHostStorageSpec`
- `RuntimeHostStatus`
- `RuntimeHostPhase`

Initial Apple mapping:

- `RuntimeHostSpec.Platform` maps to the guest OS and architecture supported by the host Mac.
- `RuntimeHostSpec.Capacity` maps to virtual CPU count, memory size, and disk size.
- `RuntimeHostBootstrapSpec` is initially limited to boot image selection, readiness probes, and optional guest-control bootstrap data.
- `RuntimeHostStatus.GuestControl` reports whether SSH or the guest agent is reachable.
- `RuntimeHostStatus.Readiness` reports the first successful guest-control probe.

### ExecutionUnit

`ExecutionUnit` maps to a per-session or per-task environment inside the VM.

For the first slice, this can be lightweight: a guest working directory plus environment metadata. Later it can become a stronger namespace or container boundary inside the VM.

Relevant contract types:

- `ExecutionUnitSpec`
- `ExecutionUnitStatus`
- `ExecutionUnitPhase`
- `ExecutionUnitIdentitySpec`
- `ExecutionUnitNetworkSpec`

Initial Apple mapping:

- one `RuntimeHost` can contain one or more lightweight `ExecutionUnit` working directories.
- an `ExecutionUnit` is not a VM, container, or process.
- the first implementation may realize an `ExecutionUnit` as a directory plus environment metadata.
- stronger isolation inside the guest is deferred until after boot, projection, and exec work.

### ContentProjection

`ContentProjection` maps selected host workspace directories into the guest.

Initial implementation can choose the simplest working projection:

- `VZVirtioFileSystemDeviceConfiguration` with a `VZSingleDirectoryShare` or `VZMultipleDirectoryShare`
- Linux guest mount using `mount -t virtiofs tag directory`
- copy-in/copy-out fallback

The provider should report the actual realization through `RealizedProjectionView`.

Relevant contract types:

- `Workspace`
- `ContentProjectionSpec`
- `ContentProjectionStatus`
- `ProjectionRealizationKind`
- `ProjectionWriteEffect`
- `CoherenceClass`
- `SyncPolicy`
- `FinalizationResult`

Initial Apple mapping:

- read-write projections must be opt-in at the HPD policy level.
- actual coherence is reported; do not claim strong coherence unless it is proven.
- if virtiofs mounting requires guest setup, that setup is part of guest readiness.
- if file sharing cannot satisfy a requested projection, fall back to copy-in/copy-out and report the fallback.

### ProcessInvocation

`ProcessInvocation` maps to a command executed inside the guest.

The first implementation can use a guest agent or SSH. A guest agent is preferable long-term because HPD needs structured output, process lifecycle, terminal resizing, and cancellation.

Relevant contract types:

- `ProcessInvocationSpec`
- `ProcessCommandSpec`
- `ProcessIoSpec`
- `IProcessInvocationHandle`
- `ProcessOutputChunk`
- `ProcessInvocationResult`

Initial Apple mapping:

- command execution does not use the host shell.
- `ProcessCommandSpec` is serialized to the guest-control channel.
- stdout/stderr are streamed back as `ProcessOutputChunk`.
- cancellation maps to a guest-side process stop request first, then force termination if supported.
- `ProcessInvocationResult` is the source of truth for exit code, timeout, truncation, and output-drain behavior.

### EngineControlPlane

`EngineControlPlane` maps to Docker/containerd running inside the VM.

This is not part of the first milestone. The first milestone should prove boot, projection, exec, and streaming. Then Docker/containerd can be installed and exposed as a controlled engine resource.

Relevant contract types:

- `EngineControlPlaneSpec`
- `EngineControlPlaneStatus`
- `EngineControlPlaneKind`
- `EngineAuthorityMode`
- `EngineApiEndpointStatus`

## First Vertical Slice

The first useful demo should be:

```text
1. Add `HPD-Execution.AppleVirtualization.csproj`.
2. Add `AppleVirtualizationProviderModule`.
3. Add a fake `hpd-vz` transport for tests.
4. Register provider descriptors for `RuntimeHost`, `ExecutionUnit`, `ProcessInvocation`, and `ContentProjection`.
5. Ensure a `RuntimeHost` from a known local Linux image path.
6. Start the VM through the helper.
7. Wait until guest control is ready.
8. Project one host workspace directory into the guest.
9. Ensure an `ExecutionUnit`.
10. Run `uname -a`.
11. Run `pwd` and `ls`.
12. Stream stdout/stderr as `ProcessOutputChunk`.
13. Return `ProcessInvocationResult`.
14. Show host/unit/projection/process state through execution resources.
```

No Docker yet. No port publishing yet. No snapshots yet.

Acceptance criteria:

- tests can run against a fake helper without macOS virtualization.
- provider registration is visible through `ExecutionProviderRegistry`.
- planner can select the provider for the first required contract set.
- fake-helper tests prove request/response protocol shape before real Swift work.
- macOS integration test can be manually enabled and skipped elsewhere.
- a failed VM start returns a degraded or failed `RuntimeHostStatus` with a diagnostic, not an unstructured exception.
- a failed command returns `ProcessInvocationResult` with `CompletionKind` and captured stderr.
- content projection reports the effective realization and write effect.

## Docker/Containerd Phase

After the vertical slice works:

```text
1. Add bootstrap support for a container runtime guest component.
2. Start Docker or containerd inside the guest.
3. Surface it as an EngineControlPlane.
4. Publish the engine endpoint only through explicit AuthorityBinding or EndpointPublication policy.
5. Run a simple container inside the VM.
```

Important invariant:

```text
Docker is not the HPD isolation boundary.
The VM is the isolation boundary.
Docker/containerd is a workload engine inside that boundary.
```

## HPDOS Integration

HPDOS should not call Apple Virtualization or `hpd-vz` directly.

HPDOS should observe HPD execution state:

- runtime host phase
- execution unit phase
- workspace projection status
- active process list
- process output
- published endpoints
- engine status

The active-session main canvas can become a live execution dashboard:

```text
Runtime Host
  Starting / Ready / Degraded

Workspace Projection
  Mounted / Syncing / Conflicted

Processes
  Running / Exited / Failed

Engine
  Docker Ready / Not Installed / Degraded

Endpoints
  localhost ports and services
```

The conversation rail remains the agent narrative. The main canvas becomes the actual work surface.

## Risks

### Ungrounded platform assumptions

The proposal fails if it assumes Apple Virtualization behaves like Docker, Lima, or a general-purpose cloud API.

Mitigation:

- keep every Apple-specific assumption behind the helper protocol
- write fake-helper tests first
- require a short manual proof for each Apple feature before modeling it as supported
- report unsupported features through `UnsupportedReason`, `Condition`, and provider limitations

### Native bridge complexity

Virtualization.framework is Apple-native. A Swift helper is the lowest-risk bridge, but it introduces packaging and protocol concerns.

Mitigation:

- keep the helper small
- use explicit JSON messages
- version the protocol
- write provider tests against a fake helper

### Guest image management

Boot images, disk resizing, and guest provisioning can become a project by themselves.

Mitigation:

- start with one known Linux image path
- add image resolution/import later
- keep `ContentArtifact` mapping minimal until the vertical slice works

### File projection semantics

Live file sharing and bidirectional sync can have subtle behavior.

Mitigation:

- report actual coherence and write effects
- start with one safe projection mode
- allow copy-in/copy-out fallback

### Entitlements and distribution

Creating VMs requires the Apple virtualization entitlement. Local development, test execution, signed helper distribution, and packaged HPDOS use may have different entitlement constraints.

Mitigation:

- keep the .NET provider testable without the entitlement
- make the helper report entitlement and host capability failures explicitly
- document the signing and entitlement path before shipping the provider
- do not block contract-level tests on a real VM

### Docker bootstrap

Installing and running Docker inside the guest is useful but not necessary for proving the VM provider.

Mitigation:

- delay Docker until after process execution is reliable
- model Docker as `EngineControlPlane`, not as host setup magic

## Open Questions

- Guest control: SSH first for speed, or custom HPD guest agent first for correct streaming and cancellation?
- Linux image: which local image format and path become the initial blessed development image?
- VM lifetime: one VM per workspace, per active session, or per runtime scope?
- Idle retention: what default keeps iteration fast without hiding stale state?
- Workspace writes: read-write mount by default, or staged writes with explicit finalization?
- HPDOS dashboard: which resource states are required for the first useful UI?
- Helper protocol: JSON over stdio only for milestone one, or stdio for tests and Unix socket for real VM sessions?
- Entitlements: how is `hpd-vz` signed and launched in development versus packaged builds?

## Proposed Decision

Proceed with a macOS-first Apple Virtualization provider.

Do it as a provider behind `HPD.Execution.Contracts`, not as HPDOS-specific VM code.

Grounded first implementation:

```text
HPD-Execution.AppleVirtualization
  .NET provider package
  maps HPD contracts to helper protocol
  fully testable against fake helper

hpd-vz
  Swift helper
  owns Virtualization.framework calls
  reports structured status, errors, and events
```

First milestone:

```text
boot Linux VM
project workspace
run command
stream output
show execution state
```

Second milestone:

```text
bootstrap Docker/containerd inside VM
surface it as EngineControlPlane
publish explicit endpoints
```

This gives HPD a real isolated execution substrate while preserving the provider-neutral architecture for future Linux, Windows, remote, Lima, Firecracker, or container-based providers.
