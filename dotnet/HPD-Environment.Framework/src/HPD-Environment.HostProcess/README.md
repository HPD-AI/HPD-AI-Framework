# HPD Environment Host Process

`HPD-Environment.HostProcess` executes sandboxed processes directly on the
current host. It owns runtime-host and execution-unit identity, operating-system
enforcement, network mediation, violation observation, process launch, and the
initial host-path content-projection behavior.

## Agent workspace

```csharp
using HPD.Agent.Sandbox;
using HPD.Environment.HostProcess;

builder.WithSandbox(
    new HostProcessProviderModule(),
    HostProcessSandbox.ForDirectory(workspacePath));
```

The factory exposes one live host directory. Pass `readOnly: true` for a
workspace that file tools and sandboxed processes can read.

## Security posture

- macOS uses Seatbelt through `sandbox-exec` when dependency checks pass.
- Linux uses bubblewrap, namespaces, seccomp, and provider-mediated networking
  when the required kernel and executable features are available.
- Windows supports a restricted AppContainer command profile when the requested
  policy can be enforced: network egress must be blocked, filesystem rules and
  authority bindings must be empty, TLS interception and interactive features
  must be disabled, and the process violation action must be `ObserveOnly`.
  Unsupported policies fail capability admission unless provider-degraded
  execution is explicitly allowed; degraded execution has no OS filesystem,
  network, or process-tree confinement.
- Windows workspace access is currently limited. The provider can provision
  live, disjoint host-directory roots with read-only or read/write ACL grants
  for an AppContainer profile, but the current capability refinement does not
  admit sessions that require workspace bindings. Such requests fail closed.
  Workspace grants are released when the provider session is disposed.
- Agent permissions remain a separate decision layer. This provider enforces
  the compiled Environment isolation plan and does not inspect Agent prompts,
  tools, or conversation state.
- A host-path projection cannot be remapped to a different destination path.
  Writable host-path projections require explicit direct-source mutation
  authorization.

## Provider identity

```text
hpd.execution.host-process
```

The provider supports the common `RuntimeHost`, `ExecutionUnit`,
`ProcessInvocation`, `ProcessIsolation`, and `ContentProjection` contracts.
Feature support is reported through `ProviderCapabilityReport`; required
unsupported capabilities cause Agent Sandbox admission to fail before session
creation.

## Lifecycle

1. Ensure the current host as a logical `RuntimeHost`.
2. Ensure a logical `ExecutionUnit` attached to that host.
3. Compile and apply the requested isolation policy when a process starts.
4. Return the process handle and bounded output.
5. Stop and delete processes, projections, the execution unit, and the host in
   dependency order.
