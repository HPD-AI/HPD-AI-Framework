namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal sealed class CodingCommandArtifacts
{
    public string? StdoutArtifactPath { get; set; }

    public string? StderrArtifactPath { get; set; }

    public string? CombinedOutputArtifactPath { get; set; }

    public string? StdoutContentId { get; set; }

    public string? StderrContentId { get; set; }

    public string? CombinedOutputContentId { get; set; }

    public string? StdoutLocalPath { get; set; }

    public string? StderrLocalPath { get; set; }

    public string? CombinedOutputLocalPath { get; set; }

    public bool HasAny =>
        StdoutArtifactPath is not null ||
        StderrArtifactPath is not null ||
        CombinedOutputArtifactPath is not null ||
        StdoutContentId is not null ||
        StderrContentId is not null ||
        CombinedOutputContentId is not null ||
        StdoutLocalPath is not null ||
        StderrLocalPath is not null ||
        CombinedOutputLocalPath is not null;
}
