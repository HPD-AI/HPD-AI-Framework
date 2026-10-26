# HPD Agent TUI

HPD Agent TUI is the terminal shell for HPD Agent conversations. It provides a
streaming transcript, prompt and command surface, interaction dialogs, permission
prompts, and session/thread navigation. Applications compose the features they need
instead of using a fixed, closed chat screen.

This package does not create a model connection or decide how conversations are
stored. The host supplies an `IHpdAgentTuiRuntime`; the TUI turns that runtime's
events into a terminal experience.

**Start here:** [try the test console](#try-the-test-console) ·
[learn the controls](#using-the-interface) ·
[embed the TUI](#embedding-the-tui) ·
[extend the shell](#extending-the-shell) ·
[troubleshoot](#troubleshooting)

## Where it fits

The UI is split into independent packages so an agent application can use the
terminal foundation without pulling in coding-specific presentation. HPD Agent TUI
depends on HPD Agent runtime abstractions; it does not own provider credentials,
backend lifecycle, or application persistence.

| Package | Purpose |
| --- | --- |
| `HPD-TUI` | General-purpose terminal rendering, components, forms, and dialogs |
| `HPD-Agent.TUI` | Agent chat shell, commands, transcript projection, and interactions |
| `HPD-Agent.Harness.Coding.TUI` | Coding-specific tool, command, edit, diagnostic, LSP, and debugger presentation |

Use `HPD-Agent.TUI` for an agent chat surface. Add
`HPD-Agent.Harness.Coding.TUI` when the agent uses the HPD coding tool harness.

## Requirements

- .NET 10 or later to build the framework source.
- An interactive terminal. Redirected stdin/stdout is not supported for the chat UI.
- A terminal with reliable ANSI input handling. The bundled test console is designed
  for xterm, iTerm2, Windows Terminal, or WezTerm.
- A runtime that can resolve a target, submit input, observe events, answer
  interaction requests, and report thread state.

The default `Viewport` history mode has the widest compatibility.
`TerminalScrollback` mode additionally requires terminal split-footer support and
throws a descriptive `NotSupportedException` when the detected terminal cannot
provide it.

## Try the test console

The framework repository includes a console harness for evaluating the UI without
writing a host first. Run these commands from `dotnet/HPD-Agent.Framework`.

### Interactive demo without credentials

```bash
dotnet run --project test/HPD-Agent.TUI.Console -- demo
```

The demo uses a fake runtime and streams a sample answer and tool call. It does not
contact a provider. Its composition also demonstrates a custom command,
autocomplete provider, widget, footer item, and keyboard shortcut.

Type a message and press Enter. Use `Ctrl+Escape` to exit the bundled console.

### Chat with a real provider

The `direct` and `coding` modes use OpenRouter and read
`OPENROUTER_API_KEY` from the environment or user secrets.

```bash
export OPENROUTER_API_KEY="..."

dotnet run --project test/HPD-Agent.TUI.Console -- direct \
  --agent tui-direct-agent \
  --session local-session \
  --thread main \
  --model qwen/qwen3.7-plus
```

To evaluate coding presentation:

```bash
dotnet run --project test/HPD-Agent.TUI.Console -- coding \
  --workspace /absolute/path/to/repository \
  --model qwen/qwen3.7-plus
```

The `coding` test mode uses `InMemoryAgentTuiRuntime`. Its conversation is intended
for UI evaluation and may disappear when the process exits. Use a host backed by a
durable session store for real work.

To run a persisted local server and TUI client together:

```bash
dotnet run --project test/HPD-Agent.TUI.Console -- server \
  --data ./.hpd-tui-console \
  --session local-session
```

Common test-console options:

| Option | Applies to | Meaning |
| --- | --- | --- |
| `--agent ID` | direct, coding, server | Agent identifier |
| `--session ID` | direct, coding, server | Session identifier |
| `--thread ID` | direct, coding, server | Thread identifier |
| `--model ID` | direct, coding, server | OpenRouter model identifier |
| `--workspace PATH` | coding | Repository exposed to the coding harness |
| `--url URL` | server | Local server address; defaults to `http://127.0.0.1:5057` |
| `--data PATH` | server | Durable server data directory |
| `--list-tools` | coding | Print the coding tools and exit without starting the TUI |

## Using the interface

The normal screen contains a header, transcript, prompt, and status line. Command
and widget availability depends on what the host contributed.

### Keyboard

| Key | Behavior |
| --- | --- |
| `Enter` | Submit the prompt, or accept the selected autocomplete item |
| `Shift+Enter` | Insert a newline when the prompt is configured as multiline |
| `Up` / `Down` | Navigate prompt history, or autocomplete choices while suggestions are open |
| `Left` / `Right` / `Home` / `End` | Move within the prompt |
| `Backspace` / `Delete` | Delete before or after the cursor |
| `/` | Start slash-command autocomplete |
| `Tab` / `Shift+Tab` | Accept autocomplete, or move focus through registered widgets |
| `Page Up` / `Page Down` | Scroll a viewport-based transcript |
| `Alt+Up` | Restore the newest queued follow-up when the prompt is empty |
| `Esc` | Dismiss autocomplete, leave a page/dialog, steer a queued follow-up, or cancel an active execution |
| `Ctrl+Escape` | Exit in the bundled test console |

Exit and cancellation shortcuts are host-owned. The host may add another shutdown
gesture, but it should label that gesture in the prompt status so users do not have
to infer it.

Focus traversal currently advances in registration order for both Tab and
Shift+Tab, then returns to the prompt after the final widget.

Pressing Escape once during an execution arms cancellation and shows a confirmation
in the status line. Press Escape again within two seconds to cancel. When a queued
follow-up exists, Escape steers that follow-up instead; the queued-message preview
shows the available edit and steer actions.

### Sandbox capability requests

An enabled sandbox can block a tool operation that needs file access, network
egress, a local port, an interactive terminal, or unsandboxed execution outside
its current grants. The coding TUI presents an `AgentCapabilityRequestEvent` with
the request ID, capability, resource, and reason.

For a request tied to an active agent turn, the dialog offers both:

- `Allow once` — approve only the current operation.
- `Allow all files this turn` or `Allow all similar requests this turn` — suppress
  further prompts in that capability family until the current
  `ThreadExecutionId` finishes.

The broader choice is deliberately bounded to one agent turn, one agent/session/
thread scope, and the same capability family. It does not become a persistent
security rule. File reads and writes share the file family; network, local binding,
interactive terminal, and unsandboxed execution remain separate. Every access is
still checked and recorded after approval.

### Commands

Type `/` to open command autocomplete, or `/help` to view every visible command
registered by the current composition.

The default shell commands are:

- `/help` — show available commands.
- `/clear` — clear the visible transcript.

Feature packages add more commands. Common Agent TUI commands include:

- `/model` — choose the provider and model after `AddModelSelection` is configured.
- `/questions` — reopen questions minimized while the agent was running.
- `/subagents` — open durable subagent conversations after `AddSubAgentTui`.

Coding extensions add `/lsp` for language-server status and `/debug` for debugger
state. The test console additionally demonstrates `/agents`, `/sessions`, `/thread`,
`/providers`, `/provider`, and `/status`.

Some test-console commands accept typed subcommands. Run the command without
arguments to open its picker. If a typed form is required, the command prints its
usage. For example:

```text
/sessions switch <sessionId>
/thread switch <threadId>
/agents switch <agentId> [sessionId] [threadId]
```

## Embedding the TUI

Add the package and define how the host builds an agent:

```bash
dotnet add package HPD-Agent.TUI
```

The smallest useful composition is:

```csharp
using HPD.Agent.TUI;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Runtime;

var scope = new AgentTuiRuntimeScope(
    agentId: "support-agent",
    sessionId: "local-session",
    threadId: "main");

// CreateAgent() is application-owned. Configure its provider/model before
// constructing the runtime.
await using var runtime = new InMemoryAgentTuiRuntime(CreateAgent(), scope);
await using var app = HpdAgentTuiApp.Create(
    runtime,
    new DirectAgentTuiExecutionTarget(scope),
    tui => tui.AddAgentTuiDefaults());

await app.RunAsync();
```

`AddAgentTuiDefaults()` installs the standard shell, prompt, transcript renderers,
slash-command autocomplete, `/help`, `/clear`, and the default permission-request
handler. It does not install a provider picker, questions, subagents, or coding
presentation; those are explicit contributions.

`InMemoryAgentTuiRuntime` is useful for a single in-process agent. For durable,
multi-client, or server-hosted conversations, implement `IHpdAgentTuiRuntime` or
use `HostedAgentTuiRuntime`.

A durable host should generally:

1. Resolve an `AgentTuiExecutionTarget`.
2. Initialize or resume the session and thread.
3. Hydrate existing events and supplies stable journal cursors in `ObserveAsync`.
4. Forward submissions, cancellations, and interaction answers to the agent.
5. Report active execution and cursor state from `GetThreadStateAsync`.

The runtime is the source of truth. The shell is a projection that can be rebuilt
from durable state after a reconnect or process restart.

### Enable multiline input

The default prompt is single-line. Replace the prompt factory when the application
expects long coding requests:

```csharp
tui => tui
    .AddAgentTuiDefaults()
    .ReplacePrompt(new DefaultAgentTuiPromptFactory
    {
        Placeholder = "Describe the change...",
        Multiline = true
    })
```

With multiline enabled, Enter submits and Shift+Enter inserts a newline. Pasted text
is retained as one removable prompt block and expanded to its full content on
submission.

### Add model selection

Provide an `IAgentTuiModelCatalog` that exposes the application's provider
connections and models:

```csharp
tui => tui
    .AddAgentTuiDefaults()
    .AddModelSelection(modelCatalog, modelSelection)
```

The selected provider/model state can be used by the host's `AgentTuiRunConfigComposer`
to construct each run. A host should validate that the connection is registered,
authenticated, unexpired, and capable of the requested tool use before accepting a
selection.

### Add questions and subagents

```csharp
tui => tui
    .AddAgentTuiDefaults()
    .AddQuestionInteraction()
    .AddSubAgentTui()
```

Question handling preserves draft answers and provides `/questions` for minimized
requests. Subagent support adds conversation navigation and scoped runtime access.

### Add coding presentation

```csharp
using HPD.Agent.ToolHarness.Coding.TUI;

tui => tui
    .AddAgentTuiDefaults()
    .AddCodingHarnessTui()
```

The coding contribution renders tool exploration, command lifecycle and output,
file mutations, language-server diagnostics/status, and debugger state. It also adds
the `/lsp` and `/debug` inspection pages. The agent still needs the coding tool
harness and an appropriate workspace in its run configuration.

## Extending the shell

`HpdAgentTuiBuilder` is the composition root. Contributions are registered by key
and normally reject duplicates so two packages cannot silently replace each other.

| Capability | Builder API |
| --- | --- |
| Slash command | `AddSlashCommand` / `TryAddSlashCommand` |
| Full-screen page | `AddPage` / `TryAddPage` |
| Widget above or below prompt | `AddWidget` |
| Footer item | `AddFooterItem` |
| Autocomplete provider | `AddAutocompleteProvider` |
| Keyboard shortcut | `AddShortcut` |
| Runtime event projection | `AddEventHandler` |
| Transcript cell renderer | `AddTranscriptRenderer` |
| Human interaction handler | `AddInteractionHandler` |

Use `Add*` when a missing or duplicate contribution is an integration error. Use
`TryAdd*` when a feature may already have been supplied by another composition.

### Add a command

```csharp
tui.AddSlashCommand(new HpdAgentTuiCommandDescriptor(
    "review",
    context => RunReviewAsync(context))
{
    Title = "/review",
    Description = "Review the current changes.",
    Order = 200
});
```

Command handlers should append semantic transcript entries or open a page/dialog.
Avoid writing directly to the terminal; direct writes bypass redraw, resizing,
scrollback recovery, and the presentation model.

### Project live state

Event handlers should be deterministic projections from runtime events into shell
state. Use a stable `EntryKey` when subsequent events update the same visible row,
and unique IDs for one-shot transcript entries. Include agent, session, and thread
metadata so replayed events remain attributable to the correct scope.

Long streams should update an existing live entry rather than append one entry per
chunk. Final transcript entries should be committed according to the host's
history policy so native terminal scrollback remains immutable.

## Transcript and terminal behavior

The transcript supports two history presentations:

- `Viewport` renders a fixed tail and uses Page Up/Page Down for older rows.
- `TerminalScrollback` publishes accepted rows into the terminal's own history.

Choose the presentation during builder configuration:

```csharp
tui.UseTranscriptHistoryPresentation(TranscriptHistoryPresentation.Viewport);
```

The default is `Viewport`. Native scrollback requires a compatible terminal and is
best when terminal selection, search, and the terminal's normal scroll controls are
important to users.

The UI handles terminal resizing and presentation recovery, but renderers must
respect the width supplied by the layout contract. Do not cache terminal escape
sequences or assume a fixed width inside custom components.

## Styling

Markdown colors are independent of the shell's UI theme. Register immutable palettes
when building the application:

```csharp
using HPD.TUI.Core;
using HPD.TUI.Markdown;

var ui = Theme.Default;
var markdown = MarkdownTheme.FromTheme(ui) with
{
    Heading1 = new Style(new Color(190, 140, 250), Color.Default, TextAttributes.Bold),
    Link = new Style(new Color(70, 200, 240), Color.Default, TextAttributes.Underline),
    InlineCode = new Style(new Color(240, 190, 90), Color.Default),
    Strong = new MarkdownInlineStyle { Attributes = TextAttributes.Bold },
    Syntax = CodeSyntaxTheme.FromTheme(ui) with
    {
        Keyword = new Style(new Color(190, 140, 250), Color.Default),
        Comment = new Style(new Color(125, 140, 155), Color.Default, TextAttributes.Italic)
    }
};
var reasoning = markdown with { Body = ui.Border, Heading1 = ui.Border };

tui.UseTheme(ui)
    .UseMarkdownTheme(markdown)
    .UseReasoningMarkdownTheme(reasoning);
```

`MarkdownTheme` supplies separate styles for body text, headings, links, inline
code, fenced code, quotes, lists, tasks, tables, thematic breaks, images, and raw
HTML. `Syntax` supplies token styles for text, identifiers, keywords, strings,
numbers, functions, types, operators, punctuation, and comments.

Strong, emphasis, and strikethrough use `MarkdownInlineStyle`. Null colors inherit
from the enclosing heading, link, or body; explicit colors override them. The
built-in highlighter is lexical rather than compiler-backed, so unconventional
names can be classified differently from an IDE.

Without explicit palettes, responses derive defaults from the UI theme and reasoning
uses muted defaults. A custom transcript renderer receives the resolved
`MarkdownTheme` through its render context.

Palettes are fixed for a built registry. Changing them requires constructing a new
presentation and replaying displayed history; they are not a live recoloring API.

### Catalog reasoning constraints

`AgentTuiModelCapabilities.SupportedReasoningEfforts` preserves raw catalog levels
and `DefaultReasoningEffort` identifies the advertised default. The picker intersects
known levels (`none`, `low`, `medium`, `high`, `xhigh`) with the implementation;
unknown values remain metadata. Choosing the server default leaves reasoning
unspecified.

A catalog can supply `AgentTuiModelChoice.ProviderConfig` for model-specific
construction constraints. Selection carries that payload into normal provider
resolution while retaining the selected connection identity.

## Troubleshooting

### The application exits immediately

The chat UI needs an interactive stdin and stdout. Run it from a terminal, not a
captured subprocess, CI job, or HTTP request. Check that the host's runtime resolves
an initial target before the screen starts.

### It throws `NotSupportedException` for native chat

`TerminalScrollback` mode requires terminal split-footer support. Use a supported
terminal or switch the composition to `TranscriptHistoryPresentation.Viewport`.

### Slash commands do not appear

Type `/` as the first character of the prompt. `/help` lists visible commands only;
commands added by another package or host integration must be part of the same
builder composition.

### Enter submits instead of adding a line

Single-line is the default. Replace the prompt with
`DefaultAgentTuiPromptFactory { Multiline = true }`. Shift+Enter inserts a newline
only in multiline mode.

### Messages run with the wrong model or configuration

The TUI does not create provider credentials. Ensure the host uses the selected
model state in its run configuration and that the selected provider connection is
registered and authenticated.

### Session or thread commands are missing

Those commands require a runtime that implements `IAgentTuiSessionThreadRuntime`.
An `IHpdAgentTuiRuntime` can support chat without supporting session, thread, agent,
or subagent management.

### History does not appear after reconnecting

Verify that `ObserveAsync` starts from the requested cursor and emits stable,
ordered journal cursors. The shell can only reconstruct state the runtime can replay.

## Development

Run the core test project:

```bash
dotnet test test/HPD-Agent.TUI.Tests/HPD-Agent.TUI.Tests.csproj
```

Run coding TUI tests:

```bash
dotnet test test/HPD-Agent.Harness.Coding.TUI.Tests/HPD-Agent.Harness.Coding.TUI.Tests.csproj
```

The test console modes are the fastest way to exercise a change in a real terminal:

```bash
dotnet run --project test/HPD-Agent.TUI.Console -- demo
dotnet run --project test/HPD-Agent.TUI.Console -- direct
dotnet run --project test/HPD-Agent.TUI.Console -- coding
dotnet run --project test/HPD-Agent.TUI.Console -- server
```

## Pre-1.0 compatibility

HPD Agent TUI is pre-1.0. Releases may refine runtime interfaces, transcript
rendering contracts, and model-selection surfaces as the terminal experience
stabilizes. Pin the package version for applications that need a stable integration
surface.

## Install

```bash
dotnet add package HPD-Agent.TUI
```

For coding presentation:

```bash
dotnet add package HPD-Agent.Harness.Coding.TUI
```
