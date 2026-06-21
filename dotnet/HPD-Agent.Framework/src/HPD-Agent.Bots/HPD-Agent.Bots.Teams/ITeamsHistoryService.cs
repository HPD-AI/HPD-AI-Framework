using HPD.Agent.Bots.Contracts;

namespace HPD.Agent.Bots.Teams;

public interface ITeamsHistoryService
{
    Task<TeamsFetchMessagesResult> FetchMessagesAsync(
        string threadId,
        TeamsFetchOptions? options = null,
        CancellationToken ct = default);

    Task<TeamsListThreadsResult> ListThreadsAsync(
        string channelId,
        TeamsListThreadsOptions? options = null,
        CancellationToken ct = default);

    Task<TeamsChannelContext?> FetchChannelContextAsync(
        string threadId,
        CancellationToken ct = default);
}

public sealed class NoopTeamsHistoryService : ITeamsHistoryService
{
    public Task<TeamsFetchMessagesResult> FetchMessagesAsync(
        string threadId,
        TeamsFetchOptions? options = null,
        CancellationToken ct = default)
        => Task.FromResult(new TeamsFetchMessagesResult([], null, false));

    public Task<TeamsListThreadsResult> ListThreadsAsync(
        string channelId,
        TeamsListThreadsOptions? options = null,
        CancellationToken ct = default)
        => Task.FromResult(new TeamsListThreadsResult([], null));

    public Task<TeamsChannelContext?> FetchChannelContextAsync(
        string threadId,
        CancellationToken ct = default)
        => Task.FromResult<TeamsChannelContext?>(null);
}
