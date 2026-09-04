using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI;

/// <summary>
/// Adds framework-owned subagent discovery and navigation to an agent TUI.
/// </summary>
public static class SubAgentTuiExtensions
{
    /// <summary>
    /// Registers the <c>/subagents</c> command. Selecting a child switches the complete
    /// runtime scope to its default agent, session, and durable thread.
    /// </summary>
    /// <param name="builder">The TUI builder to extend.</param>
    /// <returns>The same builder.</returns>
    public static HpdAgentTuiBuilder AddSubAgentTui(
        this HpdAgentTuiBuilder builder,
        Action<AgentTuiSubAgentMenuOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new AgentTuiSubAgentMenuOptions();
        configure?.Invoke(options);
        var actions = options.Actions.ToArray();
        return builder.TryAddSlashCommand(new HpdAgentTuiCommandDescriptor(
            "subagents",
            context => ExecuteAsync(context, actions))
        {
            Title = "/subagents",
            Description = "Open a durable subagent conversation from the current session."
        });
    }

    private static async ValueTask ExecuteAsync(
        AgentTuiCommandContext context,
        IReadOnlyList<AgentTuiSubAgentMenuAction> actions)
    {
        AgentTuiSubAgentInfo[] children = [];
        if (context.Runtime is IAgentTuiSessionThreadRuntime runtime)
        {
            children = (await runtime.ListSubAgentsAsync(
                    context.Scope.SessionId, context.Scope.ThreadId, CancellationToken.None)
                .ConfigureAwait(false)).ToArray();
        }

        if (actions.Count == 0 && children.Length == 0)
        {
            AddNotice(
                context,
                context.Runtime is IAgentTuiSessionThreadRuntime
                    ? "No subagents have run from this thread."
                    : "Subagent threads are not available.",
                context.Runtime is IAgentTuiSessionThreadRuntime
                    ? TranscriptSeverity.Info
                    : TranscriptSeverity.Warning);
            return;
        }

        await context.Dialogs.RunFlowAsync<object?>(async (flow, cancellationToken) =>
        {
            var choices = actions
                .Select(static action => new SubAgentMenuChoice(action, null))
                .Concat(children.Select(static child => new SubAgentMenuChoice(null, child)))
                .ToArray();
            var selected = await flow.SelectAsync(
                "Subagents",
                choices,
                choice => FormatChoice(context, choice),
                cancellationToken).ConfigureAwait(false);
            if (selected.IsBack || selected.IsCanceled || selected.Value is null)
                return null;

            if (selected.Value.Action is { } action)
            {
                await action.ExecuteAsync(context, flow, cancellationToken).ConfigureAwait(false);
                return null;
            }

            var child = selected.Value.Child!;
            if (child.Availability != SubAgentChildAvailability.Available ||
                child.AgentId is null || child.SessionId is null || child.ThreadId is null)
            {
                AddNotice(context, child.Reason ?? "This subagent is unavailable.", TranscriptSeverity.Warning);
                return null;
            }
            await context.SwitchTargetAsync(
                new ControlledSubAgentTuiExecutionTarget(
                    new AgentTuiRuntimeScope(child.AgentId, child.SessionId, child.ThreadId),
                    context.Scope,
                    new SubAgentLocalId(child.LocalId),
                    new AgentTuiClientSelectionSummary(child.ProviderKey, child.ModelName)),
                cancellationToken).ConfigureAwait(false);
            return null;
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private static string FormatChoice(AgentTuiCommandContext context, SubAgentMenuChoice choice)
    {
        if (choice.Action is { } action)
            return action.Title(context);

        var child = choice.Child!;
        var task = $"{child.LocalId} · {child.Role}";
        var messages = child.MessageCount == 1 ? "1 message" : $"{child.MessageCount} messages";
        return $"{task}  {child.Availability}  {child.Status ?? "idle"}  {messages}";
    }

    private static void AddNotice(
        AgentTuiCommandContext context,
        string message,
        TranscriptSeverity severity = TranscriptSeverity.Info)
        => context.Shell.Transcript.AddFinal(new TranscriptEntry(
            $"subagents-{Guid.NewGuid():N}",
            null,
            new NoticeCell(message, Severity: severity),
            new TranscriptEntryMetadata(
                AgentId: context.Scope.AgentId,
                AgentName: "subagents",
                SessionId: context.Scope.SessionId,
                ThreadId: context.Scope.ThreadId)));

    private sealed record SubAgentMenuChoice(
        AgentTuiSubAgentMenuAction? Action,
        AgentTuiSubAgentInfo? Child);
}

/// <summary>Configures application-owned actions shown above child conversations in <c>/subagents</c>.</summary>
public sealed class AgentTuiSubAgentMenuOptions
{
    private readonly List<AgentTuiSubAgentMenuAction> _actions = [];

    /// <summary>Gets the configured actions in display order.</summary>
    public IReadOnlyList<AgentTuiSubAgentMenuAction> Actions => _actions;

    /// <summary>Adds an application-owned action to the subagent menu.</summary>
    /// <param name="title">The label displayed in the menu.</param>
    /// <param name="executeAsync">The flow executed after the action is selected.</param>
    public void AddAction(
        string title,
        Func<AgentTuiCommandContext, AgentTuiDialogFlowContext, CancellationToken, ValueTask> executeAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        AddAction(_ => title, executeAsync);
    }

    /// <summary>Adds an application-owned action with a label resolved when the menu opens.</summary>
    /// <param name="title">Resolves the label displayed in the menu.</param>
    /// <param name="executeAsync">The flow executed after the action is selected.</param>
    public void AddAction(
        Func<AgentTuiCommandContext, string> title,
        Func<AgentTuiCommandContext, AgentTuiDialogFlowContext, CancellationToken, ValueTask> executeAsync)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(executeAsync);
        _actions.Add(new AgentTuiSubAgentMenuAction(title, executeAsync));
    }
}

/// <summary>Describes one application-owned action in the <c>/subagents</c> menu.</summary>
/// <param name="Title">Resolves the label displayed in the menu.</param>
/// <param name="ExecuteAsync">The flow executed after selection.</param>
public sealed record AgentTuiSubAgentMenuAction(
    Func<AgentTuiCommandContext, string> Title,
    Func<AgentTuiCommandContext, AgentTuiDialogFlowContext, CancellationToken, ValueTask> ExecuteAsync);
