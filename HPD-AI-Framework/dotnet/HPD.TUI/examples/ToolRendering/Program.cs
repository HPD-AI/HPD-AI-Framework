using HPD.TUI.Content;
using HPD.TUI.Components;
using HPD.TUI.Extensions;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

using var app = new TuiApplication(new ProcessTerminal());

var registry = new TuiExtensionRegistry()
    .RegisterToolResultMapper(new JsonToolResultMapper());

var root = new Container();
root.Add(new Text("HPD.TUI semantic tool result example"));
root.Add(new Text("Press Ctrl+Escape to exit."));
if (registry.TryMapToolResult("application/json", """
{
  "tool": "workspace.search",
  "matches": 3
}
""".AsMemory(), out var block))
{
    root.Add(Frame.Create(block).WithHeader("tool result").WithPadding(1).WithBorder(BorderSpec.Rounded));
}

app.SetRoot(root);
await app.RunAsync();

internal sealed class JsonToolResultMapper : IToolResultMapper
{
    public bool TryMap(string contentType, ReadOnlyMemory<char> payload, out IContentBlock block)
    {
        if (contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            block = CodeBlock.Create(payload.ToString(), "json");
            return true;
        }

        block = null!;
        return false;
    }
}
