using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

using var app = new TuiApplication(new ProcessTerminal());

var history = new Viewport(height: 8);
var prompt = PromptView.Create(submitted: value => history.AddLine($"> {value}"));

var root = new Container();
root.Add(new Text("Type and press Enter. Press Ctrl+Escape to exit."));
root.Add(history);
root.Add(prompt);

app.SetRoot(root);
app.SetFocus(prompt);
await app.RunAsync();
