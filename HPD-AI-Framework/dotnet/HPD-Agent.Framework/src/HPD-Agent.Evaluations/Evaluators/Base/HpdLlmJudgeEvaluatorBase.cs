// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Evaluators;

/// <summary>
/// Base class for single-call LLM-as-judge evaluators that parse the &lt;S0&gt;&lt;S1&gt;&lt;S2&gt;
/// XML-tagged response. Handles timing, metadata, null checks, GetResponseAsync, and
/// exception → error diagnostic fallback. Subclasses implement only the prompt
/// construction and result parsing.
/// </summary>
public abstract class HpdLlmJudgeEvaluatorBase : HpdEvaluatorBase
{
    private static readonly ChatOptions _judgeOptions = new()
    {
        Temperature = 0f,
        ResponseFormat = ChatResponseFormat.Text,
    };

    /// <summary>
    /// Build the message list sent to the judge LLM.
    /// </summary>
    protected abstract List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext);

    /// <summary>
    /// Parse the judge response text and populate metrics on <paramref name="result"/>.
    /// Use the S0/S1/S2 tag regex for standard tag parsing.
    /// </summary>
    protected abstract void ParseJudgeResponse(
        string responseText,
        EvaluationResult result,
        ChatResponse judgeResponse,
        TimeSpan duration);

    /// <summary>Creates an EvaluationResult with metric shells matching EvaluationMetricNames.</summary>
    protected abstract EvaluationResult CreateEmptyResult();

    public sealed override async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var result = CreateEmptyResult();

        if (chatConfiguration is null)
        {
            result.AddDiagnosticsToAllMetrics(
                EvaluationDiagnostic.Error(
                    $"No {nameof(ChatConfiguration)} was provided. An IChatClient is required for judge evaluation."));
            return result;
        }

        var prompt = BuildJudgePrompt(messages, modelResponse, additionalContext);

        var sw = Stopwatch.StartNew();
        ChatResponse judgeResponse;
        try
        {
            judgeResponse = await chatConfiguration.ChatClient
                .GetResponseAsync(prompt, _judgeOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.AddDiagnosticsToAllMetrics(
                EvaluationDiagnostic.Error($"Judge LLM call failed: {ex.Message}"));
            return result;
        }
        finally
        {
            sw.Stop();
        }

        ParseJudgeResponse(judgeResponse.Text ?? string.Empty, result, judgeResponse, sw.Elapsed);
        result.MarkAllMetricsAsHpdBuiltIn();
        return result;
    }
}
