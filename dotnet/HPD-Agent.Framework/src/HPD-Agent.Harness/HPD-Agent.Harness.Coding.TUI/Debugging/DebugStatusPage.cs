using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal static class DebugStatusPage
{
    public const string PageId = "hpd.coding.debug";

    public static HpdAgentTuiPageDescriptor Create(CodingHarnessTuiTheme theme)
        => new(PageId, context => new Component(context.State, theme))
        {
            Title = "Debugger",
            Description = "Inspect debugger sessions, breakpoints, and bounded output.",
            Hidden = true
        };

    private sealed class Component(AgentTuiStateBag state, CodingHarnessTuiTheme theme) : HPD.TUI.Core.Component
    {
        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        {
            var maxWidth = constraints.MaxWidth;
            var rows = 2;
            if (state.TryGet<DebugTuiState>(DebugTuiState.StateKey, out var debug))
            {
                rows += debug.Trees.Values.Sum(tree =>
                    3 +
                    debug.Sessions.Values.Count(session => session.DebugTreeId == tree.DebugTreeId) +
                    debug.BreakpointSelections.Values
                        .Where(selection => selection.DebugTreeId == tree.DebugTreeId)
                        .Sum(selection => Math.Min(12, selection.After.Count)) +
                    Math.Min(8, tree.Output.Count));
            }
            return new(Math.Min(20, maxWidth), Math.Min(120, maxWidth), rows);
        }

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var maxWidth = output.MaxWidth;
            output.Write("Debugger".AsSpan(), theme.ResolveLabel(context.Theme));
            if (!state.TryGet<DebugTuiState>(DebugTuiState.StateKey, out var debug) ||
                debug.Trees.Count == 0)
            {
                WriteLine(ref output, "No debugger sessions are retained.", maxWidth, theme.ResolveMuted(context.Theme));
                return;
            }

            foreach (var tree in debug.Trees.Values.OrderByDescending(item => item.LastChanged))
            {
                WriteLine(ref output, $"• {tree.Status.ToLowerInvariant()}  {tree.AdapterId ?? "adapter"}  {Short(tree.DebugTreeId)}",
                    maxWidth, theme.ResolveText(context.Theme));
                WriteLine(ref output, $"  breakpoints {tree.Breakpoints.Verified}/{tree.Breakpoints.Requested} verified, " +
                    $"{tree.Breakpoints.Pending} pending", maxWidth, theme.ResolveMuted(context.Theme));
                WriteLine(ref output, $"  threads {tree.ThreadCount}  modules {tree.ModuleCount}  sources {tree.SourceCount}",
                    maxWidth, theme.ResolveMuted(context.Theme));
                foreach (var session in debug.Sessions.Values
                    .Where(session => session.DebugTreeId == tree.DebugTreeId)
                    .OrderBy(session => session.DebugSessionId, StringComparer.Ordinal))
                {
                    var stop = session.CurrentStop;
                    var location = stop is { DisplayPath: { } path, Line: { } line }
                        ? $" · {path}:{line}"
                        : "";
                    WriteLine(
                        ref output,
                        $"  session {Short(session.DebugSessionId)} · {session.Status.ToLowerInvariant()}{location}",
                        maxWidth,
                        theme.ResolveMuted(context.Theme));
                }
                foreach (var selection in debug.BreakpointSelections.Values
                    .Where(selection => selection.DebugTreeId == tree.DebugTreeId)
                    .OrderBy(selection => selection.Kind)
                    .ThenBy(selection => selection.ToolCallId, StringComparer.Ordinal))
                {
                    foreach (var breakpoint in selection.After.Take(12))
                    {
                        var location = breakpoint.DisplayPath is { } path
                            ? $"{path}:{breakpoint.ResolvedLine ?? breakpoint.RequestedLine}"
                            : breakpoint.SafeDisplayName ?? breakpoint.Kind.ToString();
                        var status = breakpoint.Verified ? "●" : breakpoint.Acknowledged ? "○" : "!";
                        WriteLine(ref output, $"  {status} {location}", maxWidth, theme.ResolveMuted(context.Theme));
                    }
                }
                if (tree.DroppedOutputRecords > 0)
                    WriteLine(ref output, $"  output dropped {tree.DroppedOutputRecords} records",
                        maxWidth, theme.ResolveMuted(context.Theme));
                foreach (var text in tree.Output.TakeLast(8))
                    WriteLine(ref output, $"  {text.ReplaceLineEndings(" ")}",
                        maxWidth, theme.ResolveMuted(context.Theme));
            }
        }

        public override bool HandleInput(in TuiInputEvent input) => false;
        private static string Short(string value) => value.Length <= 8 ? value : value[..8];
        private static void WriteLine(
            ref DisplayListBuilder output,
            string text,
            int maxWidth,
            Style style)
        {
            output.WriteLineBreak();
            output.Write(text.AsSpan(0, Math.Min(text.Length, maxWidth)), style);
        }
    }
}
