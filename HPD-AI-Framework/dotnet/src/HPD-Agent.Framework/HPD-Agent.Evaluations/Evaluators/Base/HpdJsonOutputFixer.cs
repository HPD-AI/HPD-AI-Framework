// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Evaluators;

/// <summary>
/// Internal utility that replicates the behavior of MS's internal JsonOutputFixer.
/// Used by HpdJsonJudgeEvaluatorBase on JSON parse failure.
/// </summary>
internal static class HpdJsonOutputFixer
{
    private static readonly ChatOptions _repairOptions = new()
    {
        Temperature = 0f,
        ResponseFormat = ChatResponseFormat.Json,
    };

    /// <summary>
    /// Strips leading/trailing whitespace, backtick fences, and the 'json' language
    /// marker from markdown-formatted JSON responses.
    /// </summary>
    internal static ReadOnlySpan<char> TrimMarkdownDelimiters(string json)
    {
        var span = json.AsSpan().Trim();

        // Strip ```json ... ``` or ``` ... ```
        if (span.StartsWith("```", StringComparison.Ordinal))
        {
            span = span.Slice(3);
            if (span.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                span = span.Slice(4);
            span = span.TrimStart();

            int end = span.LastIndexOf("```", StringComparison.Ordinal);
            if (end >= 0)
                span = span.Slice(0, end).TrimEnd();
        }

        return span;
    }

    /// <summary>
    /// Makes a second LLM call to repair malformed JSON. Returns the repaired JSON
    /// string (trimmed). Never returns null — returns empty string if the repair call
    /// produces an empty response. Throws if the LLM call itself throws.
    /// </summary>
    internal static async ValueTask<string> RepairJsonAsync(
        string malformedJson,
        IChatClient chatClient,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                "Fix any syntax errors in the supplied JSON. Conform strictly to the JSON " +
                "standard. Return only valid JSON with no markdown or explanation."),
            new(ChatRole.User, malformedJson),
        };

        var response = await chatClient
            .GetResponseAsync(messages, _repairOptions, ct)
            .ConfigureAwait(false);

        var text = response.Text ?? string.Empty;
        var trimmed = TrimMarkdownDelimiters(text);
        return trimmed.ToString();
    }
}
