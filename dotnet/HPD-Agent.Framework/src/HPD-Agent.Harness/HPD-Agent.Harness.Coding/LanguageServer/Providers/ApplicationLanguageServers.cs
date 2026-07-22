namespace HPDOS.ToolHarnesses.Middleware;

/// <summary>Provides Python type intelligence through Pyright.</summary>
[HpdLanguageServer("pyright")]
[LanguageServerExtensions(".py", ".pyi")]
[LanguageServerLanguageIds(".py", "python", ".pyi", "python")]
[LanguageServerRootMarkers("pyproject.toml", "pyrightconfig.json", "setup.py", "setup.cfg", "requirements.txt", "Pipfile")]
[LanguageServerExecutable("pyright-langserver")]
[LanguageServerArguments("--stdio")]
public sealed class PyrightLanguageServer;

/// <summary>Provides an alternative Python language server through python-lsp-server.</summary>
[HpdLanguageServer("pylsp")]
[LanguageServerExtensions(".py")]
[LanguageServerLanguageIds(".py", "python")]
[LanguageServerRootMarkers("pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "Pipfile")]
[LanguageServerExecutable("pylsp")]
[LanguageServerDisabledByDefault]
public sealed class PythonLspLanguageServer;

/// <summary>Provides an alternative Python type checker through BasedPyright.</summary>
[HpdLanguageServer("basedpyright")]
[LanguageServerExtensions(".py", ".pyi")]
[LanguageServerLanguageIds(".py", "python", ".pyi", "python")]
[LanguageServerRootMarkers("pyproject.toml", "pyrightconfig.json", "setup.py", "requirements.txt")]
[LanguageServerExecutable("basedpyright-langserver")]
[LanguageServerArguments("--stdio")]
[LanguageServerDisabledByDefault]
public sealed class BasedPyrightLanguageServer;

/// <summary>Provides Python diagnostics and formatting through Ruff.</summary>
[HpdLanguageServer("ruff")]
[LanguageServerExtensions(".py", ".pyi")]
[LanguageServerLanguageIds(".py", "python", ".pyi", "python")]
[LanguageServerRootMarkers("pyproject.toml", "ruff.toml", ".ruff.toml")]
[LanguageServerExecutable("ruff")]
[LanguageServerArguments("server")]
public sealed class RuffLanguageServer;

/// <summary>Provides Java language intelligence through Eclipse JDT LS.</summary>
[HpdLanguageServer("jdtls")]
[LanguageServerExtensions(".java")]
[LanguageServerLanguageIds(".java", "java")]
[LanguageServerRootMarkers("pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle", ".project")]
[LanguageServerExecutable("jdtls")]
public sealed class JavaLanguageServer;

/// <summary>Provides Kotlin language intelligence.</summary>
[HpdLanguageServer("kotlin-lsp")]
[LanguageServerExtensions(".kt", ".kts")]
[LanguageServerLanguageIds(".kt", "kotlin", ".kts", "kotlin")]
[LanguageServerRootMarkers("build.gradle", "build.gradle.kts", "pom.xml", "settings.gradle", "settings.gradle.kts")]
[LanguageServerExecutable("kotlin-lsp")]
[LanguageServerArguments("--stdio")]
public sealed class KotlinLanguageServer;

/// <summary>Provides Scala language intelligence through Metals.</summary>
[HpdLanguageServer("metals")]
[LanguageServerExtensions(".scala", ".sbt", ".sc")]
[LanguageServerLanguageIds(".scala", "scala", ".sbt", "scala", ".sc", "scala")]
[LanguageServerRootMarkers("build.sbt", "build.sc", "build.gradle", "pom.xml")]
[LanguageServerExecutable("metals")]
public sealed class MetalsLanguageServer;

/// <summary>Provides Ruby language intelligence through Ruby LSP.</summary>
[HpdLanguageServer("ruby-lsp")]
[LanguageServerExtensions(".rb", ".rake", ".gemspec", ".erb")]
[LanguageServerLanguageIds(".rb", "ruby", ".rake", "ruby", ".gemspec", "ruby", ".erb", "erb")]
[LanguageServerRootMarkers("Gemfile", ".ruby-version", ".ruby-gemset")]
[LanguageServerExecutable("ruby-lsp")]
public sealed class RubyLanguageServer;

/// <summary>Provides alternative Ruby language intelligence through Solargraph.</summary>
[HpdLanguageServer("solargraph")]
[LanguageServerExtensions(".rb", ".rake", ".gemspec")]
[LanguageServerRootMarkers("Gemfile", ".solargraph.yml", "Rakefile")]
[LanguageServerExecutable("solargraph")]
[LanguageServerArguments("stdio")]
[LanguageServerDisabledByDefault]
public sealed class SolargraphLanguageServer;

/// <summary>Provides Ruby diagnostics through RuboCop's LSP mode.</summary>
[HpdLanguageServer("rubocop")]
[LanguageServerExtensions(".rb", ".rake")]
[LanguageServerRootMarkers(".rubocop.yml", "Gemfile")]
[LanguageServerExecutable("rubocop")]
[LanguageServerArguments("--lsp")]
public sealed class RubocopLanguageServer;

/// <summary>Provides PHP language intelligence through Intelephense.</summary>
[HpdLanguageServer("intelephense")]
[LanguageServerExtensions(".php", ".phtml")]
[LanguageServerLanguageIds(".php", "php", ".phtml", "php")]
[LanguageServerRootMarkers("composer.json", "composer.lock", ".git")]
[LanguageServerExecutable("intelephense")]
[LanguageServerArguments("--stdio")]
public sealed class IntelephenseLanguageServer;

/// <summary>Provides alternative PHP language intelligence through Phpactor.</summary>
[HpdLanguageServer("phpactor")]
[LanguageServerExtensions(".php")]
[LanguageServerLanguageIds(".php", "php")]
[LanguageServerRootMarkers("composer.json", ".phpactor.json", ".phpactor.yml")]
[LanguageServerExecutable("phpactor")]
[LanguageServerArguments("language-server")]
[LanguageServerDisabledByDefault]
public sealed class PhpactorLanguageServer;

/// <summary>Provides Elixir language intelligence through ElixirLS.</summary>
[HpdLanguageServer("elixir-ls")]
[LanguageServerExtensions(".ex", ".exs", ".heex", ".eex")]
[LanguageServerRootMarkers("mix.exs", "mix.lock")]
[LanguageServerExecutable("elixir-ls")]
public sealed class ElixirLanguageServer;

/// <summary>Provides alternative Elixir language intelligence through Expert.</summary>
[HpdLanguageServer("expert")]
[LanguageServerExtensions(".ex", ".exs", ".heex", ".eex")]
[LanguageServerRootMarkers("mix.exs", "mix.lock")]
[LanguageServerExecutable("expert")]
[LanguageServerArguments("--stdio")]
[LanguageServerDisabledByDefault]
public sealed class ExpertLanguageServer;

/// <summary>Provides Erlang language intelligence.</summary>
[HpdLanguageServer("erlang-ls")]
[LanguageServerExtensions(".erl", ".hrl")]
[LanguageServerLanguageIds(".erl", "erlang", ".hrl", "erlang")]
[LanguageServerRootMarkers("rebar.config", "erlang.mk", "rebar.lock")]
[LanguageServerExecutable("erlang_ls")]
public sealed class ErlangLanguageServer;

/// <summary>Provides Gleam language intelligence.</summary>
[HpdLanguageServer("gleam")]
[LanguageServerExtensions(".gleam")]
[LanguageServerLanguageIds(".gleam", "gleam")]
[LanguageServerRootMarkers("gleam.toml")]
[LanguageServerExecutable("gleam")]
[LanguageServerArguments("lsp")]
public sealed class GleamLanguageServer;
