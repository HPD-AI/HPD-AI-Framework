namespace HPDOS.ToolHarnesses.Middleware;

[HpdLanguageServer("typescript")]
[LanguageServerExtensions(".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".mts", ".cts")]
[LanguageServerLanguageIds(
    ".ts", "typescript",
    ".tsx", "typescriptreact",
    ".js", "javascript",
    ".jsx", "javascriptreact",
    ".mjs", "javascript",
    ".cjs", "javascript",
    ".mts", "typescript",
    ".cts", "typescript")]
[LanguageServerRootMarkers("package.json", "tsconfig.json", "jsconfig.json", "package-lock.json", "bun.lockb", "bun.lock", "pnpm-lock.yaml", "yarn.lock")]
[LanguageServerExcludeRootMarkers("deno.json", "deno.jsonc")]
public sealed partial class TypeScriptLanguageServer : ILanguageServerProvider
{
    public string ConfigurationIdentity => "typescript-language-server:v1";

    private static readonly StaticCommandLanguageServerProvider RootProvider = new(
        ["package.json", "tsconfig.json", "jsconfig.json", "package-lock.json", "bun.lockb", "bun.lock", "pnpm-lock.yaml", "yarn.lock"],
        "typescript-language-server",
        ["--stdio"],
        ["deno.json", "deno.jsonc"]);

    public ValueTask<string?> ResolveRootAsync(
        LanguageServerRootContext context,
        CancellationToken cancellationToken = default)
        => RootProvider.ResolveRootAsync(context, cancellationToken);

    public async ValueTask<LanguageServerLaunchDescriptor?> ResolveLaunchAsync(
        LanguageServerLaunchContext context,
        CancellationToken cancellationToken = default)
    {
        var tsserver = await context.ToolResolver
            .FindNodeModuleAsync("typescript/lib/tsserver.js", context.Root, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(tsserver))
            return null;

        var executable = await context.ToolResolver
            .FindExecutableAsync("typescript-language-server", context.Root, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(executable))
            return null;

        return new LanguageServerLaunchDescriptor
        {
            FileName = executable,
            Arguments = ["--stdio"],
            WorkingDirectory = context.Root,
            InitializationOptions = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["tsserver"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["path"] = tsserver
                }
            }
        };
    }

    public ValueTask<LanguageServerInitialization> CreateInitializationAsync(
        LanguageServerInitializationContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new LanguageServerInitialization());
}
