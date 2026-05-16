namespace HPDOS.Harneses.Middleware;

[HpdLanguageServer("deno")]
[LanguageServerExtensions(".ts", ".tsx", ".js", ".jsx", ".mjs")]
[LanguageServerLanguageIds(
    ".ts", "typescript",
    ".tsx", "typescriptreact",
    ".js", "javascript",
    ".jsx", "javascriptreact",
    ".mjs", "javascript")]
[LanguageServerRootMarkers("deno.json", "deno.jsonc")]
public sealed partial class DenoLanguageServer : ILanguageServerProvider
{
    public ValueTask<string?> ResolveRootAsync(
        LanguageServerRootContext context,
        CancellationToken cancellationToken = default)
    {
        var directory = File.Exists(context.Path)
            ? Path.GetDirectoryName(context.Path)
            : context.Path;

        while (!string.IsNullOrEmpty(directory) && IsInsideWorkspace(directory, context.WorkspaceRoot))
        {
            if (File.Exists(Path.Combine(directory, "deno.json")) ||
                File.Exists(Path.Combine(directory, "deno.jsonc")))
            {
                return ValueTask.FromResult<string?>(directory);
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent == directory)
                break;

            directory = parent;
        }

        return ValueTask.FromResult<string?>(null);
    }

    public async ValueTask<LanguageServerLaunchDescriptor?> ResolveLaunchAsync(
        LanguageServerLaunchContext context,
        CancellationToken cancellationToken = default)
    {
        var executable = await context.ToolResolver
            .FindExecutableAsync("deno", context.Root, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(executable))
            return null;

        return new LanguageServerLaunchDescriptor
        {
            FileName = executable,
            Arguments = ["lsp"],
            WorkingDirectory = context.Root
        };
    }

    public ValueTask<LanguageServerInitialization> CreateInitializationAsync(
        LanguageServerInitializationContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new LanguageServerInitialization());

    private static bool IsInsideWorkspace(string directory, string workspaceRoot)
    {
        var relative = Path.GetRelativePath(workspaceRoot, directory);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }
}
