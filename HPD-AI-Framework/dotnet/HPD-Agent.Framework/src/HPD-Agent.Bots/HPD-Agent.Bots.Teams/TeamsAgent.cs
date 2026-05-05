using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.App.Proactive;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;

namespace HPD.Agent.Bots.Teams;

/// <summary>
/// M365 Agents SDK entry point. Route handlers delegate to <see cref="TeamsBot"/>
/// as Teams support is built out.
/// </summary>
public sealed class TeamsAgent : AgentApplication
{
    private readonly TeamsBot _bot;

    public TeamsAgent(AgentApplicationOptions options, TeamsBot bot) : base(options)
    {
        _bot = bot;

        OnActivity(ActivityTypes.Message, OnMessageAsync, rank: RouteRank.Last);
        OnActivity(ActivityTypes.MessageReaction, OnReactionAsync, rank: RouteRank.Last);
        OnActivity(ActivityTypes.Invoke, OnInvokeAsync, rank: RouteRank.Last);
    }

    private Task OnMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        if (turnContext.Activity.Value is not null && string.IsNullOrWhiteSpace(turnContext.Activity.Text))
            return _bot.ProcessActionAsync(turnContext, cancellationToken);

        return _bot.ProcessMessageAsync(turnContext, turnState, cancellationToken);
    }

    private Task OnReactionAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
        => _bot.ProcessReactionAsync(turnContext, cancellationToken);

    private Task OnInvokeAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
        => _bot.ProcessActionAsync(turnContext, cancellationToken);

    public Task ContinueConversationAsync(
        ITurnContext turnContext,
        string conversationId,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turnContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return Proactive.ContinueConversationAsync(
            turnContext.Adapter,
            conversationId,
            async (context, _, ct) =>
                await context.SendActivityAsync(text, cancellationToken: ct),
            cancellationToken: cancellationToken);
    }

    public async Task<string> OpenDmAsync(
        ITurnContext turnContext,
        string userId,
        string? userName = null,
        string? tenantId = null,
        string? initialText = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turnContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var serviceUrl = turnContext.Activity.ServiceUrl;
        if (string.IsNullOrWhiteSpace(serviceUrl))
            throw new InvalidOperationException("Teams proactive DM creation requires a service URL from an existing Teams turn.");

        tenantId = string.IsNullOrWhiteSpace(tenantId)
            ? turnContext.Activity.Conversation?.TenantId ?? _bot.Config.AppTenantId
            : tenantId.Trim();
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new InvalidOperationException("Teams proactive DM creation requires a tenant ID.");

        var builder = CreateConversationOptionsBuilder
            .Create(_bot.Config.AppId, Channels.Msteams, serviceUrl)
            .WithUser(userId, userName ?? string.Empty)
            .WithTenantId(tenantId)
            .IsGroup(false)
            .WithStoreConversation(true);

        if (!string.IsNullOrWhiteSpace(initialText))
            builder.WithActivity(MessageFactory.Text(initialText));

        var conversation = await Proactive.CreateConversationAsync(
            turnContext.Adapter,
            builder.Build(),
            cancellationToken: cancellationToken);

        var conversationId = conversation.Reference.Conversation?.Id;
        if (string.IsNullOrWhiteSpace(conversationId))
            throw new InvalidOperationException("Teams did not return a conversation ID for the proactive DM.");

        return TeamsThreadId.FormatRaw(conversationId, conversation.Reference.ServiceUrl ?? serviceUrl);
    }

    [ContinueConversation]
    public Task OnContinueConversationAsync(
        ITurnContext turnContext,
        ITurnState turnState,
        CancellationToken cancellationToken)
        => _bot.ProcessProactiveContinueAsync(turnContext, cancellationToken);
}
