using HPD.TUI.Core;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Views;

internal sealed class DiagnosticsTranscriptView : IComponent
{
    private const int MaxDiagnostics = 5;
    private readonly LanguageServerDiagnosticsReceivedEvent _diagnostics;

    public DiagnosticsTranscriptView(LanguageServerDiagnosticsReceivedEvent diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var rows = 1 + VisibleDiagnostics(_diagnostics).Take(MaxDiagnostics).Count();
        if (_diagnostics.DiagnosticsTruncated)
        {
            rows++;
        }

        return new Measurement(1, Math.Min(maxWidth, 100), rows);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
        => RenderDiagnosticsBody(_diagnostics, maxWidth, MaxDiagnostics, in context, ref output);

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    public static void RenderDiagnosticsBody(
        LanguageServerDiagnosticsReceivedEvent diagnostics,
        int maxWidth,
        int maxDiagnostics,
        in RenderContext context,
        ref SegmentWriter output)
    {
        output.Write("Diagnostics".AsSpan(), context.Theme.Border);

        var rendered = 0;
        foreach (var diagnostic in VisibleDiagnostics(diagnostics).Take(maxDiagnostics))
        {
            output.WriteLineBreak();
            RenderDiagnostic(diagnostic, maxWidth, in context, ref output);
            rendered++;
        }

        if (rendered == 0)
        {
            output.WriteLineBreak();
            output.Write("  no errors or warnings".AsSpan(), context.Theme.Border);
        }

        if (diagnostics.DiagnosticsTruncated)
        {
            output.WriteLineBreak();
            output.Write("  ⋮ diagnostics omitted".AsSpan(), context.Theme.Border);
        }
    }

    private static void RenderDiagnostic(
        LanguageServerDiagnosticSummary diagnostic,
        int maxWidth,
        in RenderContext context,
        ref SegmentWriter output)
    {
        var style = diagnostic.Severity switch
        {
            LanguageServerDiagnosticSeverity.Error => context.Theme.Error,
            LanguageServerDiagnosticSeverity.Warning => context.Theme.Warning,
            _ => context.Theme.Border
        };
        var marker = diagnostic.Severity == LanguageServerDiagnosticSeverity.Warning ? "⚠" : "■";
        var code = string.IsNullOrWhiteSpace(diagnostic.Code) ? diagnostic.Source.ToString() : diagnostic.Code;
        var prefix = $"  {marker} {code} {diagnostic.Line}:{diagnostic.Character} ";
        output.Write(prefix.AsSpan(), style);

        var remaining = Math.Max(0, maxWidth - prefix.Length);
        var message = Clip(diagnostic.Message, remaining);
        output.Write(message.AsSpan(), style);
    }

    private static IEnumerable<LanguageServerDiagnosticSummary> VisibleDiagnostics(
        LanguageServerDiagnosticsReceivedEvent diagnostics)
        => diagnostics.Diagnostics
            .Where(static diagnostic =>
                diagnostic.Severity is LanguageServerDiagnosticSeverity.Error or LanguageServerDiagnosticSeverity.Warning)
            .OrderBy(static diagnostic => diagnostic.Severity)
            .ThenBy(static diagnostic => diagnostic.Line)
            .ThenBy(static diagnostic => diagnostic.Character);

    private static string Clip(string text, int maxWidth)
    {
        if (maxWidth <= 0)
        {
            return "";
        }

        if (text.Length <= maxWidth)
        {
            return text;
        }

        if (maxWidth <= 3)
        {
            return new string('.', maxWidth);
        }

        return string.Concat(text.AsSpan(0, maxWidth - 3), "...");
    }
}
