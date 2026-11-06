using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.ClientTools;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal abstract record NormalizedToolResultPart;

internal sealed record NormalizedTextPart(string Text) : NormalizedToolResultPart;

internal sealed record NormalizedJsonPart(JsonElement Json) : NormalizedToolResultPart;

internal sealed record NormalizedAIContentPart(AIContent Content) : NormalizedToolResultPart;

internal sealed record NormalizedClientContentPart(IToolResultContent Content) : NormalizedToolResultPart;

internal sealed record NormalizedToolErrorPart(string Code, string Message) : NormalizedToolResultPart;

/// <summary>The one provider-neutral normalized result shared by Agent result consumers.</summary>
internal sealed record NormalizedToolResult(
    IReadOnlyList<NormalizedToolResultPart> Parts,
    string? DeclaredResultType)
{
    internal object? ToModelResult()
    {
        if (Parts.Count == 0)
            return null;
        if (Parts.Count == 1)
        {
            return Parts[0] switch
            {
                NormalizedTextPart text => text.Text,
                NormalizedJsonPart json => json.Json.Clone(),
                NormalizedAIContentPart ai => ai.Content,
                NormalizedClientContentPart client => client.Content,
                NormalizedToolErrorPart error => SerializeError(error),
                _ => throw Unsupported(Parts[0])
            };
        }

        var contents = new List<AIContent>(Parts.Count);
        foreach (var part in Parts)
        {
            contents.Add(part switch
            {
                NormalizedTextPart text => new Microsoft.Extensions.AI.TextContent(text.Text),
                NormalizedJsonPart json => new Microsoft.Extensions.AI.TextContent(json.Json.GetRawText()),
                NormalizedAIContentPart ai => ai.Content,
                NormalizedClientContentPart { Content: ClientTools.TextContent text } =>
                    new Microsoft.Extensions.AI.TextContent(text.Text),
                NormalizedClientContentPart { Content: ClientTools.JsonContent json } =>
                    new Microsoft.Extensions.AI.TextContent(json.Value.GetRawText()),
                NormalizedClientContentPart { Content: ClientTools.BinaryContent binary } =>
                    ToAIContent(binary),
                NormalizedToolErrorPart error => new Microsoft.Extensions.AI.TextContent(
                    SerializeError(error).GetRawText()),
                _ => throw Unsupported(part)
            });
        }

        return contents;
    }

    internal ToolResultPayload ToPayload()
    {
        if (Parts.Count == 0)
            return ToolResultPayload.FromResult(null);
        if (Parts.Count == 1)
        {
            return Parts[0] switch
            {
                NormalizedTextPart text => new ToolResultPayload(
                    Text: text.Text,
                    Json: JsonSerializer.SerializeToElement(text.Text, HPDJsonContext.Default.String),
                    ResultType: DeclaredResultType),
                NormalizedJsonPart json => new ToolResultPayload(
                    Text: json.Json.GetRawText(),
                    Json: json.Json.Clone(),
                    ResultType: DeclaredResultType),
                NormalizedAIContentPart ai => FromAIContent(ai.Content),
                NormalizedClientContentPart client => ToolResultPayload.FromResult(client.Content),
                NormalizedToolErrorPart error => new ToolResultPayload(
                    Text: SerializeError(error).GetRawText(),
                    Json: SerializeError(error),
                    ResultType: typeof(ToolResultError).FullName),
                _ => throw Unsupported(Parts[0])
            };
        }

        var content = new List<IToolResultContent>(Parts.Count);
        var textParts = new List<string>(Parts.Count);
        foreach (var part in Parts)
        {
            switch (part)
            {
                case NormalizedTextPart text:
                    textParts.Add(text.Text);
                    content.Add(new ClientTools.TextContent(text.Text));
                    break;
                case NormalizedJsonPart json:
                    textParts.Add(json.Json.GetRawText());
                    content.Add(new ClientTools.JsonContent(json.Json.Clone()));
                    break;
                case NormalizedAIContentPart ai:
                    var converted = ToPayload(ai.Content);
                    textParts.Add(ToolResultText.FromResult(converted.Content));
                    content.AddRange(converted.Content);
                    break;
                case NormalizedClientContentPart client:
                    textParts.Add(ToolResultText.FromResult(client.Content));
                    content.Add(client.Content);
                    break;
                case NormalizedToolErrorPart error:
                    textParts.Add(error.Message);
                    content.Add(new ClientTools.JsonContent(SerializeError(error)));
                    break;
                default:
                    throw Unsupported(part);
            }
        }

        return new ToolResultPayload(
            Text: string.Join(System.Environment.NewLine, textParts),
            Content: content,
            ResultType: DeclaredResultType);
    }

    internal string ToSummary()
    {
        foreach (var part in Parts)
        {
            var value = part switch
            {
                NormalizedTextPart text => text.Text,
                NormalizedJsonPart json => json.Json.GetRawText(),
                NormalizedToolErrorPart error => error.Message,
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(value))
                return value.Length <= 4096 ? value : value[..4096];
        }
        return string.Empty;
    }

    private static JsonElement SerializeError(NormalizedToolErrorPart error) =>
        JsonSerializer.SerializeToElement(
            new ToolResultError(error.Code, error.Message),
            HPDJsonContext.Default.ToolResultError);

    private static AIContent ToAIContent(ClientTools.BinaryContent binary)
    {
        if (!string.IsNullOrEmpty(binary.Data))
            return new DataContent(Convert.FromBase64String(binary.Data), binary.MimeType)
            {
                Name = binary.Filename
            };
        if (!string.IsNullOrEmpty(binary.Url) &&
            Uri.TryCreate(binary.Url, UriKind.Absolute, out var uri))
        {
            return new UriContent(uri, binary.MimeType);
        }
        throw new ToolResultNormalizationException(
            "tool_result_content_unrepresentable",
            $"Binary content '{binary.Filename ?? binary.Id ?? "unknown"}' has no provider-addressable data or HTTP(S) URL.");
    }

    private static ToolResultPayload FromAIContent(AIContent content)
    {
        if (content is Microsoft.Extensions.AI.TextContent text)
            return new ToolResultPayload(
                Text: text.Text,
                Json: JsonSerializer.SerializeToElement(text.Text, HPDJsonContext.Default.String),
                ResultType: content.GetType().FullName);
        if (content is DataContent data)
        {
            var binary = new ClientTools.BinaryContent(
                data.MediaType ?? "application/octet-stream",
                Data: Convert.ToBase64String(data.Data.ToArray()),
                Filename: data.Name);
            return new ToolResultPayload(
                Text: binary.Filename ?? binary.MimeType,
                Content: [binary],
                ResultType: content.GetType().FullName);
        }
        if (content is UriContent uri)
        {
            var binary = new ClientTools.BinaryContent(
                uri.MediaType ?? "application/octet-stream",
                Url: uri.Uri.AbsoluteUri);
            return new ToolResultPayload(
                Text: uri.Uri.AbsoluteUri,
                Content: [binary],
                ResultType: content.GetType().FullName);
        }
        throw new ToolResultNormalizationException(
            "tool_result_content_unrepresentable",
            $"AIContent type '{content.GetType().FullName}' cannot be projected to durable events.");
    }

    private static (string Text, IReadOnlyList<IToolResultContent> Content) ToPayload(AIContent content)
    {
        var payload = FromAIContent(content);
        return (payload.Text ?? string.Empty, payload.Content ?? []);
    }

    private static ToolResultNormalizationException Unsupported(NormalizedToolResultPart part) =>
        new("tool_result_part_unrepresentable",
            $"Normalized result part '{part.GetType().Name}' cannot be projected.");
}

internal static class ToolResultNormalizer
{
    internal static async ValueTask<NormalizedToolResult> NormalizeAsync(
        object? result,
        Type? declaredResultType,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default)
    {
        if (result is null)
            return new([], declaredResultType?.FullName);

        var parts = new List<NormalizedToolResultPart>();
        await AddAsync(result, declaredResultType, serializerOptions, parts, cancellationToken)
            .ConfigureAwait(false);
        return new(parts, declaredResultType?.FullName ?? result.GetType().FullName);
    }

    private static async ValueTask AddAsync(
        object result,
        Type? declaredResultType,
        JsonSerializerOptions serializerOptions,
        List<NormalizedToolResultPart> parts,
        CancellationToken cancellationToken)
    {
        switch (result)
        {
            case NormalizedToolResult normalized:
                parts.AddRange(normalized.Parts);
                return;
            case string text:
                parts.Add(new NormalizedTextPart(text));
                return;
            case JsonDocument sourceDocument:
                parts.Add(new NormalizedJsonPart(sourceDocument.RootElement.Clone()));
                return;
            case JsonElement json:
                parts.Add(new NormalizedJsonPart(json.Clone()));
                return;
            case JsonNode node:
                parts.Add(new NormalizedJsonPart(JsonSerializer.SerializeToElement(
                    node, HPDJsonContext.Default.JsonNode)));
                return;
            case ToolResultPayload payload:
                AddPayload(payload, parts);
                return;
            case ValidationErrorResponse validation:
                parts.Add(new NormalizedJsonPart(JsonSerializer.SerializeToElement(
                    validation, HPDJsonContext.Default.ValidationErrorResponse)));
                return;
            case AIContent content:
                parts.Add(new NormalizedAIContentPart(content));
                return;
            case IEnumerable<AIContent> contents:
                foreach (var item in contents)
                    parts.Add(new NormalizedAIContentPart(item));
                return;
            case IToolResultContent content:
                parts.Add(new NormalizedClientContentPart(content));
                return;
            case IEnumerable<IToolResultContent> contents:
                foreach (var item in contents)
                    parts.Add(new NormalizedClientContentPart(item));
                return;
            case Exception exception:
                parts.Add(new NormalizedToolErrorPart(
                    exception.GetType().Name,
                    exception.Message));
                return;
        }

        var resultType = declaredResultType ?? result.GetType();
        if (resultType == typeof(void) || resultType == typeof(Task) || resultType == typeof(ValueTask))
            return;

        JsonTypeInfo typeInfo;
        try
        {
            typeInfo = serializerOptions.GetTypeInfo(resultType);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            throw new ToolResultNormalizationException(
                "tool_result_projection_metadata_missing",
                $"No source-generated JSON metadata is available for tool result type '{resultType.FullName}'.",
                exception);
        }

        await using var stream = new MemoryStream();
        try
        {
            await JsonSerializer.SerializeAsync(stream, result, typeInfo, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new ToolResultNormalizationException(
                "tool_result_projection_failed",
                $"Tool result type '{resultType.FullName}' could not be serialized.",
                exception);
        }
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        parts.Add(new NormalizedJsonPart(document.RootElement.Clone()));
    }

    private static void AddPayload(ToolResultPayload payload, List<NormalizedToolResultPart> parts)
    {
        if (payload.Json is { } json)
            parts.Add(new NormalizedJsonPart(json.Clone()));
        else if (payload.Text is { } text)
            parts.Add(new NormalizedTextPart(text));
        if (payload.Content is { Count: > 0 } content)
            parts.AddRange(content.Select(item => new NormalizedClientContentPart(item)));
    }
}

/// <summary>Describes an intentionally rejected or unrepresentable tool result.</summary>
public sealed record ToolResultError(string Code, string Message);

internal sealed class ToolResultNormalizationException : Exception
{
    internal ToolResultNormalizationException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    internal string Code { get; }
}
