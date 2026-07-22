namespace HPDOS.ToolHarnesses.Middleware;

/// <summary>Provides shell-script language intelligence.</summary>
[HpdLanguageServer("bash-language-server")]
[LanguageServerExtensions(".sh", ".bash", ".zsh")]
[LanguageServerRootMarkers(".git")]
[LanguageServerExecutable("bash-language-server")]
[LanguageServerArguments("start")]
public sealed class BashLanguageServer;

/// <summary>Provides Lua language intelligence.</summary>
[HpdLanguageServer("lua-language-server")]
[LanguageServerExtensions(".lua")]
[LanguageServerLanguageIds(".lua", "lua")]
[LanguageServerRootMarkers(".luarc.json", ".luarc.jsonc", ".luacheckrc", ".stylua.toml", "stylua.toml")]
[LanguageServerExecutable("lua-language-server")]
public sealed class LuaLanguageServer;

/// <summary>Provides Terraform language intelligence.</summary>
[HpdLanguageServer("terraform-ls")]
[LanguageServerExtensions(".tf", ".tfvars")]
[LanguageServerLanguageIds(".tf", "terraform", ".tfvars", "terraform-vars")]
[LanguageServerRootMarkers(".terraform", "terraform.tfstate")]
[LanguageServerExecutable("terraform-ls")]
[LanguageServerArguments("serve")]
public sealed class TerraformLanguageServer;

/// <summary>Provides Helm chart language intelligence.</summary>
[HpdLanguageServer("helm-ls")]
[LanguageServerExtensions(".tpl", ".yaml", ".yml")]
[LanguageServerRootMarkers("Chart.yaml", "Chart.yml")]
[LanguageServerExecutable("helm_ls")]
[LanguageServerArguments("serve")]
public sealed class HelmLanguageServer;

/// <summary>Provides Markdown language intelligence through Marksman.</summary>
[HpdLanguageServer("marksman")]
[LanguageServerExtensions(".md", ".markdown")]
[LanguageServerLanguageIds(".md", "markdown", ".markdown", "markdown")]
[LanguageServerRootMarkers(".marksman.toml", ".git")]
[LanguageServerExecutable("marksman")]
[LanguageServerArguments("server")]
public sealed class MarksmanLanguageServer;

/// <summary>Provides LaTeX language intelligence through TexLab.</summary>
[HpdLanguageServer("texlab")]
[LanguageServerExtensions(".tex", ".bib", ".sty", ".cls")]
[LanguageServerRootMarkers(".latexmkrc", "latexmkrc", ".texlabroot", "texlabroot", "Tectonic.toml")]
[LanguageServerExecutable("texlab")]
public sealed class TexLabLanguageServer;

/// <summary>Provides Haskell language intelligence.</summary>
[HpdLanguageServer("haskell-language-server")]
[LanguageServerExtensions(".hs", ".lhs")]
[LanguageServerLanguageIds(".hs", "haskell", ".lhs", "haskell")]
[LanguageServerRootMarkers("stack.yaml", "cabal.project", "hie.yaml", "package.yaml", "*.cabal")]
[LanguageServerExecutable("haskell-language-server-wrapper")]
[LanguageServerArguments("--lsp")]
public sealed class HaskellLanguageServer;

/// <summary>Provides OCaml language intelligence.</summary>
[HpdLanguageServer("ocamllsp")]
[LanguageServerExtensions(".ml", ".mli", ".mll", ".mly")]
[LanguageServerRootMarkers("dune-project", "dune-workspace", "*.opam", ".ocamlformat")]
[LanguageServerExecutable("ocamllsp")]
public sealed class OcamlLanguageServer;

/// <summary>Provides Swift language intelligence through SourceKit-LSP.</summary>
[HpdLanguageServer("sourcekit-lsp")]
[LanguageServerExtensions(".swift")]
[LanguageServerLanguageIds(".swift", "swift")]
[LanguageServerRootMarkers("Package.swift", "*.xcodeproj", "*.xcworkspace", "project.yml", ".swiftpm")]
[LanguageServerExecutable("sourcekit-lsp")]
public sealed class SourceKitLanguageServer;

/// <summary>Provides Dart language intelligence through the Dart SDK.</summary>
[HpdLanguageServer("dart")]
[LanguageServerExtensions(".dart")]
[LanguageServerLanguageIds(".dart", "dart")]
[LanguageServerRootMarkers("pubspec.yaml", "pubspec.lock")]
[LanguageServerExecutable("dart")]
[LanguageServerArguments("language-server", "--protocol=lsp")]
public sealed class DartLanguageServer;

/// <summary>Provides Vim script language intelligence.</summary>
[HpdLanguageServer("vim-language-server")]
[LanguageServerExtensions(".vim", ".vimrc")]
[LanguageServerLanguageIds(".vim", "vim", ".vimrc", "vim")]
[LanguageServerRootMarkers(".git")]
[LanguageServerExecutable("vim-language-server")]
[LanguageServerArguments("--stdio")]
public sealed class VimLanguageServer;

/// <summary>Provides TLA+ language intelligence.</summary>
[HpdLanguageServer("tlaplus")]
[LanguageServerExtensions(".tla", ".tlaplus")]
[LanguageServerLanguageIds(".tla", "tlaplus", ".tlaplus", "tlaplus")]
[LanguageServerRootMarkers("*.tla")]
[LanguageServerExecutable("tlapm_lsp")]
[LanguageServerArguments("--stdio")]
public sealed class TlaPlusLanguageServer;
