namespace HPD.Agent.Tests.TestToolkits;

/// <summary>
/// Mock Toolkit with multiple functions for testing selective registration
/// This Toolkit must be in its own file so the source generator can process it
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
/// Mock debugging Toolkit with skills that reference MockFileSystemTools functions
/// </summary>
public class MockDebuggingToolkit
{
    [Skill]
    public static Skill FileDebugging() => SkillFactory.Create(
        "FileDebugging",
        "Debug file system issues",
        "Use file operations to debug issues",
        "MockFileSystemTools.ReadFile",
        "MockFileSystemTools.WriteFile"
    );
}
