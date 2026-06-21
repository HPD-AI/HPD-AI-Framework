using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

using var app = new TuiApplication(new ProcessTerminal());

var root = new Container();
root.Add(new Text("HPD.TUI Simple App"));
root.Add(new Text("Press Ctrl+Escape to exit."));

app.SetRoot(root);
await app.RunAsync();
