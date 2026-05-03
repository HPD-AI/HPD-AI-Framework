using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPDOS.Harneses.Middleware;

/// <summary>
/// Config for <see cref="EnvironmentContextMiddleware"/>.
/// </summary>
public class EnvironmentContextConfig
{
    /// <summary>
    /// Directories the agent is allowed to write to.
    /// If null, defaults to the current working directory.
    /// </summary>
    public IReadOnlyList<string>? WritableRoots { get; init; }
}

/// <summary>
/// Harness-scoped middleware that injects environment context (cwd, shell, platform,
/// git status, writable roots) as XML into the conversation at the start of each turn.
/// On subsequent turns, re-injects only if the working directory has changed.
/// </summary>
public class EnvironmentContextMiddleware : IHarnessMiddleware
{
    private readonly IReadOnlyList<string>? _writableRoots;
    private EnvironmentContext? _lastContext;

    public EnvironmentContextMiddleware(EnvironmentContextConfig config)
    {
        _writableRoots = config.WritableRoots;
    }

    public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        var currentContext = EnvironmentContext.CreateCurrent(
            _writableRoots ?? [Directory.GetCurrentDirectory()],
            includeDirectoryListing: true);

        string? contextXml = null;

        if (_lastContext == null)
        {
            contextXml = currentContext.SerializeToXml();
        }
        else if (_lastContext.Cwd != currentContext.Cwd)
        {
            contextXml = $"[Environment Update]\n{currentContext.SerializeToXml()}";
        }

        if (contextXml != null)
        {
            var envMessage = new ChatMessage(ChatRole.User, contextXml);

            var insertIndex = 0;
            for (int i = 0; i < context.ConversationHistory.Count; i++)
            {
                if (context.ConversationHistory[i].Role == ChatRole.System)
                    insertIndex = i + 1;
                else
                    break;
            }

            context.ConversationHistory.Insert(insertIndex, envMessage);
        }

        _lastContext = currentContext;
        return Task.CompletedTask;
    }
}
