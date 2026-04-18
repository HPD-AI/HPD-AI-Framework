using System.Collections.Immutable;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPDOS.Toolkits.Middleware;

/// <summary>
/// Toolkit-scoped middleware that injects directory context files (CONTEXT.md, HPDOS.md)
/// into the conversation when the agent reads files from a directory.
///
/// Pipeline:
///   BeforeFunctionAsync   — tracks directories touched by ReadFile into <see cref="FileAccessState"/>
///   BeforeMessageTurnAsync — on the next turn, finds and injects context files for those directories, then clears state
///
/// Context file discovery walks up from the accessed directory toward CWD,
/// stopping as soon as a context file is found (closest-wins) or CWD is reached.
/// </summary>
public class FileContextInjectionMiddleware : IToolkitMiddleware
{
    /// <summary>
    /// Ordered list of filenames to look for in each directory.
    /// First match wins.
    /// </summary>
    private static readonly string[] ContextFileNames = ["CONTEXT.md", "HPDOS.md", ".context.md"];

    // ─── Track phase: runs for every function call in the iteration loop ───

    public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
    {
        if (context.Function?.Name != "ReadFile")
            return Task.CompletedTask;

        if (!context.Arguments.TryGetValue("filePath", out var raw) || raw is not string filePath)
            return Task.CompletedTask;

        try
        {
            // Mirror ReadFileTool's ResolvePath: relative → absolute
            var resolved = Path.IsPathFullyQualified(filePath)
                ? Path.GetFullPath(filePath)
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), filePath));

            var dir = Path.GetDirectoryName(resolved);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return Task.CompletedTask;

            context.UpdateMiddlewareState<FileAccessState>(s =>
                s with { AccessedDirectories = s.AccessedDirectories.Add(dir) });
        }
        catch
        {
            // Never break the tool pipeline — context injection is supplementary
        }

        return Task.CompletedTask;
    }

    // ─── Inject phase: runs once at the start of the next turn ───

    public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        var state = context.GetMiddlewareState<FileAccessState>();
        if (state is null || state.AccessedDirectories.IsEmpty)
            return;

        // Insert after system messages, same convention as EnvironmentContextMiddleware
        var insertIndex = context.ConversationHistory
            .TakeWhile(m => m.Role == ChatRole.System)
            .Count();

        foreach (var dir in state.AccessedDirectories)
        {
            var contextFile = FindContextFile(dir);
            if (contextFile is null)
                continue;

            var content = await File.ReadAllTextAsync(contextFile, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var msg = new ChatMessage(
                ChatRole.User,
                $"[Directory Context: {dir}]\n{content}");

            context.ConversationHistory.Insert(insertIndex, msg);
        }

        // Clear state so we don't re-inject the same directories next turn
        context.UpdateMiddlewareState<FileAccessState>(_ => new FileAccessState());
    }

    // ─── Context file discovery ───

    /// <summary>
    /// Walks up from <paramref name="startDir"/> toward CWD looking for a context file.
    /// Returns the first match found (closest directory wins), or null if none found.
    /// Never walks above CWD to avoid pulling in unrelated project context.
    /// </summary>
    private static string? FindContextFile(string startDir)
    {
        var cwd = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(startDir);

        while (dir is not null)
        {
            foreach (var name in ContextFileNames)
            {
                var candidate = Path.Combine(dir.FullName, name);
                if (File.Exists(candidate))
                    return candidate;
            }

            // Stop at CWD — don't walk above the project root
            if (string.Equals(dir.FullName, cwd, StringComparison.OrdinalIgnoreCase))
                break;

            dir = dir.Parent;
        }

        return null;
    }
}
