# HPD-Agent.ToolHarness.FileSystem (Deprecated)

> **Deprecated.** This package is retained only for migration and does not
> receive new features. It is not a supported workspace-isolation or
> command-execution security boundary.

The public `FileSystemTools` and `FileSystemContext` types produce compiler
warning `CS0618`. Existing applications may continue to build during the
migration window, but new applications should not adopt this package.

The deprecated harness has known limitations around physical path containment,
symlink traversal, and shell allowlists. Treating it as a sandbox can expose
files outside the configured workspace. Do not rely on `EnableShell`,
`AllowOutsideWorkspace`, or the command allow/block lists for security.

## Migration

Choose the replacement based on the trust boundary:

| Existing use | Replacement |
| --- | --- |
| Trusted local coding workspace | [`HPD-Agent.ToolHarness.Coding`](../HPD-Agent.Harness.Coding/README.md) |
| Untrusted commands, files, or generated code | A sandbox runtime such as [`HPD-Agent.Sandbox.HostProcess`](../../HPD-Agent.Sandbox/HPD-Agent.Sandbox.HostProcess/README.md) or [`HPD-Agent.Sandbox.HostContainers`](../../HPD-Agent.Sandbox/HPD-Agent.Sandbox.HostContainers/README.md) |
| Application-specific file access | An application-owned ToolHarness over an explicitly governed storage or workspace service |

For trusted coding tasks, install and register the coding harness:

```bash
dotnet add package HPD-Agent.ToolHarness.Coding
```

```csharp
using HPD.Agent.ToolHarness.Coding;

await using var agent = await new AgentBuilder(agentConfig)
    .WithToolHarness<CodingToolHarness>(options =>
        options.Configure(new CodingHarnessConfig()))
    .BuildAsync();
```

Use the coding harness with an explicit workspace binding. Do not expose host
paths or host command execution to untrusted users or generated code without
placing the work in an appropriate sandbox.

See [DEPRECATION.md](DEPRECATION.md) for the deprecation policy, security
rationale, migration checklist, and package retirement process.
