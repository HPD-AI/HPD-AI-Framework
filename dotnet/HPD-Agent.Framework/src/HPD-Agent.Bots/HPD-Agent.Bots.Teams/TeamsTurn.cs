using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;

namespace HPD.Agent.Bots.Teams;

/// <summary>
/// Minimal Teams turn surface consumed by the HPD bridge. It keeps most bot
/// logic testable without requiring a full Agents SDK turn test toolharness.
/// </summary>
public interface ITeamsTurn
{
    string Text { get; }

    string ConversationId { get; }

    string ServiceUrl { get; }

    string? ActivityName { get; }

    string? ReplyToId { get; }

    string? TenantId { get; }

    IReadOnlyDictionary<string, string> Values { get; }

    IReadOnlyList<InputFile> InputFiles { get; }

    Task QueueInformativeUpdateAsync(string text, CancellationToken ct);

    void QueueTextChunk(string text);

    Task CompleteCardAsync(TeamsAdaptiveCard card, CancellationToken ct);

    Task SendCardAsync(TeamsAdaptiveCard card, CancellationToken ct);

    Task EndStreamAsync(CancellationToken ct);
}

internal sealed class TeamsSdkTurn(ITurnContext context, ITurnState? turnState = null) : ITeamsTurn
{
    public string Text => context.Activity.Text ?? string.Empty;

    public string ConversationId => context.Activity.Conversation?.Id
        ?? throw new InvalidOperationException("Teams activity is missing conversation ID.");

    public string ServiceUrl => context.Activity.ServiceUrl
        ?? throw new InvalidOperationException("Teams activity is missing service URL.");

    public string? ActivityName => context.Activity.Name;

    public string? ReplyToId => context.Activity.ReplyToId;

    public string? TenantId
        => Values.TryGetValue("tenant.id", out var tenantId) ? tenantId : null;

    public IReadOnlyDictionary<string, string> Values
        => TeamsActivityValueReader.Read(context.Activity.Value);

    public IReadOnlyList<InputFile> InputFiles => [.. turnState?.Temp.InputFiles ?? []];

    public Task QueueInformativeUpdateAsync(string text, CancellationToken ct)
        => context.StreamingResponse.QueueInformativeUpdateAsync(text, ct);

    public void QueueTextChunk(string text)
        => context.StreamingResponse.QueueTextChunk(text);

    public Task CompleteCardAsync(TeamsAdaptiveCard card, CancellationToken ct)
    {
        context.StreamingResponse.FinalMessage = MessageFactory.Attachment(new Attachment
        {
            ContentType = ContentTypes.AdaptiveCard,
            Content = card,
        });

        return Task.CompletedTask;
    }

    public Task SendCardAsync(TeamsAdaptiveCard card, CancellationToken ct)
        => context.SendActivityAsync(MessageFactory.Attachment(new Attachment
        {
            ContentType = ContentTypes.AdaptiveCard,
            Content = card,
        }), ct);

    public Task EndStreamAsync(CancellationToken ct)
        => context.StreamingResponse.EndStreamAsync(ct);
}
