namespace HPDOS.ToolHarnesses.Middleware;

/// <summary>Provides C# language intelligence through csharp-ls.</summary>
[HpdLanguageServer("csharp")]
[LanguageServerExtensions(".cs", ".csx")]
[LanguageServerLanguageIds(".cs", "csharp", ".csx", "csharp")]
[LanguageServerRootMarkers("*.sln", "*.slnx", "*.csproj", "Directory.Build.props", "global.json")]
[LanguageServerExecutable("csharp-ls")]
public sealed class CSharpLanguageServer;

/// <summary>Provides Rust language intelligence through rust-analyzer.</summary>
[HpdLanguageServer("rust-analyzer")]
[LanguageServerExtensions(".rs")]
[LanguageServerLanguageIds(".rs", "rust")]
[LanguageServerRootMarkers("Cargo.toml", "rust-analyzer.toml")]
[LanguageServerExecutable("rust-analyzer")]
public sealed class RustAnalyzerLanguageServer;

/// <summary>Provides Go language intelligence through gopls.</summary>
[HpdLanguageServer("gopls")]
[LanguageServerExtensions(".go", ".mod", ".sum")]
[LanguageServerLanguageIds(".go", "go", ".mod", "gomod", ".sum", "gosum")]
[LanguageServerRootMarkers("go.work", "go.mod", "go.sum")]
[LanguageServerExecutable("gopls")]
[LanguageServerArguments("serve")]
public sealed class GoplsLanguageServer;

/// <summary>Provides C-family language intelligence through clangd.</summary>
[HpdLanguageServer("clangd")]
[LanguageServerExtensions(".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx", ".m", ".mm")]
[LanguageServerRootMarkers("compile_commands.json", "CMakeLists.txt", ".clangd", ".clang-format", "Makefile")]
[LanguageServerExecutable("clangd")]
[LanguageServerArguments("--background-index", "--clang-tidy", "--header-insertion=iwyu")]
public sealed class ClangdLanguageServer;

/// <summary>Provides Zig language intelligence through zls.</summary>
[HpdLanguageServer("zls")]
[LanguageServerExtensions(".zig")]
[LanguageServerLanguageIds(".zig", "zig")]
[LanguageServerRootMarkers("build.zig", "build.zig.zon", "zls.json")]
[LanguageServerExecutable("zls")]
public sealed class ZigLanguageServer;

/// <summary>Provides Odin language intelligence through OLS.</summary>
[HpdLanguageServer("ols")]
[LanguageServerExtensions(".odin")]
[LanguageServerLanguageIds(".odin", "odin")]
[LanguageServerRootMarkers("ols.json", ".git")]
[LanguageServerExecutable("ols")]
public sealed class OdinLanguageServer;

/// <summary>Provides Nix language intelligence through nixd.</summary>
[HpdLanguageServer("nixd")]
[LanguageServerExtensions(".nix")]
[LanguageServerLanguageIds(".nix", "nix")]
[LanguageServerRootMarkers("flake.nix", "default.nix", "shell.nix")]
[LanguageServerExecutable("nixd")]
public sealed class NixdLanguageServer;

/// <summary>Provides alternative Nix language intelligence through nil.</summary>
[HpdLanguageServer("nil")]
[LanguageServerExtensions(".nix")]
[LanguageServerLanguageIds(".nix", "nix")]
[LanguageServerRootMarkers("flake.nix", "default.nix", "shell.nix")]
[LanguageServerExecutable("nil")]
[LanguageServerDisabledByDefault]
public sealed class NilLanguageServer;
