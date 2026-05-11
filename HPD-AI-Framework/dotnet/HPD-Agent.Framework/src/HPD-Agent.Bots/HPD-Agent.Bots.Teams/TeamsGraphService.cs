using HPD.Agent.Bots.Contracts;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Net;
using System.Text.RegularExpressions;

namespace HPD.Agent.Bots.Teams;

public sealed partial class TeamsGraphService(GraphServiceClient? graphClient = null) : ITeamsHistoryService
{
    private readonly GraphServiceClient? _graphClient = graphClient;

    public async Task<TeamsFetchMessagesResult> FetchMessagesAsync(
        string threadId,
        TeamsFetchOptions? options = null,
        CancellationToken ct = default)
    {
        var parsed = TeamsThreadId.Parse(threadId);
        options ??= new TeamsFetchOptions();
        EnsureGraphClient();

        var threadMessageId = ExtractThreadMessageId(parsed.DecodedConversationId);
        var response = options.ChannelContext is { } channelContext
            ? await FetchChannelMessagesAsync(channelContext, threadMessageId, options, ct)
            : await FetchChatMessagesAsync(parsed.BaseConversationId, options, ct);

        var messages = response.Value?.Select(message => MapMessage(message, ResolveThreadId(parsed, message, options.ChannelContext, threadMessageId))).ToArray()
            ?? [];
        if (options.Direction == TeamsFetchDirection.OldestFirst)
            Array.Reverse(messages);

        return new TeamsFetchMessagesResult(
            messages,
            response.OdataNextLink,
            !string.IsNullOrWhiteSpace(response.OdataNextLink));
    }

    public async Task<TeamsListThreadsResult> ListThreadsAsync(
        string channelId,
        TeamsListThreadsOptions? options = null,
        CancellationToken ct = default)
    {
        var parsed = TeamsThreadId.Parse(channelId);
        options ??= new TeamsListThreadsOptions();
        EnsureGraphClient();

        var response = options.ChannelContext is { } channelContext
            ? await FetchChannelRootMessagesAsync(channelContext, options.Limit, options.Cursor, ct)
            : await FetchChatMessagesAsync(parsed.BaseConversationId, new TeamsFetchOptions(options.Limit, options.Cursor), ct);

        var threads = response.Value?.Select(message => MapThread(parsed, message)).ToArray()
            ?? [];

        return new TeamsListThreadsResult(threads, response.OdataNextLink);
    }

    public Task<TeamsChannelContext?> FetchChannelContextAsync(string threadId, CancellationToken ct = default)
    {
        _ = TeamsThreadId.Parse(threadId);
        EnsureGraphClient();

        return Task.FromResult<TeamsChannelContext?>(null);
    }

    private void EnsureGraphClient()
    {
        if (_graphClient is null)
            throw new BotAuthenticationException("Teams GraphServiceClient is not configured.");
    }

    private static async Task<ChatMessageCollectionResponse> OrEmptyAsync(Task<ChatMessageCollectionResponse?> responseTask)
        => await responseTask.ConfigureAwait(false) ?? new ChatMessageCollectionResponse();

    private Task<ChatMessageCollectionResponse> FetchChatMessagesAsync(
        string chatId,
        TeamsFetchOptions options,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Cursor))
            return OrEmptyAsync(_graphClient!.Chats[chatId].Messages.WithUrl(options.Cursor).GetAsync(cancellationToken: ct));

        return OrEmptyAsync(_graphClient!.Chats[chatId].Messages.GetAsync(config =>
        {
            config.QueryParameters.Top = options.Limit ?? 50;
            config.QueryParameters.Orderby = ["createdDateTime desc"];
            config.QueryParameters.Expand = ["attachments"];
        }, ct));
    }

    private Task<ChatMessageCollectionResponse> FetchChannelMessagesAsync(
        TeamsChannelContext channelContext,
        string? threadMessageId,
        TeamsFetchOptions options,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Cursor))
        {
            return threadMessageId is not null
                ? OrEmptyAsync(_graphClient!.Teams[channelContext.TeamId].Channels[channelContext.ChannelId].Messages[threadMessageId].Replies.WithUrl(options.Cursor).GetAsync(cancellationToken: ct))
                : OrEmptyAsync(_graphClient!.Teams[channelContext.TeamId].Channels[channelContext.ChannelId].Messages.WithUrl(options.Cursor).GetAsync(cancellationToken: ct));
        }

        if (threadMessageId is not null)
            return FetchChannelThreadMessagesAsync(channelContext, threadMessageId, options, ct);

        return FetchChannelRootMessagesAsync(channelContext, options.Limit, null, ct);
    }

    private async Task<ChatMessageCollectionResponse> FetchChannelThreadMessagesAsync(
        TeamsChannelContext channelContext,
        string threadMessageId,
        TeamsFetchOptions options,
        CancellationToken ct)
    {
        var parent = await _graphClient!.Teams[channelContext.TeamId]
            .Channels[channelContext.ChannelId]
            .Messages[threadMessageId]
            .GetAsync(cancellationToken: ct);

        var replies = await _graphClient.Teams[channelContext.TeamId]
            .Channels[channelContext.ChannelId]
            .Messages[threadMessageId]
            .Replies
            .GetAsync(config =>
            {
                config.QueryParameters.Top = options.Limit ?? 50;
                config.QueryParameters.Orderby = ["createdDateTime desc"];
                config.QueryParameters.Expand = ["attachments"];
            }, ct);

        var value = new List<ChatMessage>();
        if (parent is not null)
            value.Add(parent);
        if (replies?.Value is not null)
            value.AddRange(replies.Value);

        return new ChatMessageCollectionResponse
        {
            Value = value,
            OdataNextLink = replies?.OdataNextLink,
        };
    }

    private Task<ChatMessageCollectionResponse> FetchChannelRootMessagesAsync(
        TeamsChannelContext channelContext,
        int? limit,
        string? cursor,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(cursor))
            return OrEmptyAsync(_graphClient!.Teams[channelContext.TeamId].Channels[channelContext.ChannelId].Messages.WithUrl(cursor).GetAsync(cancellationToken: ct));

        return OrEmptyAsync(_graphClient!.Teams[channelContext.TeamId]
            .Channels[channelContext.ChannelId]
            .Messages
            .GetAsync(config =>
            {
                config.QueryParameters.Top = limit ?? 50;
                config.QueryParameters.Orderby = ["createdDateTime desc"];
                config.QueryParameters.Expand = ["attachments"];
            }, ct));
    }

    private static TeamsMessageSummary MapMessage(ChatMessage message, string threadId)
        => new(
            Id: message.Id ?? string.Empty,
            ThreadId: threadId,
            Text: ExtractText(message),
            CreatedAt: message.CreatedDateTime,
            FromUserId: message.From?.User?.Id,
            FromDisplayName: message.From?.User?.DisplayName,
            Attachments: MapAttachments(message));

    private static TeamsThreadSummary MapThread(
        TeamsThreadId baseThread,
        ChatMessage message)
    {
        var messageId = message.Id;
        var threadConversationId = string.IsNullOrWhiteSpace(messageId)
            ? baseThread.BaseConversationId
            : $"{baseThread.BaseConversationId};messageid={messageId}";
        var threadId = TeamsThreadId.FormatRaw(threadConversationId, baseThread.DecodedServiceUrl);

        return new TeamsThreadSummary(
            threadId,
            messageId,
            ExtractText(message),
            message.CreatedDateTime);
    }

    private static string ResolveThreadId(
        TeamsThreadId parsed,
        ChatMessage message,
        TeamsChannelContext? channelContext,
        string? threadMessageId)
    {
        if (channelContext is not null && threadMessageId is not null)
            return TeamsThreadId.FormatRaw($"{parsed.BaseConversationId};messageid={threadMessageId}", parsed.DecodedServiceUrl);

        if (channelContext is not null && !string.IsNullOrWhiteSpace(message.Id))
            return TeamsThreadId.FormatRaw($"{parsed.BaseConversationId};messageid={message.Id}", parsed.DecodedServiceUrl);

        return TeamsThreadId.FormatRaw(parsed.BaseConversationId, parsed.DecodedServiceUrl);
    }

    private static string? ExtractText(ChatMessage message)
    {
        var content = message.Body?.Content;
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var withoutTags = HtmlTagRegex().Replace(content, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static IReadOnlyList<TeamsMessageAttachmentSummary> MapAttachments(ChatMessage message)
        => message.Attachments?.Select(attachment => new TeamsMessageAttachmentSummary(
            Id: attachment.Id ?? string.Empty,
            Name: attachment.Name,
            ContentType: attachment.ContentType,
            ContentUrl: attachment.ContentUrl)).ToArray()
        ?? [];

    private static string? ExtractThreadMessageId(string conversationId)
    {
        const string Marker = ";messageid=";
        var markerIndex = conversationId.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex < 0 ? null : conversationId[(markerIndex + Marker.Length)..];
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();
}

public sealed record TeamsFetchOptions(
    int? Limit = null,
    string? Cursor = null,
    TeamsFetchDirection Direction = TeamsFetchDirection.NewestFirst,
    TeamsChannelContext? ChannelContext = null);

public enum TeamsFetchDirection
{
    NewestFirst,
    OldestFirst
}

public sealed record TeamsListThreadsOptions(
    int? Limit = null,
    string? Cursor = null,
    TeamsChannelContext? ChannelContext = null);

public sealed record TeamsFetchMessagesResult(
    IReadOnlyList<TeamsMessageSummary> Messages,
    string? NextCursor,
    bool HasMore);

public sealed record TeamsListThreadsResult(
    IReadOnlyList<TeamsThreadSummary> Threads,
    string? NextCursor);

public sealed record TeamsMessageSummary(
    string Id,
    string ThreadId,
    string? Text,
    DateTimeOffset? CreatedAt,
    string? FromUserId = null,
    string? FromDisplayName = null,
    IReadOnlyList<TeamsMessageAttachmentSummary>? Attachments = null);

public sealed record TeamsMessageAttachmentSummary(
    string Id,
    string? Name,
    string? ContentType,
    string? ContentUrl);

public sealed record TeamsThreadSummary(
    string ThreadId,
    string? MessageId,
    string? Title,
    DateTimeOffset? CreatedAt);

public sealed record TeamsChannelContext(string TeamId, string ChannelId);
