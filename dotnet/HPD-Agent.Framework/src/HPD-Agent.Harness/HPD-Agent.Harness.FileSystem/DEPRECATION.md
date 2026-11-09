# FileSystem ToolHarness deprecation

## Status

`HPD-Agent.ToolHarness.FileSystem` is deprecated as of HPD-Agent 1.0.0.

- No new features or general fixes will be added.
- Existing consumers receive compiler warning `CS0618` from
  `FileSystemTools` and `FileSystemContext`.
- The package remains available for a migration window so existing
  applications can upgrade without an immediate build break.
- The package is eligible for removal no earlier than the next major release.
- New applications must use the coding harness or a sandbox-backed
  implementation.

## Why it is deprecated

The package's API suggests stronger isolation than it provides:

- Workspace checks compare path prefixes and do not establish physical path
  containment.
- Symlinks can resolve outside the configured workspace.
- The shell command allow list checks the first command token while the full
  command is interpreted by a platform shell.
- File operations and shell execution are not governed by one consistent
  sandbox policy.

Calling `WithToolHarness<FileSystemTools>()` does not create a secure sandbox.
The permission system can still require approval for selected operations, but
approval does not convert this harness into an isolation boundary.

## Migration

### Trusted workspace tasks

Use `HPD-Agent.ToolHarness.Coding` for code reading, editing, searching, and
trusted command execution. The coding harness uses explicit workspace bindings
and treats absolute execution paths separately from model-facing workspace
addresses.

```csharp
using HPD.Agent.ToolHarness.Coding;

await using var agent = await new AgentBuilder(agentConfig)
    .WithToolHarness<CodingToolHarness>(options =>
        options.Configure(new CodingHarnessConfig()))
    .BuildAsync();
```

See the [coding harness guide](../HPD-Agent.Harness.Coding/README.md) for
workspace, command, language-server, and TUI configuration.

### Untrusted workloads

Use an HPD sandbox backend for code that is not trusted with the host
filesystem or network:

- [`HPD-Agent.Sandbox.HostProcess`](../../HPD-Agent.Sandbox/HPD-Agent.Sandbox.HostProcess/README.md)
  for backend-neutral process isolation.
- [`HPD-Agent.Sandbox.HostContainers`](../../HPD-Agent.Sandbox/HPD-Agent.Sandbox.HostContainers/README.md)
  for container-backed execution and workspace projection.

The application must provision only the workspace roots and process
capabilities required by the task.

### Application-specific storage

If the application needs a narrow file API, define an application-owned
ToolHarness over an explicitly governed storage service. Validate canonical
paths, resolve symlinks where appropriate, keep authorization outside the
model arguments, and expose only the operations the application requires.

## Migration checklist

1. Remove `HPD-Agent.ToolHarness.FileSystem` from package references.
2. Replace `WithToolHarness<FileSystemTools>()` with the coding harness or an
   application-owned ToolHarness.
3. Move untrusted workspace and command execution into a sandbox.
4. Replace host-absolute-path arguments with workspace-bound model-facing
   addresses where supported.
5. Rework permissions so they protect the replacement's actual authority and
   not a legacy allow/block list.
6. Remove project-wide `CS0618` suppression. Suppress it only temporarily at a
   specific migration call site when necessary.

## Package retirement

Publishing a new package build does not automatically mark it Deprecated on
nuget.org. After publishing the deprecation release, maintainers must mark
`HPD-Agent.ToolHarness.FileSystem` as deprecated in the NuGet package
administration UI, set `HPD-Agent.ToolHarness.Coding` as the replacement, and
reference this document in the deprecation message.
