# HPDOS Workspace Tabs

## Goal

HPDOS should replace the current main workspace model with a first-class tabbed surface manager.

The current model has grown by special cases:

- artifacts render in the main workspace
- apps render in the main workspace
- files live in the sidebar
- terminal lives in the sidebar
- chat input stays fixed below the main workspace

This worked while HPDOS was small, but it is already showing pressure. Terminals need more width. File browsing wants real preview space. Apps need persistent webview slots. Artifacts need to remain available without being the only thing the main area understands.

HPDOS does not need to preserve the old surface contract. We do not need backward-compatible layout shims, transitional duplicate renderers, or retrofit paths that keep old special cases alive. The tabbed workspace should become the new root architecture.

The new model should be:

```text
+------+----------------------+--------------------------------+
| rail | sidebar              | main workspace                 |
|      |                      |                                |
| S    | session context      | [Artifacts] [Penpot] [+]       |
| F    | workspace context    |                                |
| A    | app catalog          | active workspace tab           |
| +    | creation shortcuts   |                                |
+------+----------------------+--------------------------------+
```

The main workspace should own all heavyweight work surfaces. The sidebar should own navigation and context.

## Core Idea

The `+` button in the main workspace tab strip should not create a blank tab immediately.

It should open a tab picker:

```text
Choose what to open

[ Files      ] Browse workspace files
[ Terminal   ] Start an interactive shell
[ Browser    ] Open a website
[ Apps       ] Open a workspace app
[ Artifacts  ] View session outputs
[ Review     ] View code changes
[ Side chat  ] Start a focused conversation
```

Each choice creates a concrete tab kind. HPDOS stops asking whether a feature belongs in the sidebar or in a hardcoded main-area slot. It asks what kind of workspace tab the user wants.

The core architectural rule is:

```text
rail/sidebar = navigation and context
main workspace = all real work tabs
composer = persistent command/input layer
```

This means the old special-case workspace renderers should be deleted once their tab equivalents exist.

## Why This Matters

The terminal experiment exposed the shape of the problem.

Terminals technically can live in the sidebar, but they do not have enough horizontal space. That is not a terminal bug. It is a layout contract bug.

Files have the same issue. A sidebar file tree is useful for quick navigation, but serious file work needs space:

- tree
- preview
- editor or rendered document
- metadata
- search results
- diffs

Apps already need the main surface. Artifacts already need the main surface. Browser tabs, reviews, terminals, and side chats will also need it.

So the main surface should become the place where work views live.

## Product Shape

Initial tab strip:

```text
+--------------------------------------------------+
| [Artifacts 2] [Penpot] [Code Server] [+]         |
+--------------------------------------------------+
|                                                  |
| active tab content                               |
|                                                  |
+--------------------------------------------------+
```

Clicking `+` opens a centered chooser:

```text
+--------------------------------------+
| Open in workspace                     |
|                                      |
| Files      Browse workspace files     |
| Terminal   Start an interactive shell |
| Browser    Open a website             |
| Apps       Open installed apps        |
| Artifacts  View session outputs       |
| Review     View code changes          |
| Side chat  Start focused chat         |
+--------------------------------------+
```

The tab strip should remain visible even when there are no tabs. In that state it shows only the strip background and the `+` button.

## Tab Model

Suggested frontend model:

```ts
type WorkspaceTab =
  | ArtifactListTab
  | ArtifactTab
  | AppTab
  | FilesTab
  | TerminalTab
  | BrowserTab
  | ReviewTab
  | SideChatTab;

interface WorkspaceTabBase {
  id: string;
  kind: string;
  title: string;
  icon?: string;
  createdAt: string;
  updatedAt: string;
  pinned?: boolean;
}

interface ArtifactListTab extends WorkspaceTabBase {
  kind: "artifact-list";
  sessionId: string;
}

interface ArtifactTab extends WorkspaceTabBase {
  kind: "artifact";
  sessionId: string;
  artifactId: string;
}

interface AppTab extends WorkspaceTabBase {
  kind: "app";
  appId: string;
}

interface FilesTab extends WorkspaceTabBase {
  kind: "files";
  rootId?: string;
  path?: string;
}

interface TerminalTab extends WorkspaceTabBase {
  kind: "terminal";
  terminalId?: string;
}

interface BrowserTab extends WorkspaceTabBase {
  kind: "browser";
  url?: string;
}

interface ReviewTab extends WorkspaceTabBase {
  kind: "review";
}

interface SideChatTab extends WorkspaceTabBase {
  kind: "side-chat";
  sessionId?: string;
}
```

This should start as a frontend-local model. Backend persistence can come later if we need cross-window or crash restore. For now, local workspace persistence is enough.

Because this is a breaking architecture change, the tab model should be treated as the only main-surface contract. Existing app/artifact/file/terminal display state should be adapted into tab records or feature-local renderer state, then old layout state should be removed.

Suggested persisted state:

```ts
interface WorkspaceTabState {
  activeTabId?: string;
  tabs: WorkspaceTab[];
  order: string[];
}
```

Storage key:

```text
hpdos.workspaceTabs.{workspaceId}.v1
```

Session-dependent tabs should include their session id. Workspace-dependent tabs should not.

## Ownership Rules

### Workspace-Scoped Tabs

These belong to the current workspace:

- app tabs
- files tabs
- terminal tabs
- browser tabs
- review tabs

Switching sessions should not destroy them.

### Session-Scoped Tabs

These belong to the selected chat session:

- artifact list tab
- individual artifact tabs
- side chat tabs, if the side chat is session-specific

Switching sessions can update or hide these tabs depending on the tab contract.

The important invariant is explicitness. A tab knows whether it is workspace-scoped or session-scoped.

## Initial Tab Kinds

### Artifacts

The current artifact display should be replaced by an `artifact-list` tab.

This tab shows all artifacts for the active session. Individual artifacts can either open inside the same tab or later become their own `artifact` tabs.

Initial behavior:

- If a session has artifacts, HPDOS ensures an Artifacts tab exists.
- If a session has no artifacts, the tab strip can remain empty except for `+`.
- The Artifacts tab is session-scoped.

### Apps

The existing app webview host should be replaced by `app` tabs.

Opening Penpot creates:

```ts
{ kind: "app", appId: "penpot", title: "Penpot" }
```

Opening Code Server creates:

```ts
{ kind: "app", appId: "code-server", title: "Code Server" }
```

App tabs are workspace-scoped.

They should preserve their webview identity while switching tabs when possible. If a webview must be hidden instead of destroyed, the tab host should own that behavior.

### Files

Files should move to the main workspace.

The sidebar can still show a compact file shortcut later, but it should no longer own the file explorer. The file explorer should be a `files` tab.

The Files tab should support:

- workspace root selection
- tree/list browsing
- search
- file preview
- directory details
- opening documents/code in subviews or future tabs

Initial behavior can be simple: one Files tab with the current file browser, but with enough width to grow into preview.

### Terminal

Terminal should move out of the sidebar into a `terminal` tab.

The PTY service remains the same:

```text
Svelte terminal tab
  -> ASP.NET Core terminal API
    -> Bun PTY helper
      -> bun-pty
```

Only the display owner changes. The old sidebar terminal page should be removed after the terminal tab exists.

Terminal tabs should have enough width for real command output. Multiple terminal tabs can exist. A future refinement can allow split terminals, but the first version only needs one terminal per tab.

### Browser

Browser tabs represent web destinations.

This can start with the existing Browser app/runtime, but the tab model should not assume every browser is a marketplace app. A browser tab is a workspace view that can point at a URL.

### Review

Review tabs are for code changes:

- changed files
- diffs
- generated edits
- staged/unstaged status later

This can start as a placeholder if the underlying review model is not ready.

### Side Chat

Side chat is a focused conversation tab. It should not replace the primary chat sidebar immediately. It is a way to put a chat next to work when the user wants more space.

This should remain optional. The first implementation can include the picker entry but defer the full side-chat runtime.

## Svelte 5 Architecture

Suggested files:

```text
wwwroot/src/core/hpdosWorkspaceTabs.ts
wwwroot/src/core/hpdosWorkspaceTabs.test.js
wwwroot/src/view/svelte/WorkspaceTabStrip.svelte
wwwroot/src/view/svelte/WorkspaceTabPicker.svelte
wwwroot/src/view/svelte/WorkspaceTabHost.svelte
wwwroot/src/view/svelte/tabs/ArtifactsTab.svelte
wwwroot/src/view/svelte/tabs/AppTab.svelte
wwwroot/src/view/svelte/tabs/FilesTab.svelte
wwwroot/src/view/svelte/tabs/TerminalTab.svelte
```

Existing `WorkspaceSurface.svelte` should become the owner of:

- tab state
- active tab
- tab strip
- tab picker
- tab host

Suggested shape:

```text
WorkspaceSurface.svelte
  WorkspaceTabStrip
  WorkspaceTabPicker
  WorkspaceTabHost
    ArtifactsTab
    AppTab
    FilesTab
    TerminalTab
```

The tab host should be the only component that knows how to render a tab kind.

The tab strip should only know:

- id
- title
- icon
- status
- active state
- close action

The picker should only know:

- available tab types
- whether each type is enabled
- what payload is needed to create it

## Replacement Plan

Current special-case surfaces should be replaced by tab renderers.

Likely current owners:

- `WorkspaceSurface.svelte`
- `WorkspaceAppsView.svelte`
- artifact view components
- terminal sidebar components
- file sidebar components

Replacement path:

1. Add tab state and tab strip to `WorkspaceSurface.svelte`.
2. Replace current artifact rendering with `ArtifactsTab`.
3. Replace current app rendering with `AppTab`.
4. Add `+` picker with entries for Artifacts, Apps, Files, Terminal.
5. Replace the sidebar terminal page with `TerminalTab`.
6. Replace the sidebar file explorer page with `FilesTab`.
7. Convert sidebar entries into shortcuts that open/focus corresponding workspace tabs.
8. Delete stale main-area app/artifact special casing after tab host owns both.
9. Delete stale sidebar-owned terminal/file renderers once their tab versions work.

The important posture: do not preserve old screens as compatibility paths. Once a feature has a tab renderer, that renderer is the product surface.

## Sidebar Relationship

The sidebar should not disappear.

It should become lighter:

- sessions/chat context
- compact workspace overview
- app catalog shortcuts
- quick file shortcuts
- creation shortcuts

But heavyweight work views must live in main tabs.

Example:

```text
Click Files in sidebar
  -> focus existing Files tab
  -> or create a Files tab through the tab service

Click Terminal in rail
  -> focus existing Terminal tab
  -> or create a Terminal tab
```

This lets the rail/sidebar remain fast while the main workspace provides real estate.

If a sidebar page exists only because a heavyweight view used to live there, remove it. The sidebar can keep launchers and summaries; it should not keep duplicate full implementations.

## Tab Creation Contract

The picker should use a registry:

```ts
interface WorkspaceTabDefinition {
  kind: WorkspaceTab["kind"];
  title: string;
  description: string;
  icon: string;
  scope: "workspace" | "session";
  enabled: boolean;
  create: () => WorkspaceTab | Promise<WorkspaceTab>;
}
```

This gives HPDOS a clean extension point.

Later, app packages can contribute tab definitions:

```text
Penpot package
  contributes:
    tab kind: app
    app id: penpot
    title: Penpot
```

Internal features can contribute definitions the same way:

```text
Terminal feature
  contributes:
    tab kind: terminal
    title: Terminal
```

## Persistence

Initial persistence should be local browser storage:

- active tab id
- tab order
- tab records

Do not persist volatile renderer internals in the tab record directly.

Renderer-specific state should stay with each feature:

- terminal cursor/buffer/dimensions in terminal display state
- app webview state in app host state
- file explorer selected root/path in files tab payload

This keeps the tab model small and stable.

## Invariants

- The main workspace always has a tab strip.
- The tab strip can be empty except for `+`.
- `+` opens a picker; it does not create a mystery tab.
- Tabs have explicit kind and scope.
- Workspace-scoped tabs survive chat-session switching.
- Session-scoped tabs know which session they belong to.
- App tabs are workspace-scoped.
- Terminal tabs are workspace-scoped.
- Files tabs are workspace-scoped.
- Artifact tabs are session-scoped.
- The tab host is the only place that renders tab content by kind.
- Sidebar shortcuts should focus/create tabs rather than owning heavy views.
- There should be no old app/artifact/files/terminal main-surface branches after the tab host exists.
- There should be no duplicate full file explorer or terminal surfaces in the sidebar after their tab renderers exist.

## Testing Plan

Core tests:

- create tab from definition
- close active tab selects a reasonable next tab
- close inactive tab keeps active tab
- move tab order
- persist and restore workspace tabs
- drop stale tabs if their required session/app is gone
- ensure `+` picker definitions create the expected tab shape

UI tests:

- tab strip renders with only `+` when no tabs exist
- clicking `+` opens the picker
- choosing Files creates/focuses Files tab
- choosing Terminal creates/focuses Terminal tab
- choosing an app creates/focuses App tab
- switching tabs does not destroy app/terminal state unexpectedly
- session switch updates artifact tabs but preserves workspace tabs

Integration smoke:

- open terminal tab, run `pwd`, switch away, switch back, output remains
- open files tab, browse a directory, switch away, switch back, path remains
- open app tab, switch away, switch back, app remains loaded if the webview supports persistence

## Implementation Order

1. Add `hpdosWorkspaceTabs.ts` model helpers and tests.
2. Replace `WorkspaceSurface.svelte` with the tab root: strip, picker, host, and composer slot.
3. Add `WorkspaceTabStrip.svelte`.
4. Add `WorkspaceTabPicker.svelte` opened by `+`.
5. Add `WorkspaceTabHost.svelte`.
6. Move artifact rendering into `ArtifactsTab` and delete the old artifact surface branch.
7. Move app rendering into `AppTab` and delete the old app surface branch.
8. Move file explorer rendering into `FilesTab` and delete the full sidebar file explorer.
9. Move terminal rendering into `TerminalTab` and delete the sidebar terminal page.
10. Convert rail/sidebar Files/Apps/Terminal actions to open/focus tabs.
11. Remove stale layout state, stale CSS, and stale component imports from the old surfaces.

## Non-Goals

- Do not build a full plugin marketplace here.
- Do not make tabs backend-persistent in the first pass.
- Do not destroy the sidebar.
- Do not make terminal a bottom drawer.
- Do not keep duplicate compatibility surfaces alive after their tab replacements exist.
- Do not keep terminal trapped in the sidebar.
- Do not keep the file explorer trapped in the sidebar.

## Final Recommendation

Replace the main workspace with a tabbed surface manager.

The tab infrastructure becomes the shared place for:

- artifacts
- apps
- files
- terminals
- browsers
- reviews
- side chats

This is the right second-mover move: instead of copying IDE bottom panels or growing sidebar special cases, HPDOS gets a general workspace surface where any future tool can become a tab.

The clean version is not an incremental shell around the old UI. Tabs become the workspace. Old special-case surfaces are removed as soon as their tab renderers exist.
