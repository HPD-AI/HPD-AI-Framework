namespace HPD.Agent.ToolHarness.Coding.TUI.SourcePresentation;

internal sealed record AnnotatedSourceDocument(
    string? DisplayPath,
    string? Language,
    IReadOnlyList<AnnotatedSourceHunk> Hunks,
    bool Truncated = false,
    string? TruncationReason = null);

internal sealed record AnnotatedSourceHunk(
    IReadOnlyList<AnnotatedSourceLine> Lines);

internal sealed record AnnotatedSourceLine(
    int LineNumber,
    string Text,
    IReadOnlyList<SourceAnnotation> Annotations,
    string? TrailingText = null,
    SourceLineEmphasis Emphasis = SourceLineEmphasis.None);

internal sealed record SourceAnnotation(
    string Marker,
    SourceAnnotationTone Tone,
    string? Description = null);

internal enum SourceAnnotationTone
{
    Neutral,
    Added,
    Removed,
    Information,
    Success,
    Warning,
    Error,
    Current
}

internal enum SourceLineEmphasis
{
    None,
    Subtle,
    Added,
    Removed,
    Warning,
    Error,
    Current
}
