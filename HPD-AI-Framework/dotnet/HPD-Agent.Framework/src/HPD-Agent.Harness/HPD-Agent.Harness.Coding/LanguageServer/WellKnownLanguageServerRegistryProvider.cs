namespace HPDOS.Harneses.Middleware;

public sealed class WellKnownLanguageServerRegistryProvider : ILanguageServerRegistryProvider
{
    public static WellKnownLanguageServerRegistryProvider Instance { get; } = new();

    private static readonly LanguageServerDefinition[] Definitions =
    [
        Create(
            "csharp",
            [".cs"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".cs"] = "csharp"
            },
            ["*.sln", "*.slnx", "*.csproj", "Directory.Build.props", "global.json"],
            "csharp-ls"),

        Create(
            "typescript",
            [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".mts", ".cts"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".ts"] = "typescript",
                [".tsx"] = "typescriptreact",
                [".js"] = "javascript",
                [".jsx"] = "javascriptreact",
                [".mjs"] = "javascript",
                [".cjs"] = "javascript",
                [".mts"] = "typescript",
                [".cts"] = "typescript"
            },
            ["package-lock.json", "bun.lockb", "bun.lock", "pnpm-lock.yaml", "yarn.lock"],
            "typescript-language-server",
            ["--stdio"]),

        Create(
            "pyright",
            [".py", ".pyi"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".py"] = "python",
                [".pyi"] = "python"
            },
            ["pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "Pipfile", "uv.lock"],
            "pyright-langserver",
            ["--stdio"]),

        Create(
            "rust-analyzer",
            [".rs"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".rs"] = "rust"
            },
            ["Cargo.toml"],
            "rust-analyzer"),

        Create(
            "gopls",
            [".go"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [".go"] = "go"
            },
            ["go.work", "go.mod"],
            "gopls")
    ];

    private WellKnownLanguageServerRegistryProvider()
    {
    }

    public IEnumerable<LanguageServerDefinition> GetAll() => Definitions;

    private static LanguageServerDefinition Create(
        string id,
        IReadOnlyList<string> extensions,
        IReadOnlyDictionary<string, string> languageIds,
        IReadOnlyList<string> rootMarkers,
        string executable,
        IReadOnlyList<string>? arguments = null)
        => new()
        {
            Id = id,
            Extensions = extensions,
            LanguageIds = languageIds,
            Provider = new StaticCommandLanguageServerProvider(rootMarkers, executable, arguments)
        };
}
