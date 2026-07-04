using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Converts tool results into short model-facing completion text.
/// </summary>
internal static class ToolResultText
{
    /// <summary>
    /// Converts a tool result into text suitable for background completion summaries.
    /// </summary>
    /// <param name="result">The tool result.</param>
    /// <returns>A text representation of the result.</returns>
    internal static string FromResult(object? result)
    {
        return result switch
        {
            null => string.Empty,
            string text => text,
            JsonElement json => json.GetRawText(),
            ToolResultPayload payload => payload.Text ?? payload.Json?.GetRawText() ?? string.Empty,
            ClientTools.TextContent text => text.Text,
            ClientTools.JsonContent json => json.Value.GetRawText(),
            ClientTools.BinaryContent binary => binary.Filename ?? binary.Id ?? binary.Url ?? binary.MimeType ?? string.Empty,
            IEnumerable<ClientTools.IToolResultContent> contents => string.Join(
                System.Environment.NewLine,
                contents.Select(FromResult)),
            _ => result.ToString() ?? string.Empty
        };
    }
}
