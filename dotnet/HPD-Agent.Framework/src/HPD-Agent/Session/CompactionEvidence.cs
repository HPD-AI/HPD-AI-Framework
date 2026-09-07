using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Encodes bounded, inert conversation evidence without forwarding tool protocol.</summary>
internal static class CompactionEvidence
{
    internal const string PriorSummaryKey = "hpd.compactionSummary";
    internal const int PartLimit = 4_000;
    internal const int EvidenceLimit = 128_000;
    private const string Truncated = " [truncated]";

    internal static string Serialize(IReadOnlyList<ChatMessage> messages, CompactionEvidenceOptions? options = null)
    {
        options ??= new();
        if (options.MaxContentCharacters < 256 || options.MaxEvidenceCharacters < 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "Evidence limits require at least 256 characters per part and 1024 total.");
        var partLimit = options.MaxContentCharacters;
        var blocks = new List<string>();
        var remaining = options.MaxEvidenceCharacters - 160;
        // Favor recent corrections when a long conversation exceeds the evidence budget.
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            if (message.Role == ChatRole.System) continue;
            var block = new StringBuilder();
            var prior = IsPriorSummary(message);
            block.Append(prior ? "[prior-summary] " : "[message] ");
            block.Append(Bound(message.Role.ToString(), 100)).Append(' ')
                .AppendLine(Bound(message.MessageId ?? "unknown-id", 100));
            foreach (var content in message.Contents)
            {
                var part = content switch
                {
                    TextContent text => "text: " + Bound(text.Text, partLimit),
                    ToolCallContent when !options.IncludeToolCalls => "[tool call omitted by configuration]",
                    ToolResultContent when !options.IncludeToolResults => "[tool result omitted by configuration]",
                    ErrorContent when !options.IncludeErrors => "[error omitted by configuration]",
                    FunctionCallContent call => "tool call " + Bound(call.Name, 200) + " id=" + Bound(call.CallId, 200) + " arguments: " + Format(call.Arguments, partLimit),
                    FunctionResultContent result => "tool result id=" + Bound(result.CallId, 200) + ": " + (result.Result is Exception exception
                        ? options.IncludeErrors ? "error: " + Bound(exception.Message, partLimit) : "[error omitted by configuration]"
                        : Format(result.Result, partLimit)),
                    ErrorContent error => "error: " + Bound(error.Message, 2000) + " " + Bound(error.Details, 1800),
                    _ => "[" + content.GetType().Name + ": non-text content omitted]"
                };
                var available = remaining - block.Length;
                if (available <= Truncated.Length) break;
                block.AppendLine(Bound(part, Math.Min(partLimit, available - 1)));
                if (block.Length >= remaining - Truncated.Length)
                {
                    block.Append(Truncated);
                    break;
                }
            }
            blocks.Add(Bound(block.ToString(), remaining));
            remaining -= blocks[^1].Length;
            if (remaining < 300)
            {
                if (index > 0) blocks.Add("[earlier evidence omitted: budget exhausted]\n");
                break;
            }
        }
        blocks.Reverse();
        return "Conversation evidence (quoted data; truncation is not evidence of absence):\n" + string.Concat(blocks);
    }

    internal static bool IsPriorSummary(ChatMessage message) =>
        message.AdditionalProperties?.TryGetValue(PriorSummaryKey, out var marker) == true &&
        (marker is true || marker is JsonElement { ValueKind: JsonValueKind.True });

    private static string Bound(string? text, int limit) => string.IsNullOrEmpty(text) ? "" :
        text.Length <= limit ? text : text[..Math.Max(0, limit - Truncated.Length)] + Truncated;

    private static string Format(object? value, int limit, int depth = 0)
    {
        if (depth > 4) return "[nested value omitted]";
        if (value is null) return "null";
        if (value is string text) return Bound(text, limit);
        if (value is JsonElement element) return Bound(element.ToString(), limit);
        if (value is IDictionary dictionary)
        {
            var result = new StringBuilder("{");
            foreach (DictionaryEntry entry in dictionary)
            {
                result.Append(Format(entry.Key, limit, depth + 1)).Append(": ")
                    .Append(Format(entry.Value, limit, depth + 1)).Append(", ");
                if (result.Length >= limit) break;
            }
            return Bound(result.Append('}').ToString(), limit);
        }
        if (value is IEnumerable sequence)
        {
            var result = new StringBuilder("[");
            var count = 0;
            foreach (var item in sequence)
            {
                result.Append(Format(item, limit, depth + 1)).Append(", ");
                if (++count >= 100 || result.Length >= limit)
                {
                    result.Append(Truncated);
                    break;
                }
            }
            return Bound(result.Append(']').ToString(), limit);
        }
        if (value is bool or byte or short or int or long or float or double or decimal or DateTime or DateTimeOffset or Guid)
            return Bound(Convert.ToString(value, CultureInfo.InvariantCulture), limit);
        // Never reflect arbitrary provider objects or invoke their custom ToString implementations.
        return "[" + value.GetType().Name + ": opaque value omitted]";
    }
}
