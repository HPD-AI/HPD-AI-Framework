using HPD.Agent.ToolHarness.Coding.Debugging.Attributes;

namespace HPD.Agent.ToolHarness.Coding.Debugging.Adapters;

[HpdDebugAdapter("debugpy")]
[DebugAdapterLanguages("python")]
[DebugAdapterFileExtensions(".py", ".pyw")]
[DebugAdapterRootMarkers("pyproject.toml", "setup.py", "requirements.txt", "Pipfile", ".venv")]
[DebugAdapterTargetKinds(DebugTargetKind.SourceFile | DebugTargetKind.ProjectDirectory | DebugTargetKind.Process | DebugTargetKind.RegisteredRemoteEndpoint)]
[DebugAdapterCommandHint("python")]
[DebugAdapterArgumentHints("-m", "debugpy.adapter")]
[DebugAdapterInstallGuidance("debug.debugpy.install")]
[DebugAdapterFactory(typeof(DebugPyAdapterFactory))]
public sealed class DebugPyAdapterDeclaration;

[HpdDebugAdapter("netcoredbg")]
[DebugAdapterLanguages("csharp", "fsharp", "visualbasic")]
[DebugAdapterFileExtensions(".cs", ".csx", ".fs", ".fsx", ".vb")]
[DebugAdapterRootMarkers("*.sln", "*.slnx", "*.csproj", "*.fsproj", "global.json")]
[DebugAdapterTargetKinds(DebugTargetKind.Executable | DebugTargetKind.ProjectDirectory | DebugTargetKind.Process)]
[DebugAdapterCommandHint("netcoredbg")]
[DebugAdapterArgumentHints("--interpreter=vscode")]
[DebugAdapterInstallGuidance("debug.netcoredbg.install")]
public sealed class NetCoreDbgAdapterDeclaration;

[HpdDebugAdapter("gdb")]
[DebugAdapterLanguages("c", "cpp", "rust", "fortran")]
[DebugAdapterFileExtensions(".c", ".cc", ".cpp", ".cxx", ".h", ".hpp", ".rs", ".f", ".f90")]
[DebugAdapterRootMarkers("compile_commands.json", "CMakeLists.txt", "Makefile", "Cargo.toml")]
[DebugAdapterTargetKinds(DebugTargetKind.Executable | DebugTargetKind.Process | DebugTargetKind.RegisteredRemoteEndpoint)]
[DebugAdapterCommandHint("gdb")]
[DebugAdapterArgumentHints("-i", "dap")]
[DebugAdapterInstallGuidance("debug.gdb.install")]
public sealed class GdbAdapterDeclaration;

[HpdDebugAdapter("lldb-dap")]
[DebugAdapterLanguages("c", "cpp", "objective-c", "objective-cpp", "rust", "swift", "zig")]
[DebugAdapterFileExtensions(".c", ".cc", ".cpp", ".cxx", ".m", ".mm", ".rs", ".swift", ".zig")]
[DebugAdapterRootMarkers("compile_commands.json", "CMakeLists.txt", "Makefile", "Package.swift", "Cargo.toml", "build.zig")]
[DebugAdapterTargetKinds(DebugTargetKind.Executable | DebugTargetKind.Process)]
[DebugAdapterCommandHint("lldb-dap")]
[DebugAdapterInstallGuidance("debug.lldb-dap.install")]
public sealed class LldbDapAdapterDeclaration;

[HpdDebugAdapter("codelldb")]
[DebugAdapterLanguages("c", "cpp", "objective-c", "objective-cpp", "rust", "swift", "zig")]
[DebugAdapterFileExtensions(".c", ".cc", ".cpp", ".cxx", ".m", ".mm", ".rs", ".swift", ".zig")]
[DebugAdapterRootMarkers("compile_commands.json", "CMakeLists.txt", "Makefile", "Cargo.toml", "build.zig")]
[DebugAdapterTargetKinds(DebugTargetKind.Executable | DebugTargetKind.Process | DebugTargetKind.RegisteredRemoteEndpoint)]
[DebugAdapterCommandHint("codelldb")]
[DebugAdapterArgumentHints("--port", "0")]
[DebugAdapterInstallGuidance("debug.codelldb.install")]
[DebugAdapterDisabledByDefault]
[DebugAdapterFactory(typeof(CodeLldbAdapterFactory))]
public sealed class CodeLldbAdapterDeclaration;

[HpdDebugAdapter("delve")]
[DebugAdapterLanguages("go")]
[DebugAdapterFileExtensions(".go")]
[DebugAdapterRootMarkers("go.work", "go.mod", "go.sum")]
[DebugAdapterTargetKinds(DebugTargetKind.SourceFile | DebugTargetKind.ProjectDirectory | DebugTargetKind.Executable | DebugTargetKind.Process | DebugTargetKind.RegisteredRemoteEndpoint)]
[DebugAdapterCommandHint("dlv")]
[DebugAdapterArgumentHints("dap")]
[DebugAdapterInstallGuidance("debug.delve.install")]
[DebugAdapterFactory(typeof(DelveAdapterFactory))]
public sealed class DelveAdapterDeclaration;

[HpdDebugAdapter("javascript")]
[DebugAdapterLanguages("javascript", "typescript")]
[DebugAdapterFileExtensions(".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".mts", ".cts")]
[DebugAdapterRootMarkers("package.json", "tsconfig.json", "jsconfig.json", "deno.json", "deno.jsonc")]
[DebugAdapterTargetKinds(DebugTargetKind.SourceFile | DebugTargetKind.ProjectDirectory | DebugTargetKind.Process | DebugTargetKind.RegisteredRemoteEndpoint)]
[DebugAdapterCommandHint("js-debug-adapter")]
[DebugAdapterInstallGuidance("debug.javascript.install")]
[DebugAdapterExperimental]
[DebugAdapterFactory(typeof(JavaScriptDebugAdapterFactory))]
public sealed class JavaScriptDebugAdapterDeclaration;

[HpdDebugAdapter("rdbg")]
[DebugAdapterLanguages("ruby")]
[DebugAdapterFileExtensions(".rb", ".rake", ".gemspec")]
[DebugAdapterRootMarkers("Gemfile", "Rakefile", ".ruby-version")]
[DebugAdapterTargetKinds(DebugTargetKind.SourceFile | DebugTargetKind.ProjectDirectory | DebugTargetKind.Process | DebugTargetKind.RegisteredRemoteEndpoint)]
[DebugAdapterCommandHint("rdbg")]
[DebugAdapterArgumentHints("--open", "--command", "--")]
[DebugAdapterInstallGuidance("debug.rdbg.install")]
public sealed class RubyDebugAdapterDeclaration;
