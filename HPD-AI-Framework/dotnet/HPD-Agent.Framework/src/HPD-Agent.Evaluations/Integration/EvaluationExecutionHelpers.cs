// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.Integration;

internal static class EvaluationExecutionHelpers
{
    internal static ChatConfiguration? BuildChatConfiguration(EvalJudgeConfig? config)
    {
        if (config?.OverrideAgent is not null)
            return new ChatConfiguration(new AgentBackedJudgeChatClient(config.OverrideAgent));

        if (config?.OverrideChatClient is not null)
            return new ChatConfiguration(new TracingEvalChatClient(config.OverrideChatClient));

        return null;
    }

    internal static ChatConfiguration? WithTracing(ChatConfiguration? chatConfiguration)
    {
        if (chatConfiguration is null)
            return null;

        if (chatConfiguration.ChatClient is TracingEvalChatClient)
            return chatConfiguration;

        return new ChatConfiguration(new TracingEvalChatClient(chatConfiguration.ChatClient));
    }

    internal static string ResolveEvaluatorVersion(IEvaluator evaluator) =>
        evaluator is IHpdEvaluator hpd ? hpd.Version : "ms-builtin";

    internal static bool IsPassingMetric(EvaluationMetric metric) => metric switch
    {
        { Interpretation.Failed: false } => true,
        { Interpretation.Failed: true } => false,
        BooleanMetric bm => bm.Value == true,
        NumericMetric nm => nm.Value.HasValue && nm.Value.Value > 0,
        StringMetric sm => !string.IsNullOrWhiteSpace(sm.Value),
        _ => false,
    };

    internal static bool IsFailingMetric(EvaluationMetric metric) =>
        !IsPassingMetric(metric);

    internal static (string? modelId, UsageDetails? usage, TimeSpan? duration) ExtractJudgeMetadata(
        EvaluationResult result)
    {
        foreach (var (_, metric) in result.Metrics)
        {
            string? modelId = TryGetMetadata(metric, "eval-model");
            var duration = TryGetMetadata(metric, "eval-duration-ms") is { } durationText &&
                           double.TryParse(durationText, out var durationMs)
                ? TimeSpan.FromMilliseconds(durationMs)
                : (TimeSpan?)null;

            var input = TryParseLong(TryGetMetadata(metric, "eval-input-tokens"));
            var output = TryParseLong(TryGetMetadata(metric, "eval-output-tokens"));
            var total = TryParseLong(TryGetMetadata(metric, "eval-total-tokens"));

            UsageDetails? usage = input.HasValue || output.HasValue || total.HasValue
                ? new UsageDetails
                {
                    InputTokenCount = input,
                    OutputTokenCount = output,
                    TotalTokenCount = total ?? ((input ?? 0) + (output ?? 0)),
                }
                : null;

            if (modelId is not null || usage is not null || duration is not null)
                return (modelId, usage, duration);
        }

        return (null, null, null);
    }

    private static string? TryGetMetadata(EvaluationMetric metric, string key)
    {
        if (metric.Metadata?.TryGetValue(key, out var value) != true)
            return null;

        return value?.ToString();
    }

    private static long? TryParseLong(string? value) =>
        long.TryParse(value, out var parsed) ? parsed : null;

    internal static bool IsInfrastructureError(Exception ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("429") ||
               msg.Contains("503") ||
               msg.Contains("rate limit") ||
               msg.Contains("too many requests") ||
               msg.Contains("service unavailable");
    }
}

internal sealed class TracingEvalChatClient(IChatClient inner) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messages = chatMessages.ToList();

        if (EvalTraceContext.CurrentEvaluatorName is not { } evaluatorName)
            return await inner.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await inner.GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);
            sw.Stop();

            EvalTraceContext.AddJudgeCall(new JudgeCallRecord(
                EvaluatorName: evaluatorName,
                Phase: "judge",
                Prompt: messages,
                Response: response,
                ModelId: response.ModelId,
                Usage: response.Usage,
                Duration: sw.Elapsed,
                Succeeded: true,
                ErrorMessage: null));

            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();

            EvalTraceContext.AddJudgeCall(new JudgeCallRecord(
                EvaluatorName: evaluatorName,
                Phase: "judge",
                Prompt: messages,
                Response: null,
                ModelId: null,
                Usage: null,
                Duration: sw.Elapsed,
                Succeeded: false,
                ErrorMessage: ex.Message));

            throw;
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in response.Messages)
            yield return new ChatResponseUpdate { Contents = message.Contents };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        inner.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        // The wrapped client is caller-owned.
    }
}

/// <summary>
/// Compatibility facade that lets MS-compatible evaluators call an HPD judge agent
/// through the IChatClient-shaped ChatConfiguration contract.
/// </summary>
internal sealed class AgentBackedJudgeChatClient(IAgent agent) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = string.Join("\n\n", chatMessages.Select(message =>
            $"{message.Role.Value}: {message.Text}"));

        var runConfig = new AgentRunConfig
        {
            UserMessage = prompt,
            Chat = options is null ? null : new ChatRunConfig(options),
            DisableEvaluators = true,
            IsInternalEvalJudgeCall = true,
        };

        if (EvalTraceContext.CurrentEvaluatorName is not { } evaluatorName)
            return await agent.RunAsync(runConfig, cancellationToken).ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await agent.RunAsync(runConfig, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            EvalTraceContext.AddJudgeCall(new JudgeCallRecord(
                EvaluatorName: evaluatorName,
                Phase: "judge",
                Prompt: GetJudgePromptForTrace(),
                Response: response,
                ModelId: response.ModelId,
                Usage: response.Usage,
                Duration: sw.Elapsed,
                Succeeded: true,
                ErrorMessage: null));

            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();

            EvalTraceContext.AddJudgeCall(new JudgeCallRecord(
                EvaluatorName: evaluatorName,
                Phase: "judge",
                Prompt: GetJudgePromptForTrace(),
                Response: null,
                ModelId: null,
                Usage: null,
                Duration: sw.Elapsed,
                Succeeded: false,
                ErrorMessage: ex.Message));

            throw;
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in response.Messages)
            yield return new ChatResponseUpdate { Contents = message.Contents };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static IReadOnlyList<ChatMessage> GetJudgePromptForTrace() =>
        EvalTraceContext.TryGetLatestCapturedJudgePrompt(out var prompt)
            ? prompt
            :
            [
                new ChatMessage(ChatRole.System,
                    "Judge prompt executed through OverrideAgent; raw prompt is not captured by eval tracing.")
            ];
}
