# HPD Agent Sandbox Architecture Proposal

This proposal replaces the earlier local-only sandbox split with a universal sandbox model that works for both direct host execution and execution environments such as Apple Virtualization, Docker, WSL, and remote workers.

The important correction is this:

```text
Sandbox core must not assume that the current .NET host is the OS where the process starts.
```

For local execution, the host and execution OS are the same. For Apple Virtualization, the host is macOS, but the process starts inside a Linux guest. A universal sandbox core must therefore describe and plan the sandbox boundary, while each backend applies that plan at the actual process boundary.

## Decision

Keep `HPD-Agent.Sandbox.Core`, but define it as the portable sandbox contract and planning package, not as a universal host-side command wrapper.

```text
HPD-Agent.Sandbox.Core
  policy language
  execution context
  portable sandbox plans
  plan serialization
  shared diagnostics/events
  common policy helpers

HPD-Agent.Sandbox.Local
  direct host process provider
  host-side sandbox plan applicator

HPD-Agent.Sandbox.AppleVirtualization
  VM host/control backend
  guest-side sandbox plan transport and applicator

HPD-Agent.Sandbox.Docker
  container lifecycle/backend
  container-side or container-config sandbox applicator

HPD-Agent.Sandbox.Wsl
  WSL backend
  WSL-side sandbox applicator
```

Core is universal because every backend consumes the same policy, plan, diagnostics, and event model. Core is not universal by physically enforcing every command from the orchestrating host process.

## Mental Model

The agent asks for a bounded process environment. Core converts that request into a portable plan. The backend applies the plan wherever the process is actually born.

```text
Agent tool / terminal / process request
        |
        v
ProcessInvocationSpec + ProcessIsolationPolicy
        |
        v
HPD-Agent.Sandbox.Core
  compile policy into portable SandboxPlanEnvelope
        |
        v
Backend-owned enforcement boundary
  Local: host process boundary
  Apple VZ: Linux guest-agent process boundary
  Docker: container process boundary
  WSL: WSL process boundary
  Remote: remote worker process boundary
        |
        v
Process, terminal, filesystem, network, mounts, artifacts
```

The backend owns enforcement location. Core owns shared meaning.

## Host vs Execution Platform

The proposal distinguishes the host platform from the execution platform.

```text
Local on macOS:
  HostPlatform      = macOS
  ExecutionPlatform = macOS
  Enforcement       = Host

Apple Virtualization running Linux:
  HostPlatform      = macOS
  ExecutionPlatform = Linux
  Enforcement       = Guest

Docker Linux container on macOS:
  HostPlatform      = macOS
  ExecutionPlatform = Linux
  Enforcement       = Container

WSL:
  HostPlatform      = Windows
  ExecutionPlatform = Linux
  Enforcement       = Guest
```

The current `PlatformDetector.Current` behavior is valid only for host-side enforcement. It must not be used as the universal answer to "what OS will run this command?"

## Core Package

`HPD-Agent.Sandbox.Core` should contain the shared concepts every backend can understand without taking a backend dependency.

Proposed layout:

```text
src/HPD-Agent.Sandbox.Core/
  Contracts/
  Policy/
  Planning/
  Serialization/
  Events/
  Diagnostics/
  Network/
  Security/
  State/
```

Core should contain:

- sandbox policy models;
- conversion from agent-level sandbox policy to execution-level `ProcessIsolationPolicy`;
- `SandboxExecutionContext`;
- `SandboxEnforcementLocation`;
- portable `SandboxPlanEnvelope`;
- target-platform-aware sandbox planning;
- plan serialization contracts for host/guest/backend transport;
- shared diagnostics, violations, and observability events;
- shared network policy and proxy policy descriptions;
- shared filesystem, environment, Unix socket, TLS, and interactive policy descriptions.

Core may contain reusable helper code for Linux, macOS, and Windows planning, but it should not require the process to be enforced on the current host.

## Proposed Core Types

```csharp
public sealed record SandboxExecutionContext
{
    public required PlatformSpec HostPlatform { get; init; }
    public required PlatformSpec ExecutionPlatform { get; init; }
    public required SandboxEnforcementLocation EnforcementLocation { get; init; }
    public ResourceScope? Scope { get; init; }
}

public enum SandboxEnforcementLocation
{
    Host,
    Guest,
    Container,
    Remote
}

public sealed record SandboxPlanEnvelope
{
    public required SchemaId SchemaId { get; init; }
    public required PlatformSpec ExecutionPlatform { get; init; }
    public required SandboxEnforcementLocation EnforcementLocation { get; init; }
    public required ProcessIsolationPolicy SourcePolicy { get; init; }
    public required PortableSandboxPlan Plan { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = Array.Empty<ProviderExtensionData>();
}
```

`PortableSandboxPlan` should be serializable and stable enough to cross process and machine boundaries. The current internal `SandboxIsolationPlan` can be the starting point, but it should become either public or converted into a public DTO shape.

## Planner vs Applicator

Core should split planning from application.

```text
Planner:
  ProcessIsolationPolicy + SandboxExecutionContext
  -> SandboxPlanEnvelope

Applicator:
  CommandInvocation + SandboxPlanEnvelope
  -> backend-specific process start behavior
```

The planner is shared. Applicators are located at the enforcement boundary.

```csharp
public interface ISandboxPlanner
{
    ValueTask<SandboxPlanEnvelope> PlanAsync(
        ProcessInvocationSpec invocation,
        SandboxExecutionContext context,
        CancellationToken cancellationToken = default);
}

public interface ISandboxApplicator
{
    ValueTask<PreparedSandboxCommand> ApplyAsync(
        CommandInvocation command,
        SandboxPlanEnvelope plan,
        CancellationToken cancellationToken = default);
}
```

The applicator interface may live in core as a contract, but implementations belong to the backend or execution location that can actually start the process.

## Local Backend

`HPD-Agent.Sandbox.Local` owns direct host execution.

For local execution:

```text
HostPlatform == ExecutionPlatform
EnforcementLocation == Host
```

The local backend should:

- register the local process provider;
- create a host-side sandbox applicator;
- apply the core sandbox plan immediately before `System.Diagnostics.Process` starts;
- keep local process events and host-specific process wiring in the local package.

The current `SandboxIsolationManager` behavior is conceptually local/host-side. It may stay in core temporarily, but the long-term shape should treat it as a host applicator used by `HPD-Agent.Sandbox.Local`, not as the universal sandbox engine.

## Apple Virtualization Backend

Apple Virtualization is a two-sided backend.

```text
macOS host:
  hpd-vz helper creates and controls the VM
  virtio socket connects host control to guest agent
  virtiofs projects approved host directories
  NAT or endpoint forwarding mediates network paths

Linux guest:
  hpd-guest-agent receives process requests
  guest-side sandbox applicator wraps the command
  subprocess starts inside the sandbox boundary
```

For Apple Virtualization:

```text
HostPlatform      = macOS
ExecutionPlatform = Linux
Enforcement       = Guest
```

The Apple Virtualization process provider should not depend on host-side wrapping. It should carry sandbox policy or a sandbox plan across the host/helper/guest boundary.

Required protocol flow:

```text
AppleVirtualizationProcessProvider
  receives ProcessInvocationSpec.Isolation
  creates or carries SandboxPlanEnvelope for Linux guest
        |
        v
hpd-vz Swift helper
  forwards Isolation/SandboxPlan to guest agent
        |
        v
hpd-guest-agent
  applies Linux sandbox plan
  starts subprocess
```

The guest can implement the applicator directly in Python for the first version, or HPD can ship a dedicated guest binary:

```text
hpd-sandbox-runner
```

The runner would accept command information plus `SandboxPlanEnvelope`, apply Linux `bwrap`/seccomp/proxy/environment/filesystem restrictions, and execute the command.

## Execution Runtime Change

The current `InMemoryExecutionRuntime` prepares process isolation globally before calling the selected process provider. That shape assumes host-side command transformation.

That behavior should change.

Old flow:

```text
ExecutionRuntime
  PrepareProcessIsolationAsync(spec)
  ProcessProvider.RunAsync(preparedSpec)
```

New flow:

```text
ExecutionRuntime
  ProcessProvider.RunAsync(spec)

ProcessProvider/backend
  chooses enforcement context
  asks core planner for portable plan
  applies plan at the process boundary
```

For local, this still means the command is wrapped before local process start. For Apple Virtualization, this means the policy travels to the guest and is applied there.

`IProcessIsolationProvider` should either be removed from the global runtime path or narrowed to host-side providers only. A backend-owned planner/applicator path is the safer universal model.

## What Breaks

This proposal intentionally breaks the earlier assumption that process isolation is a global command transformation independent of the process provider.

Expected breaking changes:

- `SandboxIsolationPlan` becomes public or is converted into public `PortableSandboxPlan`;
- `SandboxIsolationManager` becomes host-side/local applicator or is renamed to reflect that role;
- `ISandboxBackend` becomes an applicator implementation detail, not a universal backend selector;
- `PlatformDetector.Current` is no longer used to choose execution OS for every process;
- `IProcessIsolationProvider` is replaced or narrowed;
- `ExecutionRuntime.PrepareProcessIsolationAsync` is removed or gated to host-side local execution;
- `LocalProcessProvider` or `LocalSandboxMiddleware` owns host-side sandbox application;
- `AppleVirtualizationProcessStartRequest` carries `Isolation` and/or `SandboxPlanEnvelope`;
- `hpd-vz` forwards sandbox data to the guest;
- `hpd-guest-agent` applies sandbox data before process start;
- protocol and golden tests update for the new payload shape.

## Package Responsibilities

```text
HPD-Agent.Sandbox.Core
  Universal sandbox contract.
  No assumption that current host OS is the execution OS.

HPD-Agent.Sandbox.Local
  Host-side process execution and host-side sandbox application.

HPD-Agent.Sandbox.AppleVirtualization
  macOS VM orchestration plus guest-side sandbox transport/application.

HPD-Agent.Sandbox.Docker
  Container orchestration plus container-side/config-side sandbox application.

HPD-Agent.Sandbox.Wsl
  WSL orchestration plus WSL-side sandbox application.
```

## Migration Plan

1. Introduce `SandboxExecutionContext`, `SandboxEnforcementLocation`, and `SandboxPlanEnvelope` in core.
2. Convert the internal `SandboxIsolationPlan` into a public serializable plan or add a public DTO projection.
3. Add a target-platform-aware planner in core.
4. Move or rename `SandboxIsolationManager` so it is clearly host-side enforcement.
5. Change local execution so host-side sandbox application happens inside `HPD-Agent.Sandbox.Local`.
6. Remove or gate global isolation preparation in `InMemoryExecutionRuntime`.
7. Add `Isolation` and/or `SandboxPlanEnvelope` to Apple Virtualization process start protocol.
8. Forward sandbox data through `hpd-vz`.
9. Add guest-side Linux sandbox application before `hpd_guest_agent.py` starts a subprocess.
10. Update sandbox core, local, execution runtime, Apple Virtualization protocol, Swift helper, guest-agent, and golden tests.

## Why This Works

The sandbox is the stable promise:

```text
This agent acts inside a bounded environment.
```

Core makes that promise portable:

```text
Here is one policy and one plan shape every backend can understand.
```

Backends make the promise real:

```text
Here is where the process starts, so here is where the sandbox is applied.
```

This gives HPD a universal sandbox system without pretending that a macOS host process can directly enforce a Linux guest process, a Docker container process, or a remote worker process.
