# HPD.TUI

HPD.TUI is a Native AOT-friendly terminal UI framework for the HPD ecosystem.
It uses retained components, pooled terminal grids, growable ANSI frame output, and differential rendering.

## Current Surface

- Core component contract: `IComponent`
- Runtime: `TuiApplication` for alternate-screen apps, `ManagedTerminalTuiApplication` for managed-terminal apps
- Non-interactive output: `TuiCapture`, `TuiOutput`
- Terminal abstraction: `ITerminal`, `ProcessTerminal`
- Rendering: `TuiRenderer`, `ManagedTerminalTuiRenderer`
- Components: `Container`, `Text`, `Markdown`, `Viewport`, `Overlay`, `OverlayHost`
- Layout primitives: `Stack`, `Grid`, `Separator`, `Frame`
- Layout hardening: `LayoutRect`, `LayoutConstraints`, grid cell padding/alignment, row sizing, and clipped cell rendering
- Model-first views: `PromptView`, `SelectionView<T>`, `TableView<T>`, `TreeView<T>`, `ActivityView`, `CommandPaletteView`
- Tables: semantic `TableModel<T>` with title, caption, borders, row separators, alignment, overflow, and adaptive column collapse
- Trees: `TreeModel<T>`, `TreeController<T>`, and `TreeView<T>` with outline, compact, and breadcrumb modes
- Activities: `ActivityView`, `ActivityGroupView`, and `ActivityScope` for single and multi-task status
- Flows: `PromptFlow<T>`, `PromptFlow.Text`, `PromptFlow.Secret`, `PromptFlow.Confirm`, `PromptFlow.Select`, `PromptFlow.MultiSelect`
- Content blocks: `TextBlock`, `MarkupBlock`, `MarkdownBlock`, `CodeBlock`, `KeyValueBlock`, `ListBlock`, `SeparatorBlock`
- Markdown: headings, lists, quotes, code blocks, inline code, links, task lists, and boxed pipe tables with width-aware wrapping
- Streaming markdown: `StreamCollector<T>`, `AnimationController<T>`, `TuiMarkdownRenderer`
- Extensibility: `IExtension`, `ExtensionManager`, `TuiExtensionRegistry`

## Minimal App

Use `TuiApplication` when the app should own the alternate screen:

```csharp
using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

using var app = new TuiApplication(new ProcessTerminal());

var root = new Container();
root.Add(new Text("Hello from HPD.TUI"));
root.Add(new Text("Press Ctrl+Escape to exit."));

app.SetRoot(root);
await app.RunAsync();
```

Use `ManagedTerminalTuiApplication` when the app should render in the normal terminal and keep a managed viewport without entering the alternate screen:

```csharp
using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

using var app = new ManagedTerminalTuiApplication(new ProcessTerminal());

app.SetRoot(new Text("Hello from the managed terminal"));
await app.RunAsync();
```

## Rendering Model

Components render into `TerminalGrid`, which stores cells, styles, and logical cursor state.
ANSI encoding lives in the renderer layer, not in `TerminalGrid`.

The renderer pipeline is:

```text
IComponent
  -> SegmentWriter
  -> TerminalGrid
  -> ANSI renderer
  -> growable ANSI frame writer
  -> ITerminal.Write(...)
  -> ITerminal.Flush()
```

`TuiRenderer` diffs full-screen cell grids for alternate-screen apps.
`ManagedTerminalTuiRenderer` renders logical component output in the normal terminal. It writes the full logical buffer on the first frame, patches visible changes when safe, scrolls naturally for appends, and falls back to a full reset when resize, shrink, or changed-above-viewport cases would leave stale rows.

## Native AOT

```bash
dotnet publish src/HPD-TUI.csproj \
  -c Release \
  -f net8.0 \
  -r osx-arm64 \
  -p:PublishAot=true \
  -p:PublishTrimmed=true
```

Replace `osx-arm64` with the target runtime identifier for the platform being published.

## Examples

- `examples/SimpleApp`
- `examples/InteractiveInput`
- `examples/ToolRendering`
