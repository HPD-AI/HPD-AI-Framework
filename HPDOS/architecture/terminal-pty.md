# HPDOS Sidebar Terminal

## Goal

HPDOS should provide a first-class terminal page in the sidebar. The terminal should be workspace-scoped, persistent across sidebar navigation, and able to run real interactive shells and command-line tools.

This is not a bottom drawer terminal. HPDOS has a three-part workspace layout:

```text
+------+----------------------+----------------------+
| rail | sidebar              | main workspace       |
|      |                      |                      |
| S    | chat / files / apps  | artifacts / apps     |
| F    | terminal             | webviews             |
| A    |                      |                      |
| T    |                      |                      |
| +    |                      |                      |
+------+----------------------+----------------------+
```

The terminal should use the sidebar as a vertical workspace surface. This keeps the main area available for apps and artifacts while allowing terminal output to grow downward like a timeline.

## Why This Needs a Real PTY

ASP.NET Core gives HPDOS the right backend infrastructure:

- HTTP endpoints
- WebSocket endpoints
- workspace scoping
- auth and short-lived tokens
- lifecycle management
- session registries
- replay buffers
- tests and integration with the existing backend

ASP.NET Core does not provide a real pseudo-terminal.

`System.Diagnostics.Process` is not enough for an interactive terminal. It can start a shell and pipe text, but it does not provide correct TTY behavior for interactive programs, terminal resize, full-screen tools, shell prompts, colors, control sequences, and terminal-oriented buffering.

Therefore HPDOS should split responsibilities:

```text
Svelte 5 sidebar terminal UI
  -> ASP.NET Core terminal API
    -> local Bun PTY helper
      -> bun-pty
        -> shell process
```

ASP.NET Core owns the HPDOS terminal contract. The Bun helper owns the PTY primitive.

## What We Learned From opencode

opencode has a mature PTY model. The most important lesson is that terminal support is a backend resource, not only a UI widget.

Observed opencode pieces:

- `packages/opencode/src/pty/index.ts`
- `packages/opencode/src/pty/pty.ts`
- `packages/opencode/src/pty/pty.bun.ts`
- `packages/opencode/src/pty/pty.node.ts`
- `packages/opencode/src/pty/schema.ts`
- `packages/opencode/src/pty/input.ts`
- `packages/opencode/src/pty/ticket.ts`
- `packages/opencode/src/server/routes/instance/httpapi/groups/pty.ts`
- `packages/opencode/src/server/routes/instance/httpapi/handlers/pty.ts`
- `packages/app/src/context/terminal.tsx`
- `packages/app/src/components/terminal.tsx`
- `packages/app/src/pages/session/terminal-panel.tsx`
- `packages/app/src/components/session/session-sortable-terminal-tab.tsx`
- `packages/app/src/utils/terminal-websocket-url.ts`
- `packages/app/src/utils/terminal-writer.ts`

opencode uses:

- `bun-pty` when running under Bun
- `@lydell/node-pty` when running under Node
- an adapter import (`#pty`) so the rest of the system does not care which PTY implementation is active
- a PTY service with `list`, `get`, `create`, `update`, `remove`, `resize`, `write`, and `connect`
- a rolling output buffer
- cursor-based reconnect/replay
- multiple WebSocket subscribers per PTY session
- short-lived WebSocket attach tickets
- lifecycle events for created, updated, exited, and deleted
- tests for session lifecycle, output isolation, shell behavior, WebSocket IO, and missing sessions
- workspace-scoped frontend terminal state, not chat-session-scoped state
- persisted local terminal metadata: active terminal, order, title, dimensions, serialized buffer, replay cursor, and scroll position
- reconnect through a cursor-bearing WebSocket URL
- stale-terminal recovery when a locally remembered terminal no longer exists on the backend
- a renderer-local write queue so incoming output is batched and flushed predictably
- draggable tabs for multiple terminals in the browser app
- `ghostty-web` for browser rendering, while the backend still owns PTY semantics

The current opencode model also shows signs of incremental improvement:

- runtime-specific adapters exist because one PTY package did not solve every runtime cleanly
- connect tickets look like a security hardening pass after raw socket attachment
- cursor replay and rolling buffers look like reconnect and UI switching fixes
- comments in their WebSocket handler mention earlier pending-frame buffering that was removed once handshake ordering was understood
- the test suite encodes many contracts that likely came from production bugs

HPDOS should start with the refined version rather than rediscovering these lessons slowly.

## Second-Mover Advantage

HPDOS can keep opencode's backend lessons while choosing a different product shape.

opencode's terminal work is built around its own TUI and IDE-style flows. HPDOS is a desktop workspace with a persistent sidebar and a main webview/artifact surface. That means the terminal should not be modeled as a cramped bottom panel by default.

HPDOS advantages:

- the sidebar is already a first-class mode selected from the activity rail
- the main workspace is already reserved for apps and artifacts
- workspaces are explicit, so terminal cwd can be scoped to the user's chosen workspace
- Svelte 5 can make terminal state local and reactive without adding a heavy client architecture
- Electrobun already bundles Bun, giving HPDOS a practical path to `bun-pty`
- HPDOS can choose a terminal display pattern that fits a vertical sidebar instead of inheriting a bottom-panel tab strip
- HPDOS can begin with cursor replay, restore state, and stale-terminal recovery as first principles instead of retrofitting them after users hit broken reconnects

## Ownership Model

### ASP.NET Core Backend

The ASP.NET Core backend should own:

- public HTTP API
- public WebSocket endpoint
- terminal session registry
- workspace scoping
- terminal metadata
- connect ticket issuance and validation
- replay cursor and output buffer
- helper process lifecycle
- cleanup on app shutdown
- tests

### Bun PTY Helper

The Bun helper should own:

- PTY spawn
- PTY read stream
- PTY write
- PTY resize
- PTY kill
- process exit notification

The helper should be intentionally small. It should not know about HPDOS UI, sessions, artifacts, apps, or user workflows.

### Svelte 5 UI

The Svelte UI should own:

- terminal sidebar route
- terminal cards/list
- terminal pane rendering
- terminal renderer integration
- resize observation
- keyboard input forwarding
- local connection state
- local terminal display state
- serialized terminal buffer, replay cursor, dimensions, and scroll position
- visual status indicators

## Backend API

Suggested endpoints:

```text
GET    /api/hpdos/terminals/shells
GET    /api/hpdos/terminals
POST   /api/hpdos/terminals
GET    /api/hpdos/terminals/{id}
PATCH  /api/hpdos/terminals/{id}
DELETE /api/hpdos/terminals/{id}
POST   /api/hpdos/terminals/{id}/connect-token
GET    /api/hpdos/terminals/{id}/connect
```

`GET /api/hpdos/terminals/{id}/connect` should be a WebSocket endpoint.

The browser should not attach to a terminal WebSocket directly without a short-lived token. The UI should request a connect token, then attach with that token.

```text
POST /api/hpdos/terminals/{id}/connect-token
  -> { ticket, expiresInSeconds }

GET /api/hpdos/terminals/{id}/connect?ticket=...&cursor=...
  -> WebSocket
```

The ticket should be scoped to:

- terminal id
- workspace id or workspace root
- current local backend instance
- short TTL

## Terminal Model

Suggested terminal info:

```json
{
  "id": "term_01",
  "title": "Terminal 1",
  "command": "/bin/zsh",
  "args": ["-l"],
  "cwd": "/Users/ewoof/Desktop/chicken",
  "status": "running",
  "pid": 12345,
  "createdAt": "2026-05-26T12:00:00Z",
  "updatedAt": "2026-05-26T12:00:05Z",
  "exitCode": null
}
```

Suggested create input:

```json
{
  "title": "frontend",
  "command": "/bin/zsh",
  "args": ["-l"],
  "cwd": "/Users/ewoof/Desktop/chicken",
  "env": {
    "TERM": "xterm-256color"
  },
  "size": {
    "cols": 80,
    "rows": 24
  }
}
```

If `command` is omitted, HPDOS should use the user's preferred shell.

If `cwd` is omitted, HPDOS should use the selected workspace root.

## WebSocket Protocol

The browser-facing WebSocket should support:

- output frames from backend to UI
- metadata frames from backend to UI
- input frames from UI to backend
- resize frames from UI to backend
- close/exit notification

Initial protocol can be simple JSON messages for control plus raw text for terminal output, or JSON for all frames. The key requirement is that cursor replay is explicit.

Example control frames:

```json
{ "type": "ready", "cursor": 17291 }
{ "type": "output", "cursor": 17322, "data": "hello\\r\\n" }
{ "type": "input", "data": "ls\\n" }
{ "type": "resize", "cols": 104, "rows": 32 }
{ "type": "exit", "exitCode": 0 }
```

The backend should keep a bounded rolling output buffer per terminal. opencode uses roughly 2 MB. HPDOS can start with the same order of magnitude.

Reconnect behavior:

```text
UI last saw cursor 12000
UI reconnects with ?cursor=12000
backend replays buffered output after cursor 12000
backend then streams live output
```

If the cursor is too old and has fallen out of the buffer, the backend should replay from the oldest retained output and send a metadata frame indicating truncation.

## Helper Protocol

The ASP.NET Core backend should communicate with the Bun PTY helper over local stdio JSON lines.

Why stdio first:

- no extra public port
- easy process lifetime ownership
- easy logging
- easy replacement later
- keeps the browser WebSocket owned by ASP.NET Core

Suggested helper commands:

```json
{ "id": 1, "type": "create", "terminalId": "term_01", "command": "/bin/zsh", "args": ["-l"], "cwd": "/Users/ewoof/Desktop/chicken", "env": {}, "cols": 80, "rows": 24 }
{ "id": 2, "type": "write", "terminalId": "term_01", "data": "ls\\n" }
{ "id": 3, "type": "resize", "terminalId": "term_01", "cols": 104, "rows": 32 }
{ "id": 4, "type": "kill", "terminalId": "term_01" }
```

Suggested helper events:

```json
{ "type": "created", "terminalId": "term_01", "pid": 12345 }
{ "type": "output", "terminalId": "term_01", "data": "hello\\r\\n" }
{ "type": "exit", "terminalId": "term_01", "exitCode": 0 }
{ "type": "error", "terminalId": "term_01", "message": "..." }
```

The helper should not persist terminal state. The ASP.NET Core backend is the source of truth.

## Workspace Scoping

Terminals should be workspace-owned, not chat-session-owned.

This matches the HPDOS app model:

- apps are workspace-dependent
- files are workspace-dependent
- terminal should be workspace-dependent
- artifacts remain session-dependent

Default terminal cwd should be the current workspace root.

When multiple workspace roots exist, HPDOS should choose the primary root by default and allow the user to create a terminal in a specific root later.

## Sidebar Route

Add a terminal item to the activity rail:

```text
S  Sessions / Chat
F  Files
A  Apps
T  Terminal
+  New Session
```

The terminal page should live in the sidebar, alongside sessions, files, and apps.

Initial route shape:

```text
Terminal
1 running

[New Terminal] [Shell]

frontend                  running
$ bun run dev
vite ready in 230ms

backend                   running
$ dotnet run
Now listening on 127.0.0.1:4317
```

## Display Pattern

opencode's browser app represents multiple terminals as draggable tabs inside a bottom terminal panel:

```text
[ Terminal 1 ] [ server ] [ + ]
--------------------------------
active terminal fills panel
```

That is a good fit for an IDE-style bottom panel, but it is not the best fit for HPDOS. HPDOS already has a dedicated sidebar mode selected from the activity rail, and the main workspace should remain available for apps, artifacts, and webviews.

HPDOS should use a vertical terminal page:

```text
Terminal page

[New Terminal] [Shell]

Terminal 1                         running
+-----------------------------------------+
| interactive terminal pane               |
| $ bun run dev                           |
| vite ready in 230ms                     |
+-----------------------------------------+

backend                            running
+-----------------------------------------+
| collapsed recent output preview         |
| Now listening on: http://127.0.0.1:4317 |
+-----------------------------------------+
```

This gives HPDOS a different product shape from opencode:

- terminal is a sidebar mode, not a bottom drawer
- terminal output can grow vertically like a work timeline
- multiple terminals can be visible at once through cards
- one terminal can be expanded and interactive
- collapsed terminals can show status and recent output
- the main surface remains available for apps and artifacts

The initial HPDOS display contract should be:

- opening the terminal sidebar creates a first terminal only if no terminal exists for the workspace
- terminal creation is user-driven after that
- each terminal has a card with title, cwd or label, status, and actions
- one card can be expanded into an interactive terminal renderer
- collapsed cards show the latest output excerpt and status
- terminal cards keep their order per workspace
- switching sidebar modes must not kill backend PTYs
- switching sidebar modes should persist local display state, including active terminal, buffer, cursor, size, and scroll position
- if the frontend remembers a terminal that the backend no longer has, the UI should either remove it or offer/retry a clean recreated terminal

opencode's tab mechanics are still useful as behavior lessons:

- terminal titles should be renameable
- terminal order should be user-controlled
- terminal close should choose a reasonable next active terminal
- stale terminal ids should not strand the UI
- terminal focus often needs a second delayed focus pass after layout changes
- output writes should be batched before being sent to the renderer

## Svelte 5 Client Design

Svelte 5 is a good fit because terminal UI has reactive local state and side effects.

Suggested files:

```text
wwwroot/src/core/hpdosTerminalApi.ts
wwwroot/src/core/hpdosTerminalStore.ts
wwwroot/src/view/svelte/TerminalSidebarView.svelte
wwwroot/src/view/svelte/TerminalCard.svelte
wwwroot/src/view/svelte/TerminalPane.svelte
```

Svelte 5 usage:

- `$state` for terminal list, selected terminal id, connection status, and local UI state
- `$derived` for running/exited groups and active terminal
- `$effect` for WebSocket attach/detach
- a ResizeObserver action for terminal dimensions
- xterm.js for terminal rendering
- local persistence for workspace-scoped terminal display state
- a small writer queue for renderer output batching

The Svelte components should not know about the Bun helper. They should only know the HPDOS terminal API.

The Svelte version should be smaller than opencode's frontend because HPDOS does not need to recreate its Solid context/cache layering. The shape can be direct:

```text
hpdosTerminalStore.ts
  terminals
  activeTerminalId
  create/update/close/move
  persisted workspace UI metadata

TerminalSidebarView.svelte
  terminal page shell
  new terminal button
  vertical terminal cards

TerminalCard.svelte
  title/status/actions
  collapsed recent output preview
  expanded terminal pane slot

TerminalPane.svelte
  renderer lifecycle
  connect-token request
  WebSocket attach/retry
  resize observer
  input forwarding
  buffer/cursor/scroll cleanup persistence
```

Example Svelte state shape:

```ts
let terminals = $state<TerminalViewState[]>([]);
let activeTerminalId = $state<string | undefined>();
let activeTerminal = $derived(terminals.find((terminal) => terminal.id === activeTerminalId));
```

Example pane ownership:

```ts
$effect(() => {
  if (!terminal.id) return;
  const connection = connectTerminal(terminal);
  return () => connection.dispose();
});
```

The hard parts remain real: PTY lifecycle, replay buffers, reconnect, resize, and renderer cleanup. Svelte helps by keeping those lifetimes local to the component that owns them.

## Dependencies

Backend/helper:

- Bun helper script
- `bun-pty`

Frontend:

- `xterm`
- likely `@xterm/addon-fit`

Alternative frontend renderer:

- `ghostty-web`, if HPDOS later wants to evaluate the same browser renderer opencode uses

For the first implementation, `xterm` is the lower-risk default because it is widely used, documented, and easy to integrate. The renderer should be kept behind `TerminalPane.svelte` so it can be replaced later.

ASP.NET Core:

- no PTY dependency required initially
- WebSocket support
- JSON serialization for helper protocol

## Security and Safety

Terminals execute commands, so HPDOS should treat them as powerful workspace resources.

Initial safety invariants:

- terminal cwd must be inside or equal to an allowed workspace root unless explicitly approved later
- browser WebSocket attach requires a short-lived ticket
- terminal IDs are opaque
- terminal output buffer is bounded
- terminal helper is local-only and owned by the backend process
- terminal sessions are cleaned up on backend shutdown
- terminal creation should be user-driven, not automatic on app load

Open safety question:

- Should the terminal run on the host workspace directly, or should a future variant run inside the HPDOS Lima instance?

For the first local desktop implementation, host workspace PTY is acceptable because HPDOS already operates on local workspace files. The architecture should still keep the PTY backend replaceable.

## Testing Plan

Backend tests:

- list starts empty
- create terminal returns metadata
- get missing terminal returns typed 404
- resize missing terminal returns typed 404
- delete missing terminal returns typed 404
- write to missing terminal returns typed 404 or WebSocket error
- create short-lived process emits created, exited, deleted
- output is delivered to WebSocket subscribers
- input sent through WebSocket reaches the process
- reconnect with cursor replays missed output
- old cursor reports truncation when buffer has rolled over
- multiple subscribers receive isolated output
- deleting terminal closes subscribers and kills process

Helper tests:

- create shell
- write input
- receive output
- resize
- kill
- exit event
- invalid command reports error

UI tests:

- terminal sidebar route renders
- new terminal creates backend terminal
- xterm attaches and receives output
- resize sends size update
- switching sidebar routes does not destroy backend terminal
- reconnect preserves output through cursor replay
- local display state restores active terminal, terminal order, dimensions, cursor, and scroll position
- stale locally persisted terminal ids do not strand the UI
- collapsed terminal cards show recent output without attaching every terminal as an active renderer
- closing the active terminal selects a reasonable next terminal
- terminal focus returns after opening the sidebar or switching the expanded terminal

## Implementation Order

1. Add terminal types and backend API skeleton.
2. Add Bun PTY helper with `bun-pty`.
3. Add backend helper process manager.
4. Add terminal session registry and lifecycle cleanup.
5. Add WebSocket attach with ticket validation.
6. Add replay buffer and cursor support.
7. Add workspace-scoped Svelte terminal store with local display persistence.
8. Add terminal sidebar route with vertical terminal cards.
9. Add interactive terminal pane for one expanded terminal.
10. Add collapsed terminal previews from local/replayed output.
11. Add stale-terminal recovery and close/active-selection behavior.
12. Add tests for lifecycle, WebSocket IO, reconnect, cleanup, persistence, and UI recovery.

## Non-Goals

- Do not make Electrobun own terminal sessions.
- Do not expose the Bun helper directly to the browser.
- Do not use `System.Diagnostics.Process` as a fake terminal.
- Do not copy opencode's bottom-panel terminal tabs as HPDOS's default display pattern.
- Do not make terminal state chat-session-owned.
- Do not block future Lima-backed terminal variants.

## Final Recommendation

Build HPDOS Terminal as a first-class workspace service:

```text
Svelte 5 terminal UI
  -> ASP.NET Core terminal API
    -> Bun PTY helper
      -> bun-pty
```

Copy opencode's backend maturity:

- real PTY
- adapter boundary
- bounded replay buffer
- cursor reconnect
- explicit resize/write/delete
- short-lived connect tickets
- lifecycle tests

Then apply HPDOS's product advantage:

- sidebar-first terminal
- workspace-scoped sessions
- main surface stays reserved for apps/artifacts
- vertical multi-terminal cards instead of a bottom-panel tab strip
- local Svelte display state for order, active terminal, renderer state, cursor, and scroll position
- stale-terminal recovery from the first implementation
