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
    public ValueTask<ThreadContextUsage> EstimateAsync(
        Thread thread,
        AgentRunConfig runConfig,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(runConfig);
        cancellationToken.ThrowIfCancellationRequested();

        var estimated = EstimateInputTokens(thread.Messages);

        return ValueTask.FromResult(new ThreadContextUsage
        {
            SessionId = thread.SessionId,
            ThreadId = thread.Id,
            ProviderKey = null,
            ModelId = null,
            ContextWindow = null,
            LastObservedInputTokens = null,
            EstimatedInputTokens = estimated,
            EffectiveInputTokens = estimated,
            UsageRatio = null,
            IsEstimate = true,
            Source = "rough-message-estimate"
        });
    }

    /// <summary>Estimates message content only; does not include tools or provider tokenization.</summary>
    public static long EstimateInputTokens(IEnumerable<ChatMessage> messages)
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
