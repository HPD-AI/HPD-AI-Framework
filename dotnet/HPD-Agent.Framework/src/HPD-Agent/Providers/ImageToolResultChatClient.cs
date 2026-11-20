using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers;

/// <summary>Adapts image tool results to the format supported by the provider connection.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ImageToolResultChatClient(IChatClient innerClient, bool supportsImages = true,
    Func<IReadOnlyList<AIContent>, object>? formatImageResult = null) : DelegatingChatClient(innerClient)
{
    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        InnerClient.GetResponseAsync(ConvertToolMessages(messages), options, cancellationToken);

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        InnerClient.GetStreamingResponseAsync(ConvertToolMessages(messages), options, cancellationToken);

    private IEnumerable<ChatMessage> ConvertToolMessages(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            ChatMessage? converted = null;
            if (message.Role == ChatRole.Tool)
            {
                for (var index = 0; index < message.Contents.Count; index++)
                {
                    if (message.Contents[index] is not FunctionResultContent { Exception: null } result ||
                        ReadImageParts(result.Result) is not { } parts)
                        continue;

                    if (converted is null)
                    {
                        converted = message.Clone();
                        converted.Contents = message.Contents.ToList();
                    }

                    var formatted = supportsImages
                        ? formatImageResult?.Invoke(parts) ?? parts
                        : DescribeImageParts(parts);
                    converted.Contents[index] = new FunctionResultContent(result.CallId, formatted)
                    {
                        AdditionalProperties = result.AdditionalProperties
                    };
                }
            }

            yield return converted ?? message;
        }
    }

    /// <summary>Describes a captured image for APIs whose tool results can only carry text.</summary>
    public static string DescribeImageParts(IEnumerable<AIContent> parts) =>
        string.Join("\n", parts.OfType<TextContent>().Select(text => text.Text)
            .Append("The tool returned an image. This provider connection accepts text-only tool results."));

    /// <summary>Reads typed or serialized image and text from a tool result.</summary>
    public static IReadOnlyList<AIContent>? ReadImageParts(object? result)
    {
        if (result is DataContent image && image.HasTopLevelMediaType("image"))
            return [image];
        if (result is IEnumerable<AIContent> typed)
        {
            var items = typed.ToArray();
            return items.All(item => item is TextContent || (item is DataContent data && data.HasTopLevelMediaType("image"))) &&
                items.Any(item => item is DataContent) ? items : null;
        }

        if (result is not JsonElement { ValueKind: JsonValueKind.Object } envelope ||
            !envelope.TryGetProperty("content", out var blocks) ||
            blocks.ValueKind != JsonValueKind.Array ||
            (envelope.TryGetProperty("isError", out var error) &&
                error.ValueKind is not (JsonValueKind.False or JsonValueKind.Null)))
            return null;

        var parts = new List<AIContent>();
        var hasImage = false;
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object ||
                !block.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                return null;

            switch (type.GetString())
            {
                case "text" when block.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String:
                    parts.Add(new TextContent(text.GetString()));
                    break;
                case "image" when block.TryGetProperty("mimeType", out var mime) &&
                    mime.ValueKind == JsonValueKind.String &&
                    mime.GetString() is "image/png" or "image/jpeg" or "image/gif" or "image/webp" &&
                    block.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String &&
                    data.TryGetBytesFromBase64(out var bytes) && bytes.Length > 0:
                    parts.Add(new DataContent(bytes, mime.GetString()!));
                    hasImage = true;
                    break;
                default:
                    return null;
            }
        }

        return hasImage ? parts : null;
    }
}
