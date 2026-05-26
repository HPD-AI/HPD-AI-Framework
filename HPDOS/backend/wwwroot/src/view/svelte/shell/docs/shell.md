# HPD-OS Shell

## Purpose

The shell owns the desktop window layout around the main work surfaces. It controls the window chrome toggle, sidebar visibility, workspace pane, app pane, app-pane resizing, and persisted shell layout intent.

The shell should feel native, stable, and predictable. Resize, collapse, refresh, and restore behavior must not surprise the user.

## Files

- `App.svelte` creates one shell controller and passes it to shell components.
- `ShellLayout.svelte` renders the sidebar, workspace pane, app pane, and resize handle.
- `WindowChrome.svelte` renders shell-level window controls.
- `controller.ts` owns shell state and layout intent.
- `layout.ts` owns split policy and width math.
- `resize.svelte.ts` owns pointer and keyboard resize behavior.
- `storage.ts` bridges shell layout state to desktop persistence.
- `styles.css` owns shell-specific CSS.
- `desktop/src/bun/settingsStore.ts` owns durable desktop settings storage.

## Invariants

- `App.svelte` creates exactly one `ShellLayoutController`.
- `WindowChrome` and `ShellLayout` share that same controller.
- Sidebar collapsed/expanded state is shell state, not component-local state.
- Expanded and collapsed app pane widths are remembered separately.
- The sidebar is fixed outside the workspace/app resize calculation.
- Resizing changes the workspace/app split only, never the sidebar width.
- Drag geometry is frozen at pointer-down so layout changes do not fight the cursor mid-drag.
- Widths are clamped by layout policy, not by ad hoc CSS.
- The shell must not paint persisted layout defaults before hydration completes.
- Desktop persistence stores layout intent, not transient DOM layout.
- Shell-specific storage stays in `storage.ts`; durable settings live in `desktop/src/bun/settingsStore.ts`.

## Persistence

Shell layout persistence stores:

```ts
type ShellLayoutSnapshot = {
  sidebarCollapsed: boolean;
  expandedAppPaneWidth: number | null;
  collapsedAppPaneWidth: number | null;
};
```

Expected behavior:

- Resize the app pane, release the divider, refresh: the app pane width is restored.
- Collapse the sidebar, refresh: the collapsed state is restored.
- Expanded and collapsed widths do not overwrite each other.
- If desktop storage is unavailable, the shell falls back to defaults without corrupting saved state.

## Layout Policy

The shell has two modes:

- `expanded`: sidebar visible, app pane defaults to 45% of resizable width.
- `collapsed`: sidebar hidden, app pane defaults to 65% of resizable width.

The resizable width is the shell width minus fixed sidebar width and active gaps.

The app pane has min/max bounds. If the available width is smaller than the combined minimums, the layout degrades proportionally instead of creating impossible constraints.

## Resize Behavior

Pointer resize:

- starts from frozen geometry captured on pointer-down;
- updates pane CSS variables through `requestAnimationFrame`;
- commits persisted layout only when drag ends.

Keyboard resize:

- `ArrowLeft` grows the app pane.
- `ArrowRight` shrinks the app pane.
- `Shift` uses the larger step.
- `Home` moves to minimum app pane width.
- `End` moves to maximum app pane width.
- `Enter` resets to the current mode default.

## Accessibility

The resize handle uses the ARIA separator pattern:

- `role="separator"`
- `tabindex="0"`
- `aria-orientation="vertical"`
- `aria-controls`
- `aria-valuemin`
- `aria-valuemax`
- `aria-valuenow`
- `aria-valuetext`

The sidebar toggle is a real button with `aria-expanded` and `aria-controls`.

## Non-Goals

- Do not put feature state into shell storage.
- Do not make `storage.ts` a global settings registry.
- Do not make component-local layout state that duplicates controller state.
- Do not persist transient DOM measurements.
- Do not add compatibility layers for old shell state formats.
- Do not add hidden layout mechanisms in CSS that fight `layout.ts`.

## Testing

Shell behavior should be covered by focused tests:

- layout policy math;
- controller state transitions;
- persistence commits;
- resize keyboard policy;
- hydration state;
- separate expanded/collapsed width memory.

Before changing shell behavior, run:

```sh
bun run test:ui
bun run check:ui
bun run build:ui
```

For desktop bridge changes, also run:

```sh
cd ../desktop
bun run check
```
