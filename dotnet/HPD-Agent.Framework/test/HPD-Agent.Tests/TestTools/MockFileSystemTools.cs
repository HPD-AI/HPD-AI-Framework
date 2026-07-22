namespace HPD.Agent.Tests.TestToolHarnesses;

/// <summary>
/// Mock ToolHarness with multiple functions for testing selective registration
/// This ToolHarness must be in its own file so the source generator can process it
/// </summary>
public class MockFileSystemTools
{
    [AIFunction, AIDescription("Read a file")]
    public string ReadFile(string path) => "file content";

    [AIFunction, AIDescription("Write a file")]
    public void WriteFile(string path, string content) { }

    [AIFunction, AIDescription("Delete a file")]
    public void DeleteFile(string path) { }

    [AIFunction, AIDescription("List files")]
    public string[] ListFiles(string path) => Array.Empty<string>();

    [AIFunction, AIDescription("Get file info")]
    public string GetFileInfo(string path) => "info";
}

/// <summary>
/// Mock debugging ToolHarness with skills that reference MockFileSystemTools functions
/// </summary>
public class MockDebuggingToolHarness
{
    [Skill]
    public static Skill FileDebugging() => Skill.Create(
        name: "FileDebugging",
        description: "Debug file system issues",
        instructions: SkillInstructions.FromText("Use file operations to debug issues"),
        capabilities:
        [
            SkillCapabilities.Function<MockFileSystemTools>(nameof(MockFileSystemTools.ReadFile)),
            SkillCapabilities.Function<MockFileSystemTools>(nameof(MockFileSystemTools.WriteFile))
        ]);
}
