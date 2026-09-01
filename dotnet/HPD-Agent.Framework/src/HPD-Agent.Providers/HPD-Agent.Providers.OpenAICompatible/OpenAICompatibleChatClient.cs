using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace HPD.Agent.Providers.OpenAICompatible;

public class OpenAICompatibleChatClient : IChatClient
{
    private static readonly OpenAICompatibleJsonContext JsonContext = new(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });

    private readonly HttpClient _httpClient;
    private readonly OpenAICompatibleChatClientOptions _options;
    private readonly ChatClientMetadata _metadata;

    public OpenAICompatibleChatClient(HttpClient httpClient, OpenAICompatibleChatClientOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metadata = new ChatClientMetadata(
            providerName: options.ProviderKey,
            providerUri: options.ProviderUri,
            defaultModelId: options.DefaultModelId);
    }

    public ChatClientMetadata Metadata => _metadata;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var requestBody = BuildRequestBody(messages, options, stream: false);
        var requestJson = JsonSerializer.Serialize(requestBody, JsonContext.OpenAICompatibleChatRequest);

        using var request = CreateHttpRequest(requestJson);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateExceptionForErrorResponse(response, responseJson);
        }

        var envelope = JsonSerializer.Deserialize(responseJson, JsonContext.OpenAICompatibleChatResponseEnvelope)
            ?? throw new InvalidOperationException($"{_options.DisplayName} returned an empty chat response.");

        return ConvertToChatResponse(envelope);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var requestBody = BuildRequestBody(messages, options, stream: true);
        var requestJson = JsonSerializer.Serialize(requestBody, JsonContext.OpenAICompatibleChatRequest);

        using var request = CreateHttpRequest(requestJson);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw CreateExceptionForErrorResponse(response, errorBody);
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? responseId = null;
        string? modelId = null;
        DateTimeOffset? createdAt = null;
        ChatRole? role = null;
        ChatFinishReason? finishReason = null;
        var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || IsIgnoredStreamingLine(line))
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line.AsSpan("data:".Length).Trim();
            if (data.SequenceEqual("[DONE]".AsSpan()))
            {
                break;
            }

            var streamingResponse = JsonSerializer.Deserialize(data, JsonContext.OpenAICompatibleStreamingResponse);
            if (streamingResponse is null)
            {
                continue;
            }

            responseId ??= streamingResponse.Id;
            modelId ??= streamingResponse.Model;
            if (streamingResponse.Created > 0 && createdAt is null)
            {
                createdAt = DateTimeOffset.FromUnixTimeSeconds(streamingResponse.Created);
            }

            if (streamingResponse.Error is not null)
            {
                yield return CreateStreamingErrorUpdate(streamingResponse, responseId, modelId, createdAt, role);
                yield break;
            }

            if (streamingResponse.Usage is not null)
            {
                var usageUpdate = CreateBaseUpdate(streamingResponse, responseId, modelId, createdAt, role, finishReason);
                usageUpdate.Contents.Add(CreateUsageContent(streamingResponse.Usage));
                yield return usageUpdate;
                continue;
            }

            if (streamingResponse.Choices.Count == 0)
            {
                continue;
            }

            var choice = streamingResponse.Choices[0];
            var delta = choice.Delta;
            if (delta?.Role is not null && role is null)
            {
                role = MapRole(delta.Role);
            }

            if (choice.FinishReason is not null)
            {
                finishReason = MapFinishReason(choice.FinishReason);
            }

            ChatResponseUpdate? update = null;
            var deltaText = GetContentText(delta?.Content);
            if (!string.IsNullOrEmpty(deltaText))
            {
                update = CreateBaseUpdate(streamingResponse, responseId, modelId, createdAt, role, finishReason);
                update.Contents.Add(new TextContent(deltaText));
            }

            var reasoningText = delta?.ReasoningContent ?? delta?.Reasoning;
            if (!string.IsNullOrEmpty(reasoningText))
            {
                update ??= CreateBaseUpdate(streamingResponse, responseId, modelId, createdAt, role, finishReason);
                update.Contents.Add(new TextReasoningContent(reasoningText));
            }

            if (delta?.ToolCalls?.Count > 0)
            {
                AccumulateToolCallDeltas(toolCallAccumulators, delta.ToolCalls);
            }

            if (update is not null)
            {
                yield return update;
            }
        }

        var toolCalls = CreateFunctionCallContents(toolCallAccumulators);
        if (toolCalls.Count > 0)
        {
            var finalUpdate = new ChatResponseUpdate
            {
                ResponseId = responseId,
                MessageId = responseId,
                ModelId = modelId,
                CreatedAt = createdAt,
                Role = role,
                FinishReason = finishReason
            };
            foreach (var toolCall in toolCalls)
            {
                finalUpdate.Contents.Add(toolCall);
            }

            yield return finalUpdate;
        }
    }

    protected virtual OpenAICompatibleChatRequest BuildRequestBody(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        bool stream)
    {
        var requestMessages = new List<OpenAICompatibleRequestMessage>();

        if (!string.IsNullOrEmpty(options?.Instructions))
        {
            requestMessages.Add(new OpenAICompatibleRequestMessage
            {
                Role = "system",
                Content = CreateStringJsonElement(options.Instructions)
            });
        }

        foreach (var message in messages)
        {
            AddRequestMessage(requestMessages, message);
        }

        var request = new OpenAICompatibleChatRequest
        {
            Model = ResolveModelId(options),
            Messages = requestMessages,
            Stream = stream
        };

        ApplyOptions(request, options, stream);
        ConfigureRequest(request, options, stream);
        return request;
    }

    protected virtual string ResolveModelId(ChatOptions? options)
        => string.IsNullOrEmpty(options?.ModelId) ? _options.DefaultModelId : options.ModelId!;

    protected virtual void ConfigureRequest(OpenAICompatibleChatRequest request, ChatOptions? options, bool stream)
    {
    }

    protected virtual Exception CreateExceptionForErrorResponse(HttpResponseMessage response, string responseBody)
        => new HttpRequestException(
            $"{_options.DisplayName} API request failed [Status: {(int)response.StatusCode} {response.StatusCode}, Model: {_options.DefaultModelId}, Endpoint: {_options.ChatCompletionsPath}]. Response: {responseBody}",
            inner: null,
            statusCode: response.StatusCode);

    protected virtual bool IsIgnoredStreamingLine(string line)
        => line.StartsWith(':') || line.StartsWith("event:", StringComparison.Ordinal);

    protected virtual ChatResponse ConvertToChatResponse(OpenAICompatibleChatResponseEnvelope envelope)
    {
        var choice = envelope.Choices.FirstOrDefault();
        if (choice?.Message is null)
        {
            throw new InvalidOperationException($"{_options.DisplayName} returned no chat choices.");
        }

        var message = new ChatMessage
        {
            Role = MapRole(choice.Message.Role),
            RawRepresentation = choice.Message
        };

        var contentText = GetContentText(choice.Message.Content);
        if (!string.IsNullOrEmpty(contentText))
        {
            message.Contents.Add(new TextContent(contentText));
        }

        if (!string.IsNullOrEmpty(choice.Message.ReasoningContent) ||
            !string.IsNullOrEmpty(choice.Message.Reasoning))
        {
            message.Contents.Add(new TextReasoningContent(choice.Message.ReasoningContent ?? choice.Message.Reasoning));
        }

        if (choice.Message.ToolCalls is not null)
        {
            foreach (var toolCall in choice.Message.ToolCalls)
            {
                if (!string.IsNullOrEmpty(toolCall.Id) && toolCall.Function is not null)
                {
                    message.Contents.Add(CreateFunctionCallContent(
                        toolCall.Id,
                        toolCall.Function.Name ?? string.Empty,
                        toolCall.Function.Arguments ?? "{}"));
                }
            }
        }

        if (!string.IsNullOrEmpty(choice.Message.Refusal))
        {
            message.Contents.Add(new ErrorContent(choice.Message.Refusal)
            {
                ErrorCode = "Refusal"
            });
        }

        AddAnnotations(message, choice.Message.Annotations, envelope);

        var response = new ChatResponse(message)
        {
            ResponseId = envelope.Id,
            ModelId = envelope.Model,
            CreatedAt = envelope.Created > 0 ? DateTimeOffset.FromUnixTimeSeconds(envelope.Created) : null,
            FinishReason = MapFinishReason(choice.FinishReason),
            RawRepresentation = envelope
        };

        message.MessageId = envelope.Id;
        message.CreatedAt = response.CreatedAt;

        if (envelope.Usage is not null)
        {
            response.Usage = CreateUsageDetails(envelope.Usage);
        }

        return response;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceType switch
        {
            Type t when t == typeof(ChatClientMetadata) => _metadata,
            Type t when t == typeof(HttpClient) => _httpClient,
            Type t when t.IsInstanceOfType(this) => this,
            _ => null
        };
    }

    public void Dispose()
    {
    }

    public static string SerializeFunctionArguments(IDictionary<string, object?>? arguments)
        => arguments is null
            ? "{}"
            : JsonSerializer.Serialize(new Dictionary<string, object?>(arguments), JsonContext.DictionaryStringObject);

    private HttpRequestMessage CreateHttpRequest(string requestJson)
        => new(HttpMethod.Post, _options.ChatCompletionsPath)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

    private void AddRequestMessage(List<OpenAICompatibleRequestMessage> requestMessages, ChatMessage message)
    {
        if (message.Role == ChatRole.Tool)
        {
            var results = message.Contents.OfType<FunctionResultContent>().ToList();
            if (results.Count > 0)
            {
                foreach (var result in results)
                {
                    requestMessages.Add(new OpenAICompatibleRequestMessage
                    {
                        Role = "tool",
                        ToolCallId = result.CallId,
                        Content = CreateStringJsonElement(SerializeFunctionResult(result.Result))
                    });
                }
                return;
            }
        }

        var requestMessage = new OpenAICompatibleRequestMessage
        {
            Role = message.Role.Value.ToLowerInvariant(),
            Name = SanitizeAuthorName(message.AuthorName),
            Content = CreateMessageContent(message)
        };

        var toolCalls = message.Contents.OfType<FunctionCallContent>().ToList();
        if (toolCalls.Count > 0)
        {
            requestMessage.ToolCalls = toolCalls.Select(static call => new OpenAICompatibleRequestToolCall
            {
                Id = call.CallId,
                Type = "function",
                Function = new OpenAICompatibleRequestFunction
                {
                    Name = call.Name,
                    Arguments = SerializeFunctionArguments(call.Arguments)
                }
            }).ToList();
        }

        requestMessages.Add(requestMessage);
    }

    private JsonElement CreateMessageContent(ChatMessage message)
    {
        var parts = new List<OpenAICompatibleRequestContentPart>();
        var hasNonTextContent = false;

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent textContent:
                    parts.Add(new OpenAICompatibleRequestContentPart
                    {
                        Type = "text",
                        Text = textContent.Text
                    });
                    break;

                case UriContent uriContent when
                    _options.RequestProfile.Vision &&
                    uriContent.HasTopLevelMediaType("image"):
                    hasNonTextContent = true;
                    parts.Add(new OpenAICompatibleRequestContentPart
                    {
                        Type = "image_url",
                        ImageUrl = new OpenAICompatibleImageUrlContentPart
                        {
                            Url = uriContent.Uri.ToString(),
                            Detail = GetContentDetail(uriContent)
                        }
                    });
                    break;

                case DataContent dataContent when
                    _options.RequestProfile.Vision &&
                    dataContent.HasTopLevelMediaType("image"):
                    hasNonTextContent = true;
                    parts.Add(new OpenAICompatibleRequestContentPart
                    {
                        Type = "image_url",
                        ImageUrl = new OpenAICompatibleImageUrlContentPart
                        {
                            Url = dataContent.Uri,
                            Detail = GetContentDetail(dataContent)
                        }
                    });
                    break;
            }
        }

        if (!hasNonTextContent)
        {
            return CreateStringJsonElement(message.Text);
        }

        if (parts.Count == 0)
        {
            parts.Add(new OpenAICompatibleRequestContentPart
            {
                Type = "text",
                Text = string.Empty
            });
        }

        return CreateJsonElement(parts, JsonContext.ListOpenAICompatibleRequestContentPart);
    }

    private void ApplyOptions(OpenAICompatibleChatRequest request, ChatOptions? options, bool stream)
    {
        var profile = _options.RequestProfile;

        if (stream && profile.StreamingUsage)
        {
            request.StreamOptions = new OpenAICompatibleStreamOptions { IncludeUsage = true };
        }

        if (options is null)
        {
            return;
        }

        request.Temperature = profile.Temperature ? options.Temperature : null;
        request.TopP = profile.TopP ? options.TopP : null;
        request.TopK = profile.TopK ? options.TopK : null;
        request.FrequencyPenalty = profile.FrequencyPenalty ? options.FrequencyPenalty : null;
        request.PresencePenalty = profile.PresencePenalty ? options.PresencePenalty : null;
        request.Stop = profile.StopSequences && options.StopSequences?.Count > 0
            ? options.StopSequences.ToList()
            : null;
        request.Seed = profile.Seed ? options.Seed : null;

        switch (profile.MaxTokensField)
        {
            case OpenAICompatibleMaxTokensField.MaxTokens:
                request.MaxTokens = options.MaxOutputTokens;
                break;
            case OpenAICompatibleMaxTokensField.MaxCompletionTokens:
                request.MaxCompletionTokens = options.MaxOutputTokens;
                break;
        }

        request.ResponseFormat = CreateResponseFormat(options.ResponseFormat, options, profile);

        if (options.Reasoning is not null)
        {
            profile.ApplyReasoning?.Invoke(request, options.Reasoning);
        }

        if (profile.Tools && options.Tools?.Count > 0)
        {
            var strict = GetStrict(options.AdditionalProperties);
            var tools = options.Tools
                .OfType<AIFunctionDeclaration>()
                .Select(function => new OpenAICompatibleRequestTool
                {
                    Type = "function",
                    Function = new OpenAICompatibleRequestToolFunction
                    {
                        Name = function.Name,
                        Description = function.Description,
                        Parameters = EnsureObjectSchema(function.JsonSchema),
                        Strict = profile.StrictTools
                            ? GetStrict(function.AdditionalProperties) ?? strict
                            : null
                    }
                })
                .ToList();

            request.Tools = tools.Count > 0 ? tools : null;
            if (request.Tools is not null)
            {
                request.ToolChoice = CreateToolChoice(options.ToolMode, profile);
                request.ParallelToolCalls = profile.ParallelToolCalls
                    ? options.AllowMultipleToolCalls
                    : null;
            }
        }
    }

    private static OpenAICompatibleResponseFormatRequest? CreateResponseFormat(
        ChatResponseFormat? responseFormat,
        ChatOptions? options,
        OpenAICompatibleRequestProfile profile)
    {
        return responseFormat switch
        {
            ChatResponseFormatText when profile.TextResponseFormat =>
                new OpenAICompatibleResponseFormatRequest { Type = "text" },
            ChatResponseFormatJson json when
                profile.JsonSchemaResponseFormat &&
                json.Schema is JsonElement schema =>
                new OpenAICompatibleResponseFormatRequest
            {
                Type = "json_schema",
                JsonSchema = new OpenAICompatibleJsonSchemaResponseFormat
                {
                    Name = string.IsNullOrEmpty(json.SchemaName) ? "response" : json.SchemaName!,
                    Description = json.SchemaDescription,
                    Schema = schema,
                    Strict = profile.StrictJsonSchema
                        ? GetStrict(options?.AdditionalProperties)
                        : null
                }
            },
            ChatResponseFormatJson json when
                json.Schema is null &&
                profile.JsonObjectResponseFormat =>
                new OpenAICompatibleResponseFormatRequest { Type = "json_object" },
            _ => null
        };
    }

    private static JsonElement? CreateToolChoice(
        ChatToolMode? mode,
        OpenAICompatibleRequestProfile profile)
    {
        return mode switch
        {
            null or AutoChatToolMode when profile.AutoToolChoice =>
                CreateStringJsonElement("auto"),
            NoneChatToolMode when profile.NoneToolChoice =>
                CreateStringJsonElement("none"),
            RequiredChatToolMode { RequiredFunctionName: { } functionName } when profile.NamedToolChoice =>
                CreateToolChoiceJsonElement(functionName),
            RequiredChatToolMode when profile.RequiredToolChoice =>
                CreateStringJsonElement("required"),
            _ => null
        };
    }

    private static JsonElement CreateStringJsonElement(string value)
    {
        return CreateJsonElement(value, JsonContext.String);
    }

    private static JsonElement EnsureObjectSchema(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out _))
        {
            return schema;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static ChatResponseUpdate CreateBaseUpdate(
        OpenAICompatibleStreamingResponse raw,
        string? responseId,
        string? modelId,
        DateTimeOffset? createdAt,
        ChatRole? role,
        ChatFinishReason? finishReason)
        => new()
        {
            ResponseId = responseId,
            MessageId = responseId,
            ModelId = modelId,
            CreatedAt = createdAt,
            Role = role,
            FinishReason = finishReason,
            RawRepresentation = raw
        };

    private static ChatResponseUpdate CreateStreamingErrorUpdate(
        OpenAICompatibleStreamingResponse raw,
        string? responseId,
        string? modelId,
        DateTimeOffset? createdAt,
        ChatRole? role)
    {
        var update = CreateBaseUpdate(raw, responseId, modelId, createdAt, role, ChatFinishReason.Stop);
        update.Contents.Add(new ErrorContent(raw.Error?.Message ?? "Streaming error")
        {
            ErrorCode = raw.Error?.Code?.ToString()
        });
        return update;
    }

    private static void AccumulateToolCallDeltas(
        Dictionary<int, ToolCallAccumulator> accumulators,
        List<OpenAICompatibleToolCallDelta> deltas)
    {
        foreach (var delta in deltas)
        {
            if (!accumulators.TryGetValue(delta.Index, out var accumulator))
            {
                accumulator = new ToolCallAccumulator();
                accumulators[delta.Index] = accumulator;
            }

            accumulator.Id ??= delta.Id;
            accumulator.Type ??= delta.Type;
            if (delta.Function is not null)
            {
                accumulator.FunctionName ??= delta.Function.Name;
                if (!string.IsNullOrEmpty(delta.Function.Arguments))
                {
                    accumulator.Arguments.Append(delta.Function.Arguments);
                }
            }
        }
    }

    private static List<FunctionCallContent> CreateFunctionCallContents(Dictionary<int, ToolCallAccumulator> accumulators)
    {
        var contents = new List<FunctionCallContent>();
        foreach (var (_, accumulator) in accumulators.OrderBy(static entry => entry.Key))
        {
            if (!string.IsNullOrEmpty(accumulator.Id) && !string.IsNullOrEmpty(accumulator.FunctionName))
            {
                contents.Add(CreateFunctionCallContent(
                    accumulator.Id!,
                    accumulator.FunctionName!,
                    accumulator.Arguments.Length == 0 ? "{}" : accumulator.Arguments.ToString()));
            }
        }

        return contents;
    }

    private static FunctionCallContent CreateFunctionCallContent(string callId, string name, string arguments)
        => FunctionCallContent.CreateFromParsedArguments(
            string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments,
            callId,
            name,
            static json => JsonSerializer.Deserialize(json, JsonContext.IDictionaryStringObject) ?? new Dictionary<string, object?>());

    private static UsageContent CreateUsageContent(OpenAICompatibleUsage usage)
        => new(CreateUsageDetails(usage));

    private static UsageDetails CreateUsageDetails(OpenAICompatibleUsage usage)
    {
        var inputTokens = usage.PromptTokens ?? usage.InputTokens;
        var outputTokens = usage.CompletionTokens ?? usage.OutputTokens;
        var usageDetails = new UsageDetails
        {
            InputTokenCount = inputTokens,
            OutputTokenCount = outputTokens,
            TotalTokenCount = usage.TotalTokens ?? NullableSum(inputTokens, outputTokens),
            CachedInputTokenCount = usage.PromptTokenDetails?.CachedTokens ?? usage.InputTokenDetails?.CachedTokens,
            ReasoningTokenCount = usage.CompletionTokenDetails?.ReasoningTokens ?? usage.OutputTokenDetails?.ReasoningTokens,
#pragma warning disable MEAI001
            InputAudioTokenCount = usage.PromptTokenDetails?.AudioTokens ?? usage.InputTokenDetails?.AudioTokens,
            OutputAudioTokenCount = usage.CompletionTokenDetails?.AudioTokens ?? usage.OutputTokenDetails?.AudioTokens
#pragma warning restore MEAI001
        };

        AddPredictionCounts(usageDetails, usage.CompletionTokenDetails ?? usage.OutputTokenDetails);

        return usageDetails;
    }

    private static long? NullableSum(long? left, long? right)
        => left.HasValue || right.HasValue ? (left ?? 0) + (right ?? 0) : null;

    private static void AddPredictionCounts(
        UsageDetails usage,
        OpenAICompatibleTokenUsageDetails? details)
    {
        if (details is null)
        {
            return;
        }

        AddUsageCount(usage, AgentUsageCountKeys.AcceptedPredictionTokens, details.AcceptedPredictionTokens);
        AddUsageCount(usage, AgentUsageCountKeys.RejectedPredictionTokens, details.RejectedPredictionTokens);
    }

    private static void AddUsageCount(UsageDetails usage, string name, long? count)
    {
        if (count is null)
        {
            return;
        }

        usage.AdditionalCounts ??= [];
        usage.AdditionalCounts[name] = count.Value;
    }

    private static ChatRole MapRole(string? role)
        => role?.ToLowerInvariant() switch
        {
            "assistant" => ChatRole.Assistant,
            "user" => ChatRole.User,
            "system" => ChatRole.System,
            "tool" => ChatRole.Tool,
            null => ChatRole.Assistant,
            _ => new ChatRole(role)
        };

    private static ChatFinishReason? MapFinishReason(string? finishReason)
        => finishReason?.ToLowerInvariant() switch
        {
            "stop" => ChatFinishReason.Stop,
            "length" => ChatFinishReason.Length,
            "tool_calls" => ChatFinishReason.ToolCalls,
            "content_filter" => ChatFinishReason.ContentFilter,
            null => null,
            _ => new ChatFinishReason(finishReason)
        };

    private static JsonElement CreateToolChoiceJsonElement(string functionName)
    {
        var choice = new Dictionary<string, object?>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = functionName
            }
        };

        return CreateJsonElement(choice, JsonContext.DictionaryStringObject);
    }

    private static JsonElement CreateJsonElement<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    private static string GetContentText(JsonElement? content)
    {
        if (content is not { } value)
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                string.Empty,
                value.EnumerateArray()
                    .Where(static part => part.ValueKind == JsonValueKind.Object &&
                        part.TryGetProperty("type", out var type) &&
                        type.GetString() == "text" &&
                        part.TryGetProperty("text", out _))
                    .Select(static part => part.GetProperty("text").GetString())),
            _ => string.Empty
        };
    }

    private static string SerializeFunctionResult(object? result)
    {
        if (result is null)
        {
            return string.Empty;
        }

        if (result is string text)
        {
            return text;
        }

        var json = JsonSerializer.Serialize(
            result,
            (JsonTypeInfo<object>)AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)));
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            document.RootElement.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? GetContentDetail(AIContent content)
        => content.AdditionalProperties?.TryGetValue("detail", out var detail) == true
            ? detail?.ToString()
            : null;

    private static bool? GetStrict(IReadOnlyDictionary<string, object?>? properties)
        => properties?.TryGetValue("strict", out var strict) == true && strict is bool value
            ? value
            : null;

    private static string? SanitizeAuthorName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var builder = new StringBuilder(capacity: Math.Min(name.Length, 64));
        foreach (var ch in name)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch == '_')
            {
                builder.Append(ch);
                if (builder.Length == 64)
                {
                    break;
                }
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static void AddAnnotations(
        ChatMessage message,
        List<OpenAICompatibleAnnotation>? annotations,
        OpenAICompatibleChatResponseEnvelope envelope)
    {
        var citations = CreateCitationAnnotations(annotations, envelope);
        if (citations.Count == 0)
        {
            return;
        }

        var textContent = message.Contents.OfType<TextContent>().FirstOrDefault();
        if (textContent is null)
        {
            textContent = new TextContent(null);
            message.Contents.Add(textContent);
        }

        textContent.Annotations ??= [];
        foreach (var citation in citations)
        {
            textContent.Annotations.Add(citation);
        }
    }

    private static List<CitationAnnotation> CreateCitationAnnotations(
        List<OpenAICompatibleAnnotation>? annotations,
        OpenAICompatibleChatResponseEnvelope envelope)
    {
        var citations = new List<CitationAnnotation>();

        if (annotations is not null)
        {
            foreach (var annotation in annotations)
            {
                var source = annotation.UrlCitation;
                var url = source?.Url ?? annotation.Url;
                if (TryCreateUri(url, out var uri))
                {
                    citations.Add(new CitationAnnotation
                    {
                        Url = uri,
                        Title = source?.Title ?? annotation.Title,
                        RawRepresentation = annotation
                    });
                }
            }
        }

        if (envelope.SearchResults is not null)
        {
            foreach (var result in envelope.SearchResults)
            {
                if (TryCreateUri(result.Url, out var uri))
                {
                    citations.Add(new CitationAnnotation
                    {
                        Url = uri,
                        Title = result.Title,
                        Snippet = result.Snippet,
                        RawRepresentation = result
                    });
                }
            }
        }

        if (envelope.Citations is not null)
        {
            foreach (var citation in envelope.Citations)
            {
                if (TryCreateUri(citation, out var uri) &&
                    !citations.Any(existing => existing.Url == uri))
                {
                    citations.Add(new CitationAnnotation { Url = uri });
                }
            }
        }

        return citations;
    }

    private static bool TryCreateUri(string? url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private sealed class ToolCallAccumulator
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? FunctionName { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
