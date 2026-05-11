# HPD.TUI

HPD.TUI is a Native AOT-friendly terminal UI framework for the HPD ecosystem.
It uses retained components, a pooled terminal grid, span-based output, and differential rendering.

## Current Surface

- Core component contract: `IComponent`
- Runtime: `TuiApplication`
- Non-interactive output: `TuiCapture`, `TuiOutput`
- Terminal abstraction: `ITerminal`, `ProcessTerminal`
- Components: `Container`, `Text`, `Markdown`, `Viewport`, `Overlay`, `OverlayHost`
- Layout primitives: `Stack`, `Grid`, `Separator`, `Frame`
- Layout hardening: `LayoutRect`, `LayoutConstraints`, grid cell padding/alignment, row sizing, and clipped cell rendering
- Model-first views: `PromptView`, `SelectionView<T>`, `TableView<T>`, `TreeView<T>`, `ActivityView`, `CommandPaletteView`
- Tables: semantic `TableModel<T>` with title, caption, borders, row separators, alignment, overflow, and adaptive column collapse
- Trees: `TreeModel<T>`, `TreeController<T>`, and `TreeView<T>` with outline, compact, and breadcrumb modes
- Activities: `ActivityView`, `ActivityGroupView`, and `ActivityScope` for single and multi-task status
- Flows: `PromptFlow<T>`, `PromptFlow.Text`, `PromptFlow.Secret`, `PromptFlow.Confirm`, `PromptFlow.Select`, `PromptFlow.MultiSelect`
- Content blocks: `TextBlock`, `MarkupBlock`, `MarkdownBlock`, `CodeBlock`, `KeyValueBlock`, `ListBlock`, `SeparatorBlock`
- Streaming markdown: `StreamCollector<T>`, `AnimationController<T>`, `TuiMarkdownRenderer`
- Extensibility: `IExtension`, `ExtensionManager`, `TuiExtensionRegistry`

## Minimal App

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

## Native AOT

```bash
dotnet publish src/HPD.TUI/HPD.TUI.csproj \
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
