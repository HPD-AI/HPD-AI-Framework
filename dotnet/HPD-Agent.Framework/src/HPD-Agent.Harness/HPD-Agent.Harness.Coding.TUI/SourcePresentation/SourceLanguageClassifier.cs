namespace HPD.Agent.ToolHarness.Coding.TUI.SourcePresentation;

internal static class SourceLanguageClassifier
{
    public static string? FromPath(string? path)
        => Path.GetExtension(path)?.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".fs" or ".fsx" => "fsharp",
            ".vb" => "vb",
            ".py" => "python",
            ".js" or ".mjs" or ".cjs" => "javascript",
            ".ts" or ".mts" or ".cts" => "typescript",
            ".tsx" => "tsx",
            ".jsx" => "jsx",
            ".go" => "go",
            ".rs" => "rust",
            ".rb" => "ruby",
            ".java" => "java",
            ".kt" or ".kts" => "kotlin",
            ".c" or ".h" => "c",
            ".cc" or ".cpp" or ".cxx" or ".hpp" => "cpp",
            ".swift" => "swift",
            ".sh" or ".bash" or ".zsh" => "shell",
            ".ps1" => "powershell",
            ".json" => "json",
            ".xml" or ".csproj" or ".fsproj" or ".vbproj" => "xml",
            ".yml" or ".yaml" => "yaml",
            ".toml" => "toml",
            ".sql" => "sql",
            _ => null
        };
}
