# HPD-Agent.ToolHarness.Coding

Coding toolharness for HPD-Agent. Provides AI functions for codebase inspection, symbol search, and implementation planning.

## Install

```bash
dotnet add package HPD-Agent.ToolHarness.Coding
```

## Use When

Use this package when you need this HPD Agent capability in an agent application.

## Language servers

The coding harness discovers language servers from a compile-time generated catalog. A server is considered for a file when its declared extension matches and one of its declared root markers exists in the file's workspace ancestry. A declaration without root markers supports loose workspace files. The server starts only when its executable can be resolved from an ecosystem-local bin directory or `PATH`.

Built-in declarations cover 51 servers across .NET, TypeScript and JavaScript, systems languages, Python, JVM languages, web frameworks, data formats, scripting languages, infrastructure tooling, and documentation formats. Alternative semantic servers such as BasedPyright, pylsp, Solargraph, Phpactor, Expert, and nil are disabled by default.

Enable or disable individual declarations with `LanguageServerOptions`:

```csharp
var options = new LanguageServerOptions
{
    EnabledServers = new HashSet<string>(StringComparer.Ordinal)
    {
        "basedpyright"
    },
    DisabledServers = new HashSet<string>(StringComparer.Ordinal)
    {
        "pyright"
    }
};
```

Simple built-ins use attributed declarations and are validated by the coding source generator:

```csharp
[HpdLanguageServer("rust-analyzer")]
[LanguageServerExtensions(".rs")]
[LanguageServerLanguageIds(".rs", "rust")]
[LanguageServerRootMarkers("Cargo.toml", "rust-analyzer.toml")]
[LanguageServerExecutable("rust-analyzer")]
public sealed class RustAnalyzerLanguageServer;
```

Implement `ILanguageServerProvider` when a server needs custom root discovery, executable resolution, launch configuration, or initialization. TypeScript and Deno are built-in examples of specialized providers.

Executable discovery checks `node_modules/.bin`, Python virtual-environment bins, Ruby bundle bins, project-local `bin`, and `PATH`. The harness does not install language servers automatically.

When the coding TUI is installed with `AddCodingHarnessTui()`, live language-server state is shown in the footer and `/lsp` opens an inspect-only page containing each activated server, its root, status, and failure message. Status snapshots use the normal persisted event pipeline, but historical snapshots are deliberately not projected as live processes after hydration or a backend restart. Persisted diagnostics continue to replay normally.

Real server smoke tests are opt-in. Set `HPD_LSP_SMOKE=1` for TypeScript (and optionally `HPD_LSP_SMOKE_TYPESCRIPT_ROOT`) or `HPD_LSP_SMOKE_CSHARP=1` with `csharp-ls` available on `PATH` for the C# startup, diagnostics-correction, UTF-16 document, and shutdown lifecycle test.
