using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Output;

public sealed class AssistantTextExtractor
{
    public string Extract(AfterMessageTurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var finalResponseText = ExtractFromMessages(context.FinalResponse.Messages);
        if (!string.IsNullOrWhiteSpace(finalResponseText))
        {
            return finalResponseText;
        }

        var lastAssistant = context.TurnHistory
            .LastOrDefault(message => message.Role == ChatRole.Assistant);
        return lastAssistant is null
            ? string.Empty
            : ExtractFromContents(lastAssistant.Contents);
    }

    private static string ExtractFromMessages(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages.Reverse())
        {
            if (message.Role != ChatRole.Assistant)
            {
                continue;
            }

            var text = ExtractFromContents(message.Contents);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string ExtractFromContents(IEnumerable<AIContent> contents)
    {
        return string.Concat(contents
            .OfType<TextContent>()
            .Select(content => content.Text));
    }
}
