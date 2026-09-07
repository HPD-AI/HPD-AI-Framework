using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Tests;
if (args.Length > 0)
{
    var oracle = new VirtualTerminalOracle(24, 6);
    oracle.Apply(File.ReadAllText(args[0]));
    if (oracle.Line(0) != "history" || oracle.Line(1) != "prompt edited" ||
        oracle.Scrollback.Count != 0 || !oracle.Autowrap || !oracle.CursorVisible)
        throw new Exception("PTY output failed terminal-model validation.");
    Console.WriteLine("Production ProcessTerminal PTY output passed history, live repaint, page return, wrapping, and cursor checks.");
    return;
}
using var terminal = new ProcessTerminal();
if (!terminal.ManagedTerminalCapabilities.SupportsSplitFooter || terminal.GetSize() != new TerminalSize(24, 6))
    throw new Exception("Production terminal detection failed.");
using var renderer = new ManagedTerminalTuiRenderer(terminal);
var batch = new ScrollbackBatch(0, 0, [new ScrollbackRow("history",
    "history".Select(c => new ScrollbackCell(c.ToString(), Style.Default, default, 1)).ToArray())]);
renderer.Render(new Text("prompt"), scrollback: batch);
renderer.Render(new Text("prompt edited"));
renderer.SetFullScreen(true);
renderer.Render(new Text("full screen page"));
renderer.SetFullScreen(false);
renderer.Render(new Text("prompt edited"));
renderer.Shutdown();
