using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Views;

internal sealed class CodingDiagnosticsCellView : HPD.TUI.Core.Component
{
    private const int MaxDiagnostics = 5;
    private readonly CodingDiagnosticsCell _cell;
    private readonly CodingHarnessTuiTheme _theme;

    public CodingDiagnosticsCellView(CodingDiagnosticsCell cell, CodingHarnessTuiTheme theme)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        var rows = 1 + VisibleDiagnostics(_cell.Diagnostics).Take(MaxDiagnostics).Count();
        if (_cell.Truncated)
        {
            rows++;
        }

        return new Measurement(1, Math.Min(maxWidth, 100), rows);
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
        => RenderDiagnosticsBody(_cell.Diagnostics, _cell.Truncated, output.MaxWidth, MaxDiagnostics, _theme, in context, ref output);

    public override bool HandleInput(in TuiInputEvent input)
    {
        return false;
    }

    public static void RenderDiagnosticsBody(
        IReadOnlyList<CodingDiagnosticLine> diagnostics,
        bool diagnosticsTruncated,
        int maxWidth,
        int maxDiagnostics,
        CodingHarnessTuiTheme theme,
        in RenderContext context,
        ref DisplayListBuilder output)
    {
        output.Write("Diagnostics".AsSpan(), theme.ResolveMuted(context.Theme));

        var rendered = 0;
        foreach (var diagnostic in VisibleDiagnostics(diagnostics).Take(maxDiagnostics))
        {
            output.WriteLineBreak();
            RenderDiagnostic(diagnostic, maxWidth, theme, in context, ref output);
            rendered++;
        }

        if (rendered == 0)
        {
            output.WriteLineBreak();
            output.Write("  no errors or warnings".AsSpan(), theme.ResolveMuted(context.Theme));
        }

        if (diagnosticsTruncated)
        {
            output.WriteLineBreak();
            output.Write("  ⋮ diagnostics omitted".AsSpan(), theme.ResolveMuted(context.Theme));
        }
    }

    private static void RenderDiagnostic(
        CodingDiagnosticLine diagnostic,
        int maxWidth,
        CodingHarnessTuiTheme theme,
        in RenderContext context,
        ref DisplayListBuilder output)
    {
        var style = diagnostic.Severity switch
        {
            CodingDiagnosticSeverity.Error => theme.ResolveDiagnosticError(context.Theme),
            CodingDiagnosticSeverity.Warning => theme.ResolveDiagnosticWarning(context.Theme),
            _ => theme.ResolveMuted(context.Theme)
        };
        var marker = diagnostic.Severity == CodingDiagnosticSeverity.Warning ? "⚠" : "■";
        var code = string.IsNullOrWhiteSpace(diagnostic.Code) ? diagnostic.Source : diagnostic.Code;
        var prefix = $"  {marker} {code} {diagnostic.Line}:{diagnostic.Character} ";
        output.Write(prefix.AsSpan(), style);

        var remaining = Math.Max(0, maxWidth - prefix.Length);
        var message = Clip(diagnostic.Message, remaining);
        output.Write(message.AsSpan(), style);
    }

    private static IEnumerable<CodingDiagnosticLine> VisibleDiagnostics(
        IReadOnlyList<CodingDiagnosticLine> diagnostics)
        => diagnostics
            .Where(static diagnostic =>
                diagnostic.Severity is CodingDiagnosticSeverity.Error or CodingDiagnosticSeverity.Warning)
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
