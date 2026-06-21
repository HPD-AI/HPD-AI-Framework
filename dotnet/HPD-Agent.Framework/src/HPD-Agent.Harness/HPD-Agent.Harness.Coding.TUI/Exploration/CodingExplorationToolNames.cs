namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

internal static class CodingExplorationToolNames
{
    public const string ReadFile = nameof(ReadFile);
    public const string Grep = nameof(Grep);
    public const string GlobSearch = nameof(GlobSearch);
    public const string ListDirectory = nameof(ListDirectory);

    public static bool IsExplorationTool(string? toolName)
        => toolName is not null && (
            string.Equals(toolName, ReadFile, StringComparison.Ordinal) ||
            string.Equals(toolName, Grep, StringComparison.Ordinal) ||
            string.Equals(toolName, GlobSearch, StringComparison.Ordinal) ||
            string.Equals(toolName, ListDirectory, StringComparison.Ordinal));
}
