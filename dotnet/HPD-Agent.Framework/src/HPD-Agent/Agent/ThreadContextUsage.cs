using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

public interface IThreadContextUsageEstimator
{
    ValueTask<ThreadContextUsage> EstimateAsync(
        Thread thread,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken = default);
}

public sealed record ThreadContextUsage
{
    public string SessionId { get; init; } = "";
    public string ThreadId { get; init; } = "";
    public string? ProviderKey { get; init; }
    public string? ModelId { get; init; }
    public int? ContextWindow { get; init; }
    public long? LastObservedInputTokens { get; init; }
    public long? EstimatedInputTokens { get; init; }
    public long? EffectiveInputTokens { get; init; }
    public double? UsageRatio { get; init; }
    public bool IsEstimate { get; init; }
    public string? Source { get; init; }
}

public sealed class ThreadContextUsageEstimator : IThreadContextUsageEstimator
{
    private static readonly string CompactionStateKey = typeof(CompactionStateData).FullName
        ?? throw new InvalidOperationException("Compaction state type has no full name.");

    public ValueTask<ThreadContextUsage> EstimateAsync(
        Thread thread,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(runConfig);
        cancellationToken.ThrowIfCancellationRequested();

        var modelContext = runConfig.Compaction?.ModelContext;
        var contextWindow = modelContext?.ContextWindow ?? modelContext?.InputTokenLimit;
        var lastObserved = ReadLastObservedInputTokens(thread);
        long? estimated = lastObserved.HasValue ? null : EstimateInputTokens(thread.Messages);
        var effective = lastObserved ?? estimated;

        return ValueTask.FromResult(new ThreadContextUsage
        {
            SessionId = thread.SessionId,
            ThreadId = thread.Id,
            ProviderKey = modelContext?.ProviderKey,
            ModelId = modelContext?.ModelId,
            ContextWindow = contextWindow,
            LastObservedInputTokens = lastObserved,
            EstimatedInputTokens = estimated,
            EffectiveInputTokens = effective,
            UsageRatio = contextWindow is > 0 && effective.HasValue
                ? effective.Value / (double)contextWindow.Value
                : null,
            IsEstimate = !lastObserved.HasValue,
            Source = lastObserved.HasValue
                ? "last-observed-provider-usage"
                : "rough-message-estimate"
        });
    }

    private static long? ReadLastObservedInputTokens(Thread thread)
    {
        var json = thread.GetMiddlewareState(CompactionStateKey);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize(
                json,
                SessionJsonContext.Default.CompactionStateData);
            return state?.LastTurnUsage?.InputTokenCount;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long EstimateInputTokens(IEnumerable<ChatMessage> messages)
    {
        long chars = 0;
        foreach (var message in messages)
            chars += EstimateChars(message);

        return Math.Max(1, (long)Math.Ceiling(chars / 4.0));
    }

    private static int EstimateChars(ChatMessage message)
    {
        var total = 0;
        foreach (var content in message.Contents)
        {
            total += content switch
            {
                TextContent text => text.Text?.Length ?? 0,
                FunctionCallContent call => (call.Name?.Length ?? 0) + EstimateFunctionArguments(call.Arguments),
                FunctionResultContent result => (result.CallId?.Length ?? 0) + (result.Result?.ToString()?.Length ?? 0),
                DataContent data => data.Name?.Length ?? data.MediaType?.Length ?? 16,
                UriContent uri => (uri.Uri?.ToString().Length ?? 0) + (uri.MediaType?.Length ?? 0),
                _ => content.ToString()?.Length ?? 8
            };
        }

        return total;
    }

    private static int EstimateFunctionArguments(
        IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        if (arguments is null)
            return 0;

        var total = 0;
        foreach (var (key, value) in arguments)
            total += key.Length + (value?.ToString()?.Length ?? 0);

        return total;
    }
}
