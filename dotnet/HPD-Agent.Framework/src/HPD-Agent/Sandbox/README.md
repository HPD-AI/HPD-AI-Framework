# Agent sandbox

Agent-facing sandbox composition contracts, backend-neutral session
composition, capability admission, and logical workspace bindings. Host
operating-system enforcement and network mediation are owned by concrete
HPD Environment backends.

## Install

```bash
dotnet add package HPD-Agent.Framework
```

## Use When

Sandbox composition is part of HPD Agent. Add the Environment provider that your app uses.

Choose an HPD Environment package and pass its provider to the same builder
extension. For a fixed Host Process workspace:

```csharp
using HPD.Agent.Sandbox;
using HPD.Environment.Contracts;
using HPD.Environment.HostProcess;

builder.WithSandbox(new HostProcessProviderModule(), new SandboxSessionRequest
{
    Isolation = ProcessIsolationPolicy.Default,
    Workspaces = [SandboxWorkspaceRequest.HostDirectory(workspacePath)],
});
```

For per-run selection, supply a host-owned `resolveWorkspace` callback to
`WithSandbox`. The default request is optional when a resolver is configured. Set `AgentRunConfig.Workspace` to an `AgentWorkspaceReference`
with an ID that the host can authorize and resolve. The session publishes
file and process bindings together before the tools run.
