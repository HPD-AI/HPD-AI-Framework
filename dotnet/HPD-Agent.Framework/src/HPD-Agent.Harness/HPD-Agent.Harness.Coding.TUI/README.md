# HPD-Agent.Harness.Coding.TUI

[![NuGet](https://img.shields.io/nuget/v/HPD-Agent.Harness.Coding.TUI.svg)](https://www.nuget.org/packages/HPD-Agent.Harness.Coding.TUI)

Coding-focused terminal presentation for HPD Agent. This package adds semantic
transcript rows for coding tool calls, command execution, file edits, diagnostics,
language-server status, debugging, and subagents.

Use it with `HPD-Agent.TUI` and `HPD-Agent.Harness.Coding`. It supplies presentation;
the host still owns the agent, provider, session store, workspace, and terminal.

## Install

```bash
dotnet add package HPD-Agent.Harness.Coding.TUI
```

## Compose the coding agent

```csharp
using HPD.Agent.TUI;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.TUI;

var scope = new AgentTuiRuntimeScope(agentId, sessionId, threadId);

var agent = await new AgentBuilder()
    .WithAgentId(agentId)
    .WithName("Coding agent")
    .WithProvider("openrouter", modelId)
    .WithToolHarness<CodingToolHarness>()
    .WithPermissions()
    .BuildAsync();

await using var runtime = new InMemoryAgentTuiRuntime(agent, scope);
await using var app = HpdAgentTuiApp.Create(
    runtime,
    new DirectAgentTuiExecutionTarget(scope),
    tui => tui
        .AddAgentTuiDefaults()
        .AddCodingHarnessTui()
        .SetRunConfigComposer(_ => CreateRunConfig(workspace)));

await app.RunAsync();
```

`WithPermissions()` is required when the selected coding capabilities are
permission-governed. `AddAgentTuiDefaults()` supplies the permission dialog;
`AddCodingHarnessTui()` supplies the coding presentation.

The run configuration must expose an appropriate coding workspace to the harness.
Use `AgentWorkspace.ContextKey` from `HPD.Agent.ToolHarness.Coding` and validate the
workspace before starting the TUI.

## What the TUI presents

- Collapsed and expanded coding tool activity.
- Foreground and background command lifecycle, bounded output, and exit state.
- Compact file diffs with add/remove styling.
- Language-server diagnostics, activation state, and the `/lsp` inspection page.
- Debugger lifecycle, breakpoints, stopped state, and the `/debug` inspection page.
- Subagent invocation and navigation presentation.
- Source views with line numbers, stable highlighting, and explicit truncation.

The package accepts `CodingHarnessTuiTheme` when an application needs to replace
the default coding colors. Transcript cells and renderer keys are public so an
application can provide targeted renderer replacements without rebuilding the
whole shell.

## Composition boundaries

`AddCodingHarnessTui()` is the standard entry point. The individual
`AddCoding*Tui()` extension methods are available for selective composition.

The coding TUI does not:

- create provider connections;
- choose or persist a workspace;
- install language servers or debug adapters;
- provide session durability;
- bypass permission or sandbox policy.

See the `HPD-Agent.TUI` package for terminal behavior, transcript history modes,
cancellation, queueing, model selection, and embedding details.
