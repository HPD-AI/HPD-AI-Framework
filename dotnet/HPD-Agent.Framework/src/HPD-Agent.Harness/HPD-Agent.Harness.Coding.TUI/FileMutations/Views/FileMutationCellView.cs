using HPD.Agent.ToolHarness.Coding.TUI.SourcePresentation;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.FileMutations.Views;

internal sealed class FileMutationCellView : HPD.TUI.Core.Component
{
    private const int MaxDiagnostics = 4;
    private readonly FileMutationCell _cell;
    private readonly CodingHarnessTuiTheme _theme;
    private readonly AnnotatedSourceView _source;

    public FileMutationCellView(FileMutationCell cell, CodingHarnessTuiTheme theme)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _source = new AnnotatedSourceView(CreateDocument(cell), theme);
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        var source = _source.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(maxWidth, context.Height));
        var rows = source.Height;

        if (ShouldRenderDiagnostics(_cell.Diagnostics, _cell.DiagnosticsTruncated))
        {
            rows += 1 + Math.Min(MaxDiagnostics, CountVisibleDiagnostics(_cell.Diagnostics));
            if (_cell.DiagnosticsTruncated)
            {
                rows++;
            }
        }

        return new Measurement(source.MinWidth, Math.Min(maxWidth, 100), rows);
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        if (maxWidth <= 0)
        {
            return;
        }

        output.Render(_source, in context, maxWidth);

        RenderDiagnosticsIfNeeded(in context, maxWidth, ref output);
    }

    public override bool HandleInput(in TuiInputEvent input)
    {
        return false;
    }

    private void RenderDiagnosticsIfNeeded(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        if (!ShouldRenderDiagnostics(_cell.Diagnostics, _cell.DiagnosticsTruncated))
        {
            return;
        }

        output.WriteLineBreak();
        output.WriteLineBreak();
        CodingDiagnosticsCellView.RenderDiagnosticsBody(
            _cell.Diagnostics,
            _cell.DiagnosticsTruncated,
            maxWidth,
            MaxDiagnostics,
            _theme,
            in context,
            ref output);
    }

    private static AnnotatedSourceDocument CreateDocument(FileMutationCell cell)
    {
        var hunks = new List<AnnotatedSourceHunk>(cell.Hunks.Count);
        foreach (var hunk in cell.Hunks)
        {
            var oldLine = hunk.OldStart;
            var newLine = hunk.NewStart;
            var lines = new List<AnnotatedSourceLine>(hunk.Lines.Count);
            foreach (var line in hunk.Lines)
            {
                var number = line.Kind switch
                {
                    FileMutationDiffLineKind.Removed => oldLine++,
                    FileMutationDiffLineKind.Added => newLine++,
                    _ => newLine++
                };
                if (line.Kind is FileMutationDiffLineKind.Context)
                {
                    oldLine++;
                }

                var (marker, tone, emphasis) = line.Kind switch
                {
                    FileMutationDiffLineKind.Added
                        => ("+", SourceAnnotationTone.Added, SourceLineEmphasis.Added),
                    FileMutationDiffLineKind.Removed
                        => ("-", SourceAnnotationTone.Removed, SourceLineEmphasis.Removed),
                    _ => (" ", SourceAnnotationTone.Neutral, SourceLineEmphasis.None)
                };
                lines.Add(new(
                    number,
                    line.Text,
                    [new SourceAnnotation(marker, tone)],
                    Emphasis: emphasis));
            }

            hunks.Add(new(lines));
        }

        return new(
            cell.DisplayPath,
            SourceLanguageClassifier.FromPath(cell.DisplayPath),
            hunks,
            cell.HunksTruncated,
            cell.HunksTruncated ? "diff truncated" : null);
    }

    private static bool ShouldRenderDiagnostics(
        IReadOnlyList<CodingDiagnosticLine> diagnostics,
        bool diagnosticsTruncated)
        => diagnosticsTruncated ||
           diagnostics.Any(static diagnostic =>
               diagnostic.Severity is CodingDiagnosticSeverity.Error or CodingDiagnosticSeverity.Warning);

    private static int CountVisibleDiagnostics(IReadOnlyList<CodingDiagnosticLine> diagnostics)
        => diagnostics.Count(static diagnostic =>
            diagnostic.Severity is CodingDiagnosticSeverity.Error or CodingDiagnosticSeverity.Warning);
}
