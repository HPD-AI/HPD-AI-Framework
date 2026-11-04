# HPD Environment Host Process

`HPD-Environment.HostProcess` executes sandboxed processes directly on the
current host. It owns runtime-host and execution-unit identity, operating-system
enforcement, network mediation, violation observation, process launch, and the
initial host-path content-projection behavior.

## Security posture

- macOS uses Seatbelt through `sandbox-exec` when dependency checks pass.
- Linux uses bubblewrap, namespaces, seccomp, and provider-mediated networking
  when the required kernel and executable features are available.
- Windows is not registered as a sandbox backend. It fails closed until an
  accepted filesystem and network enforcement mechanism is implemented.
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
