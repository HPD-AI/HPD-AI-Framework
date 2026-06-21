using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.OnnxRuntime;

internal sealed class StructuredToolCallingOnnxRuntimeChatClient : IChatClient
{
    private const string ToolInstructions = """
        When a tool is needed, return only JSON in this exact shape:
        {"tool_call":{"name":"<tool name>","arguments":{}}}

        The "name" value must exactly match one of the available tool names.
        The "arguments" value must be a JSON object matching that tool's parameters.
        Available tools:
        """;

    private readonly IChatClient _innerClient;

    public StructuredToolCallingOnnxRuntimeChatClient(IChatClient innerClient)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
    }

    public void Dispose() => _innerClient.Dispose();

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this :
            _innerClient.GetService(serviceType, serviceKey);
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (!TryCreateStructuredToolOptions(options, out var structuredOptions, out var allowedToolNames))
        {
            return await _innerClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        var response = await _innerClient.GetResponseAsync(messages, structuredOptions, cancellationToken).ConfigureAwait(false);
        if (!TryParseToolCallEnvelope(response.Text, allowedToolNames, out var toolCall))
        {
            return response;
        }

        return CreateToolCallResponse(response, toolCall);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (!ShouldUseStructuredToolCalling(options))
        {
            await foreach (var update in _innerClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }

            yield break;
        }

        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    internal static bool TryCreateStructuredToolOptions(
        ChatOptions? options,
        out ChatOptions structuredOptions,
        out HashSet<string> allowedToolNames)
    {
        structuredOptions = options?.Clone() ?? new ChatOptions();
        allowedToolNames = [];

        var functionTools = GetFunctionTools(structuredOptions).ToArray();
        if (structuredOptions.ToolMode is NoneChatToolMode || functionTools.Length == 0)
        {
            return false;
        }

        var toolJson = BuildToolJson(functionTools);
        foreach (var function in functionTools)
        {
            allowedToolNames.Add(function.Name);
        }

        var schema = BuildToolCallEnvelopeSchema(allowedToolNames);
        structuredOptions.ResponseFormat = new ChatResponseFormatJson(
            schema,
            schemaName: "hpd_tool_call",
            schemaDescription: "A single HPD tool-call envelope.");

        structuredOptions.AllowMultipleToolCalls = false;
        structuredOptions.Instructions = AppendInstructions(structuredOptions.Instructions, toolJson);

        return true;
    }

    internal static string BuildToolJson(IEnumerable<AIFunctionDeclaration> functions)
    {
        var toolArray = new JsonArray();

        foreach (var function in functions)
        {
            var functionObject = new JsonObject
            {
                ["name"] = function.Name,
                ["description"] = function.Description ?? string.Empty,
                ["parameters"] = JsonNode.Parse(function.JsonSchema.GetRawText()) ?? new JsonObject()
            };

            toolArray.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = functionObject
            });
        }

        return toolArray.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    internal static bool TryParseToolCallEnvelope(
        string text,
        ISet<string> allowedToolNames,
        out FunctionCallContent? toolCall)
    {
        toolCall = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(ExtractJson(text));
            var root = document.RootElement;
            var envelope = root;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("tool_call", out var nested))
            {
                envelope = nested;
            }

            if (envelope.ValueKind != JsonValueKind.Object ||
                !envelope.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || !allowedToolNames.Contains(name))
            {
                return false;
            }

            Dictionary<string, object?>? arguments = null;
            if (envelope.TryGetProperty("arguments", out var argumentsElement) &&
                argumentsElement.ValueKind == JsonValueKind.Object)
            {
                arguments = ReadObject(argumentsElement);
            }

            toolCall = new FunctionCallContent(
                "onnx_call_" + Guid.NewGuid().ToString("N"),
                name,
                arguments ?? []);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ShouldUseStructuredToolCalling(ChatOptions? options)
        => options?.ToolMode is not NoneChatToolMode &&
           options?.Tools?.OfType<AIFunctionDeclaration>().Any() == true;

    private static IEnumerable<AIFunctionDeclaration> GetFunctionTools(ChatOptions options)
        => options.Tools?.OfType<AIFunctionDeclaration>() ?? [];

    private static ChatResponse CreateToolCallResponse(ChatResponse source, FunctionCallContent toolCall)
    {
        var message = new ChatMessage(ChatRole.Assistant, [toolCall])
        {
            RawRepresentation = source
        };

        return new ChatResponse(message)
        {
            AdditionalProperties = source.AdditionalProperties,
            ConversationId = source.ConversationId,
            CreatedAt = source.CreatedAt,
            FinishReason = source.FinishReason,
            ModelId = source.ModelId,
            RawRepresentation = source,
            ResponseId = source.ResponseId,
            Usage = source.Usage
        };
    }

    private static JsonElement BuildToolCallEnvelopeSchema(IEnumerable<string> toolNames)
    {
        var enumValues = new JsonArray();
        foreach (var toolName in toolNames.OrderBy(static name => name, StringComparer.Ordinal))
        {
            enumValues.Add(toolName);
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["tool_call"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = enumValues
                        },
                        ["arguments"] = new JsonObject
                        {
                            ["type"] = "object"
                        }
                    },
                    ["required"] = new JsonArray("name", "arguments"),
                    ["additionalProperties"] = false
                }
            },
            ["required"] = new JsonArray("tool_call"),
            ["additionalProperties"] = false
        };

        using var document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }

    private static string AppendInstructions(string? existingInstructions, string toolJson)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(existingInstructions))
        {
            builder.AppendLine(existingInstructions.Trim());
            builder.AppendLine();
        }

        builder.AppendLine(ToolInstructions);
        builder.Append(toolJson);

        return builder.ToString();
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return ExtractFirstJsonObject(trimmed);
        }

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine)
        {
            return trimmed;
        }

        return ExtractFirstJsonObject(trimmed[(firstNewLine + 1)..lastFence].Trim());
    }

    private static string ExtractFirstJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return text;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(i + 1)];
                }
            }
        }

        return text[start..].Trim();
    }

    private static Dictionary<string, object?> ReadObject(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ReadValue(property.Value);
        }

        return result;
    }

    private static object? ReadValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ReadObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ReadValue).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => ReadNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null
        };
    }

    private static object ReadNumber(JsonElement element)
    {
        if (element.TryGetInt32(out var int32))
        {
            return int32;
        }

        if (element.TryGetInt64(out var int64))
        {
            return int64;
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        return element.GetDouble();
    }
}
