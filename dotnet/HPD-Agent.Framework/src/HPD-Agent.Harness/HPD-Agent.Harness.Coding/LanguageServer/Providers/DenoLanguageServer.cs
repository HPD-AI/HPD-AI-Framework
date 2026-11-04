namespace HPDOS.ToolHarnesses.Middleware;

using HPD.Environment.Contracts;

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
    public string ConfigurationIdentity => "deno-language-server:v1";

    public async ValueTask<string?> ResolveRootAsync(
        LanguageServerRootContext context,
        CancellationToken cancellationToken = default)
    {
        WorkspacePath start =
            await LanguageServerWorkspaceAccess.ResolveAsync(
                context.Workspace,
                context.Path,
                cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "The Deno document is outside the session workspace.");
        WorkspaceFileInfo info =
            await context.Workspace.Files.GetInfoAsync(
                start,
                cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "The Deno document disappeared.");
        WorkspacePath? directory = info.Kind ==
            WorkspaceEntryKind.Directory
                ? start
                : LanguageServerWorkspaceAccess.Parent(start);
        WorkspacePath workspaceRoot =
            HPD.Agent.ToolHarness.Coding.CodingWorkspaceAccess.Resolve(
                context.Workspace,
                context.WorkspaceRoot);

        while (directory is { } current)
        {
            if (await LanguageServerWorkspaceAccess.MarkerExistsAsync(
                    context.Workspace,
                    current,
                    "deno.json",
                    cancellationToken).ConfigureAwait(false) ||
                await LanguageServerWorkspaceAccess.MarkerExistsAsync(
                    context.Workspace,
                    current,
                    "deno.jsonc",
                    cancellationToken).ConfigureAwait(false))
            {
                return LanguageServerWorkspaceAccess.ToExecutionPath(
                    context.Workspace,
                    current);
            }
            if (current.Equals(workspaceRoot))
            {
                break;
            }
            directory = LanguageServerWorkspaceAccess.Parent(current);
        }

        return null;
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
}
