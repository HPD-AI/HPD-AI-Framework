using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

internal sealed record CodingExplorationArgs(
    string? Path,
    string? Pattern,
    int? Offset,
    int? Limit,
    bool? Recursive);

internal static class CodingExplorationArgsParser
{
    public static CodingExplorationArgs Parse(string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
        {
            return new CodingExplorationArgs(null, null, null, null, null);
        }

        var arguments = ToolCallArguments.Parse(argsJson);
        if (!arguments.IsObject)
            return new CodingExplorationArgs(null, null, null, null, null);

        return new CodingExplorationArgs(
            ReadString(arguments.Root, "path"),
            ReadString(arguments.Root, "pattern"),
            ReadInt(arguments.Root, "offset"),
            ReadInt(arguments.Root, "limit"),
            ReadBool(arguments.Root, "recursive"));
    }

    private static string? ReadString(ToolArgumentValue root, string name)
        => root.TryGetString(name, out var value) ? value : null;

    private static int? ReadInt(ToolArgumentValue root, string name)
        => root.TryGetInt32(name, out var value) ? value : null;

    private static bool? ReadBool(ToolArgumentValue root, string name)
        => root.TryGetBoolean(name, out var value) ? value : null;
}
