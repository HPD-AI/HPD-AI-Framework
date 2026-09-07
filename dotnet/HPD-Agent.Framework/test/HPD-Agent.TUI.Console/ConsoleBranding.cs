using HPD.Agent.TUI.Composition;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Utilities;

namespace HPD.Agent.TUI.Console;

internal static class ConsoleBranding
{
    private static readonly Color BrandColor = new(70, 205, 230);
    private static readonly Style BrandStyle = new(BrandColor, Color.Default);

    private const string FullLogo = """
██╗  ██╗ ██████╗  ██████╗        █████╗   ██████╗  ███████╗ ███╗   ██╗ ████████╗
██║  ██║ ██╔══██╗ ██╔══██╗      ██╔══██╗ ██╔════╝  ██╔════╝ ████╗  ██║ ╚══██╔══╝
███████║ ██████╔╝ ██║  ██║      ███████║ ██║  ███╗ █████╗   ██╔██╗ ██║    ██║
██╔══██║ ██╔═══╝  ██║  ██║      ██╔══██║ ██║   ██║ ██╔══╝   ██║╚██╗██║    ██║
██║  ██║ ██║      ██████╔╝      ██║  ██║ ╚██████╔╝ ███████╗ ██║ ╚████║    ██║
╚═╝  ╚═╝ ╚═╝      ╚═════╝       ╚═╝  ╚═╝  ╚═════╝  ╚══════╝ ╚═╝  ╚═══╝    ╚═╝
""";

    private const string CompactLogo = """
██╗  ██╗ ██████╗  ██████╗
██║  ██║ ██╔══██╗ ██╔══██╗
███████║ ██████╔╝ ██║  ██║
██╔══██║ ██╔═══╝  ██║  ██║
██║  ██║ ██║      ██████╔╝
╚═╝  ╚═╝ ╚═╝      ╚═════╝
""";

    public static HpdAgentTuiBuilder AddConsoleBranding(
        this HpdAgentTuiBuilder tui,
        string? subtitle = null)
        => tui
            .UseTheme(new Theme
            {
                Accent = BrandStyle,
                Blue = BrandStyle
            })
            .ReplaceHeader(context => new ConsoleHeader(
                subtitle is null
                    ? HeaderDetail(context)
                    : $"{subtitle}  |  {HeaderDetail(context)}"))
            .ReplacePromptStatus(context => new Text(
                string.IsNullOrWhiteSpace(context.Shell.PromptStatusText)
                    ? "state: idle | Ctrl+Escape exits"
                    : context.Shell.PromptStatusText))
            .ConfigureShellChrome(chrome =>
            {
                chrome.Dialog.Y = 15;
                chrome.Dialog.Height = 14;
            });

    private static string HeaderDetail(AgentTuiShellContext context)
        => $"HPD Agent TUI  agent: {context.Scope.AgentId}  session: {context.Scope.SessionId}  thread: {context.Scope.ThreadId}";

    private sealed class ConsoleHeader(string detail) : HPD.TUI.Core.Component
    {
        private static readonly Style LogoStyle = BrandStyle;
        private static readonly Style DetailStyle = new(Color.Gray, Color.Default);

        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
        {
            var maxWidth = constraints.MaxWidth;
            var logo = SelectLogo(maxWidth);
            var width = Math.Max(MeasureMaxLineWidth(logo), UnicodeWidth.GetWidth(detail.AsSpan()));
            return new Measurement(Math.Min(width, maxWidth), Math.Min(width, maxWidth));
        }

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var maxWidth = output.MaxWidth;
            var stack = new Stack { Gap = 0 }
                .Add(new Text(SelectLogo(maxWidth), LogoStyle))
                .Add(new Text(detail, DetailStyle));

            output.Render(stack, in context, maxWidth);
        }

        public bool HandleInput(in TuiInputEvent key)
        {
            return false;
        }

        private static string SelectLogo(int width)
            => width >= MeasureMaxLineWidth(FullLogo) ? FullLogo : CompactLogo;

        private static int MeasureMaxLineWidth(string text)
        {
            var width = 0;
            foreach (var line in text.Split('\n'))
            {
                width = Math.Max(width, UnicodeWidth.GetWidth(line.TrimEnd('\r').AsSpan()));
            }

            return width;
        }
    }
}
