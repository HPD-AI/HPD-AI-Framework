using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Input;

internal static class RealtimeInputAudioPreparer
{
    public static ChatMessage PrepareMessage(ChatMessage message)
    {
        var changed = false;
        var contents = new List<AIContent>(message.Contents.Count);

        foreach (var content in message.Contents)
        {
            if (content is AudioContent audio)
            {
                contents.Add(audio.ToRealtimeInputAudio());
                changed = true;
                continue;
            }

            if (content is DataContent data && AudioContent.IsAudioMediaType(data.MediaType))
            {
                contents.Add(AudioContent.FromDataContent(data).ToRealtimeInputAudio());
                changed = true;
                continue;
            }

            contents.Add(content);
        }

        if (!changed)
        {
            return message;
        }

        return new ChatMessage(message.Role, contents)
        {
            AdditionalProperties = message.AdditionalProperties,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            RawRepresentation = message.RawRepresentation
        };
    }
}
