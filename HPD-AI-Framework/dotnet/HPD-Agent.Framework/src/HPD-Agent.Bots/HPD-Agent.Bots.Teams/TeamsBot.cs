using HPD.Agent;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.Contracts;
using HPD.Agent.Bots.Session;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Events;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Bots.Teams;

/// <summary>
/// HPD bridge service for Microsoft Teams turns. The M365 Agents SDK owns the
/// HTTP/auth/activity pipeline; this type owns HPD session mapping and bot logic.
/// </summary>
public sealed class TeamsBot(
    IOptions<TeamsBotConfig> options,
    SessionManager sessionManager,
    AgentManager agentManager,
    PlatformSessionMapper sessionMapper,
    TeamsCardRenderer? cardRenderer = null,
    TeamsFormatConverter? formatter = null)
{
    private const string PermissionApproveActionId = "hpd.permission.approve";
    private const string PermissionDenyActionId = "hpd.permission.deny";

    private readonly TeamsBotConfig _config = options.Value;
    private readonly SessionManager _sessionManager = sessionManager;
    private readonly AgentManager _agentManager = agentManager;
    private readonly PlatformSessionMapper _sessionMapper = sessionMapper;
    private readonly TeamsCardRenderer _cardRenderer = cardRenderer ?? new TeamsCardRenderer();
    private readonly TeamsFormatConverter _formatter = formatter ?? new TeamsFormatConverter();

    public TeamsBotConfig Config => _config;

    public SessionManager SessionManager => _sessionManager;

    public AgentManager AgentManager => _agentManager;

    public PlatformSessionMapper SessionMapper => _sessionMapper;

    public event Action<TeamsCardActionEvent>? OnCardAction;

    public event Action<TeamsModalSubmitEvent>? OnModalSubmit;

    public event Action<TeamsReactionEvent>? OnReaction;

    /// <summary>
    /// Processes a Teams message activity from the M365 Agents SDK.
    /// </summary>
    public Task<bool> ProcessMessageAsync(ITurnContext turnContext, CancellationToken ct)
        => ProcessMessageAsync(new TeamsSdkTurn(turnContext), ct);

    /// <summary>
    /// Processes a Teams message activity and its turn state from the M365 Agents SDK.
    /// </summary>
    public Task<bool> ProcessMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken ct)
        => ProcessMessageAsync(new TeamsSdkTurn(turnContext, turnState), ct);

    /// <summary>
    /// Processes a message turn through HPD session mapping and native Teams streaming.
    /// </summary>
    public async Task<bool> ProcessMessageAsync(ITeamsTurn turn, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(turn);

        var text = _formatter.ToPlainText(turn.Text).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var platformKey = TeamsThreadId.FormatRaw(turn.ConversationId, turn.ServiceUrl);
        var (sessionId, branchId) = await _sessionMapper.ResolveAsync(platformKey, ct);
        await PersistTeamsMetadataAsync(sessionId, platformKey, turn, ct);

        return await StreamNativeAsync(turn, sessionId, branchId, text, ct);
    }

    public Task ProcessReactionAsync(ITurnContext turnContext, CancellationToken ct)
        => ProcessReactionAsync(new TeamsSdkTurn(turnContext), added: true, ct);

    public Task ProcessReactionAsync(ITeamsTurn turn, bool added, CancellationToken ct)
    {
        var threadId = TeamsThreadId.FormatRaw(turn.ConversationId, turn.ServiceUrl);
        var reaction = turn.Values.TryGetValue("reactionsAdded.0.type", out var addedReaction)
            ? addedReaction
            : turn.Values.TryGetValue("reactionsRemoved.0.type", out var removedReaction)
                ? removedReaction
                : null;

        OnReaction?.Invoke(new TeamsReactionEvent(threadId, turn.ReplyToId, reaction, added));
        return Task.CompletedTask;
    }

    public Task ProcessInvokeAsync(ITurnContext turnContext, CancellationToken ct)
        => ProcessActionAsync(turnContext, ct);

    public Task ProcessActionAsync(ITurnContext turnContext, CancellationToken ct)
        => ProcessActionAsync(new TeamsSdkTurn(turnContext), ct);

    public async Task ProcessActionAsync(ITeamsTurn turn, CancellationToken ct)
    {
        var threadId = TeamsThreadId.FormatRaw(turn.ConversationId, turn.ServiceUrl);
        var actionId = ExtractActionId(turn.Values);
        if (string.IsNullOrWhiteSpace(actionId))
            return;

        if (string.Equals(turn.ActivityName, "task/submit", StringComparison.OrdinalIgnoreCase))
        {
            OnModalSubmit?.Invoke(new TeamsModalSubmitEvent(actionId, turn.Values, threadId));
            return;
        }

        if (IsPermissionAction(actionId))
        {
            await ProcessPermissionActionAsync(actionId, turn.Values, ct);
            return;
        }

        OnCardAction?.Invoke(new TeamsCardActionEvent(actionId, turn.Values, threadId));
    }

    public Task ProcessInvokeAsync(ITeamsTurn turn, CancellationToken ct)
        => ProcessActionAsync(turn, ct);

    public async Task ProcessPermissionActionAsync(
        string actionId,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct)
    {
        if (!TryGetPermissionId(values, out var permissionId))
            return;

        var approved = string.Equals(actionId, PermissionApproveActionId, StringComparison.Ordinal);
        var agent = await _agentManager.GetOrBuildAgentAsync(_config.ResolveAgentId(), ct);

        await agent.TryRespondAsync(new PermissionResponseEvent(
            PermissionId: permissionId,
            SourceName: "teams",
            Approved: approved,
            Reason: approved ? null : "Denied from Teams"), ct);
    }

    public Task<string> OpenDmAsync(string userId, CancellationToken ct = default)
        => throw new NotSupportedException(
            "Opening Teams direct messages requires proactive conversation wiring for the host tenant.");

    public Task ProcessProactiveContinueAsync(ITurnContext turnContext, CancellationToken ct)
        => turnContext.SendActivityAsync("Teams proactive continuation received.", cancellationToken: ct);

    public Task AddReactionAsync(string threadId, string messageId, string reaction, CancellationToken ct = default)
        => throw new NotSupportedException("Teams Bot API does not support adding reactions.");

    public Task RemoveReactionAsync(string threadId, string messageId, string reaction, CancellationToken ct = default)
        => throw new NotSupportedException("Teams Bot API does not support removing reactions.");

    private async Task<bool> StreamNativeAsync(
        ITeamsTurn turn,
        string sessionId,
        string branchId,
        string text,
        CancellationToken ct)
    {
        if (!_sessionManager.TryAcquireBranchOperationLock(sessionId, branchId))
            return false;

        var streamStarted = false;
        try
        {
            await turn.QueueInformativeUpdateAsync("Thinking...", ct);
            streamStarted = true;

            var agentId = _config.ResolveAgentId();
            var agent = await _agentManager.GetOrBuildAgentAsync(agentId, ct);
            await using var subscription = ((IEventInboxSource)agent.EventCoordinator).CreateInbox<AgentEvent>();

            async Task HandleEventAsync(AgentEvent evt)
            {
                switch (evt)
                {
                    case TextDeltaEvent delta when !string.IsNullOrEmpty(delta.Text):
                        turn.QueueTextChunk(delta.Text);
                        break;

                    case CardContentEvent card:
                        await turn.CompleteCardAsync(_cardRenderer.Render(card.Card), ct);
                        break;

                    case PermissionRequestEvent permission:
                        await turn.SendCardAsync(BuildPermissionCard(permission), ct);
                        break;
                }
            }

            var attachments = MapInputFiles(turn.InputFiles);
            var contents = new List<AIContent> { new TextContent(text) };
            contents.AddRange(attachments);

            var runTask = agent.RunAsync(new UserMessagesInputEvent([new ChatMessage(ChatRole.User, contents)])
            {
                AgentId = agentId,
                SessionId = sessionId,
                BranchId = branchId,
                RunConfig = attachments.Count > 0
                    ? new AgentRunConfig
                    {
                        UserMessage = text,
                        Attachments = attachments,
                    }
                    : null,
            }, ct);
            await DrainEventsUntilRunCompletesAsync(subscription, runTask, HandleEventAsync, ct);
            await runTask.ConfigureAwait(false);

            return true;
        }
        finally
        {
            if (streamStarted)
                await turn.EndStreamAsync(ct);
            _sessionManager.ReleaseBranchOperationLock(sessionId, branchId);
        }
    }

    private static async Task DrainEventsUntilRunCompletesAsync(
        EventInbox<AgentEvent> subscription,
        Task runTask,
        Func<AgentEvent, Task> handleEventAsync,
        CancellationToken ct)
    {
        while (true)
        {
            while (subscription.Reader.TryRead(out var evt))
                await handleEventAsync(evt).ConfigureAwait(false);

            if (runTask.IsCompleted)
                return;

            var waitForEventTask = subscription.Reader.WaitToReadAsync(ct).AsTask();
            var completed = await Task.WhenAny(runTask, waitForEventTask).ConfigureAwait(false);
            if (completed == runTask)
            {
                await runTask.ConfigureAwait(false);
                continue;
            }

            if (!await waitForEventTask.ConfigureAwait(false))
                return;
        }
    }

    private static string? ExtractActionId(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("actionId", out var actionId))
            return actionId;

        if (values.TryGetValue("action.data.actionId", out actionId))
            return actionId;

        if (values.TryGetValue("data.actionId", out actionId))
            return actionId;

        return null;
    }

    private static bool IsPermissionAction(string actionId)
        => string.Equals(actionId, PermissionApproveActionId, StringComparison.Ordinal)
            || string.Equals(actionId, PermissionDenyActionId, StringComparison.Ordinal);

    private static bool TryGetPermissionId(IReadOnlyDictionary<string, string> values, out string permissionId)
    {
        if (values.TryGetValue("permissionId", out permissionId!)
            || values.TryGetValue("action.data.permissionId", out permissionId!)
            || values.TryGetValue("data.permissionId", out permissionId!))
        {
            return !string.IsNullOrWhiteSpace(permissionId);
        }

        permissionId = string.Empty;
        return false;
    }

    private static TeamsAdaptiveCard BuildPermissionCard(PermissionRequestEvent permission)
    {
        var body = new List<object>
        {
            new TeamsTextBlock("Permission requested", Weight: "Bolder", Size: "Medium"),
            new TeamsTextBlock(permission.Description ?? $"Allow {permission.FunctionName}?", Wrap: true),
            new TeamsFactSet(
            [
                new TeamsFact("Function", permission.FunctionName),
                new TeamsFact("Source", permission.SourceName),
            ]),
        };

        if (permission.Arguments?.Count > 0)
        {
            body.Add(new TeamsTextBlock("Arguments", Weight: "Bolder"));
            body.Add(new TeamsTextBlock(FormatPermissionArguments(permission.Arguments), Wrap: true));
        }

        return new TeamsAdaptiveCard(
            Body: body,
            Actions:
            [
                new TeamsSubmitAction(
                    Title: "Approve",
                    Data: new Dictionary<string, string>
                    {
                        ["actionId"] = PermissionApproveActionId,
                        ["permissionId"] = permission.PermissionId,
                    },
                    Style: "positive"),
                new TeamsSubmitAction(
                    Title: "Deny",
                    Data: new Dictionary<string, string>
                    {
                        ["actionId"] = PermissionDenyActionId,
                        ["permissionId"] = permission.PermissionId,
                    },
                    Style: "destructive"),
            ]);
    }

    private static string FormatPermissionArguments(IDictionary<string, object?> arguments)
        => string.Join("\n", arguments.Select(argument => $"{argument.Key}: {argument.Value}"));

    private static IReadOnlyList<DataContent> MapInputFiles(IReadOnlyList<InputFile> inputFiles)
    {
        if (inputFiles.Count == 0)
            return [];

        var attachments = new List<DataContent>(inputFiles.Count);
        foreach (var file in inputFiles)
        {
            var contentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;

            attachments.Add(new DataContent(file.Content.ToMemory(), contentType)
            {
                Name = file.Filename,
            });
        }

        return attachments;
    }

    private async Task PersistTeamsMetadataAsync(
        string sessionId,
        string platformKey,
        ITeamsTurn turn,
        CancellationToken ct)
    {
        var session = await _sessionManager.Store.LoadSessionAsync(sessionId, ct);
        if (session is null)
            return;

        session.Metadata["platformKey"] = platformKey;
        session.Metadata["teams.conversationId"] = turn.ConversationId;
        session.Metadata["teams.serviceUrl"] = turn.ServiceUrl;

        if (!string.IsNullOrWhiteSpace(turn.TenantId))
            session.Metadata["teams.tenantId"] = turn.TenantId;

        if (TryBuildChannelContext(turn.Values) is { } channelContext)
            session.Metadata["teams.channelContext"] = channelContext;

        await _sessionManager.Store.SaveSessionAsync(session, ct);
    }

    private static Dictionary<string, string>? TryBuildChannelContext(IReadOnlyDictionary<string, string> values)
    {
        var teamId = values.TryGetValue("channelData.team.id", out var nestedTeamId)
            ? nestedTeamId
            : values.TryGetValue("team.id", out var teamIdValue)
                ? teamIdValue
                : null;

        var channelId = values.TryGetValue("channelData.channel.id", out var nestedChannelId)
            ? nestedChannelId
            : values.TryGetValue("channel.id", out var channelIdValue)
                ? channelIdValue
                : null;

        return string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(channelId)
            ? null
            : new Dictionary<string, string>
            {
                ["teamId"] = teamId,
                ["channelId"] = channelId,
            };
    }
}
