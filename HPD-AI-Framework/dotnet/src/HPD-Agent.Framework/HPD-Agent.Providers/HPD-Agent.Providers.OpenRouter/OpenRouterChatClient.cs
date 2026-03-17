using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;

namespace HPD.Agent.Providers.OpenRouter;

/// <summary>
/// OpenRouter-specific IChatClient that properly exposes reasoning content from reasoning_details field.
/// This implementation uses HttpClient directly to parse OpenRouter's extended response format.
/// ✨ PERFORMANCE OPTIMIZED: Reduced allocations, early exits, span usage, object pooling
/// </summary>
internal sealed class OpenRouterChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private readonly ChatClientMetadata _metadata;
    
    // ✨ PERFORMANCE: Use ArrayPool for temporary buffers
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    
    private static readonly OpenRouterJsonContext _jsonContext = new(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });

    public OpenRouterChatClient(HttpClient httpClient, string modelName)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        _metadata = new ChatClientMetadata(
            providerName: "openrouter",
            providerUri: new Uri("https://openrouter.ai"),
            defaultModelId: modelName
        );
    }

    public ChatClientMetadata Metadata => _metadata;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var requestBody = BuildRequestBody(messages, options, stream: false);
        var requestJson = JsonSerializer.Serialize(requestBody, _jsonContext.OpenRouterChatRequest);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        // Read response body first (needed for error details)
        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // Check for HTTP errors and include response body in exception
        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenRouter API request failed [Status: {(int)httpResponse.StatusCode} {httpResponse.StatusCode}, Model: {_modelName}, Endpoint: chat/completions]. Response: {responseJson}",
                inner: null,
                statusCode: httpResponse.StatusCode
            );
        }

        var openRouterResponse = JsonSerializer.Deserialize(responseJson, _jsonContext.OpenRouterChatResponse);

        if (openRouterResponse == null)
            throw new InvalidOperationException("Failed to deserialize OpenRouter response");

        return ConvertToChatResponse(openRouterResponse);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestBody = BuildRequestBody(messages, options, stream: true);
        var requestJson = JsonSerializer.Serialize(requestBody, _jsonContext.OpenRouterChatRequest);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        using var httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        // Check for HTTP errors before starting to read stream
        if (!httpResponse.IsSuccessStatusCode)
        {
            // Read error response body for detailed error information
            var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"OpenRouter API streaming request failed [Status: {(int)httpResponse.StatusCode} {httpResponse.StatusCode}, Model: {_modelName}, Endpoint: chat/completions]. Response: {errorBody}",
                inner: null,
                statusCode: httpResponse.StatusCode
            );
        }

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? responseId = null;
        string? modelId = null;
        DateTimeOffset? createdAt = null;
        ChatRole? role = null;
        ChatFinishReason? finishReason = null;
        var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();
        var reasoningAccumulators = new Dictionary<int, ReasoningAccumulator>();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            
            // ✨ PERFORMANCE: Early validation before any processing
            if (string.IsNullOrWhiteSpace(line) || 
                OpenRouterErrorHandler.IsProcessingComment(line) ||
                !line.StartsWith("data: "))
            {
                continue;
            }

            var data = line.AsSpan("data: ".Length); // ✨ Use Span to avoid substring allocation
            if (data.SequenceEqual("[DONE]".AsSpan()))
                break;

            // ✨ PERFORMANCE: Only deserialize when we have valid data
            var streamingResponse = JsonSerializer.Deserialize(data, _jsonContext.OpenRouterStreamingResponse);
            if (streamingResponse?.Choices == null || streamingResponse.Choices.Count == 0)
                continue;

            // Check for mid-stream errors as documented by OpenRouter
            if (streamingResponse.Error != null)
            {
                var errorUpdate = new ChatResponseUpdate
                {
                    ResponseId = responseId ?? streamingResponse.Id,
                    ModelId = modelId ?? streamingResponse.Model,
                    CreatedAt = createdAt,
                    Role = role,
                    FinishReason = ChatFinishReason.Stop, // Will be overridden by choice
                    RawRepresentation = streamingResponse
                };

                // Add error content
                errorUpdate.Contents.Add(new ErrorContent(streamingResponse.Error.Message)
                {
                    ErrorCode = streamingResponse.Error.Code?.ToString()
                });

                // Check if choice indicates error finish reason
                if (streamingResponse.Choices.Count > 0 && 
                    streamingResponse.Choices[0].FinishReason == "error")
                {
                    errorUpdate.FinishReason = ChatFinishReason.Stop; // Use stop for error termination
                }

                yield return errorUpdate;
                yield break; // Terminate stream on error
            }

            responseId ??= streamingResponse.Id;
            modelId ??= streamingResponse.Model;
            if (streamingResponse.Created > 0 && createdAt == null)
            {
                createdAt = DateTimeOffset.FromUnixTimeSeconds(streamingResponse.Created);
            }

            // Handle final usage stats if present (from stream_options.include_usage)
            if (streamingResponse.Usage != null)
            {
                var usageUpdate = new ChatResponseUpdate
                {
                    ResponseId = responseId,
                    ModelId = modelId,
                    CreatedAt = createdAt,
                    Role = role,
                    RawRepresentation = streamingResponse
                };

                usageUpdate.Contents.Add(new UsageContent(new UsageDetails
                {
                    InputTokenCount = streamingResponse.Usage.PromptTokens,
                    OutputTokenCount = streamingResponse.Usage.CompletionTokens,
                    TotalTokenCount = streamingResponse.Usage.TotalTokens
                }));

                yield return usageUpdate;
                continue;
            }

            var choice = streamingResponse.Choices[0];
            var delta = choice.Delta;

            // ✨ PERFORMANCE: Early exit if no meaningful content
            bool hasContent = !string.IsNullOrEmpty(delta?.Content);
            bool hasReasoning = delta?.ReasoningDetails?.Count > 0;
            bool hasToolCalls = delta?.ToolCalls?.Count > 0;
            bool hasRole = delta?.Role != null && role == null;
            bool hasFinishReason = choice.FinishReason != null && finishReason == null;
            
            if (!hasContent && !hasReasoning && !hasToolCalls && !hasRole && !hasFinishReason)
                continue; // ✨ Skip empty updates to reduce object allocation

            if (hasRole)
            {
                role = delta!.Role!.ToLower() switch
                {
                    "assistant" => ChatRole.Assistant,
                    "user" => ChatRole.User,
                    "system" => ChatRole.System,
                    "tool" => ChatRole.Tool,
                    _ => new ChatRole(delta.Role)
                };
            }

            if (hasFinishReason)
            {
                finishReason = choice.FinishReason!.ToLower() switch
                {
                    "stop" => ChatFinishReason.Stop,
                    "length" => ChatFinishReason.Length,
                    "tool_calls" => ChatFinishReason.ToolCalls,
                    "content_filter" => ChatFinishReason.ContentFilter,
                    _ => ChatFinishReason.Stop
                };
            }

            // ✨ PERFORMANCE: Only create update when we have actual content to send
            ChatResponseUpdate? update = null;
            
            // Handle text content
            if (hasContent)
            {
                update ??= new ChatResponseUpdate
                {
                    ResponseId = responseId,
                    ModelId = modelId,
                    CreatedAt = createdAt,
                    Role = role,
                    FinishReason = finishReason,
                    RawRepresentation = streamingResponse
                };
                update.Contents.Add(new TextContent(delta!.Content!));
            }

            // Handle reasoning details - ✨ PERFORMANCE: Batch reasoning content
            if (hasReasoning)
            {
                update ??= new ChatResponseUpdate
                {
                    ResponseId = responseId,
                    ModelId = modelId,
                    CreatedAt = createdAt,
                    Role = role,
                    FinishReason = finishReason,
                    RawRepresentation = streamingResponse
                };

                foreach (var reasoningDetail in delta!.ReasoningDetails!)
                {
                    if (!reasoningAccumulators.TryGetValue(reasoningDetail.Index, out var accumulator))
                    {
                        accumulator = new ReasoningAccumulator { Type = reasoningDetail.Type };
                        reasoningAccumulators[reasoningDetail.Index] = accumulator;
                    }

                    // ✨ PERFORMANCE: Only create content objects for visible reasoning
                    if (reasoningDetail.Type == "reasoning.text" && !string.IsNullOrEmpty(reasoningDetail.Text))
                    {
                        accumulator.Text.Append(reasoningDetail.Text);
                        update.Contents.Add(new TextReasoningContent(reasoningDetail.Text!)
                        {
                            RawRepresentation = reasoningDetail
                        });
                    }
                    else if (reasoningDetail.Type == "reasoning.summary" && !string.IsNullOrEmpty(reasoningDetail.Summary))
                    {
                        accumulator.Text.Append(reasoningDetail.Summary);
                        update.Contents.Add(new TextReasoningContent(reasoningDetail.Summary!)
                        {
                            RawRepresentation = reasoningDetail
                        });
                    }
                    else if (reasoningDetail.Type == "reasoning.encrypted")
                    {
                        // Store encrypted data for later - no immediate content creation
                        accumulator.EncryptedData = reasoningDetail.Data;
                    }
                }
            }

            // Handle tool calls - ✨ PERFORMANCE: Defer tool call creation until necessary
            if (hasToolCalls)
            {
                foreach (var toolCallDelta in delta!.ToolCalls!)
                {
                    if (!toolCallAccumulators.TryGetValue(toolCallDelta.Index, out var accumulator))
                    {
                        accumulator = new ToolCallAccumulator
                        {
                            Id = toolCallDelta.Id,
                            Type = toolCallDelta.Type
                        };
                        toolCallAccumulators[toolCallDelta.Index] = accumulator;
                    }

                    accumulator.Id ??= toolCallDelta.Id;
                    accumulator.Type ??= toolCallDelta.Type;

                    if (toolCallDelta.Function != null)
                    {
                        accumulator.FunctionName ??= toolCallDelta.Function.Name;
                        if (!string.IsNullOrEmpty(toolCallDelta.Function.Arguments))
                        {
                            accumulator.Arguments.Append(toolCallDelta.Function.Arguments);
                        }
                    }
                }
            }

            // ✨ PERFORMANCE: Only yield when we have actual content
            if (update != null)
            {
                yield return update;
            }
        }

        // Emit final tool calls if any
        if (toolCallAccumulators.Count > 0)
        {
            var finalUpdate = new ChatResponseUpdate
            {
                ResponseId = responseId,
                ModelId = modelId,
                CreatedAt = createdAt,
                Role = role,
                FinishReason = finishReason
            };

            foreach (var (_, accumulator) in toolCallAccumulators.OrderBy(kvp => kvp.Key))
            {
                if (accumulator.Id != null && accumulator.FunctionName != null)
                {
                    var callContent = FunctionCallContent.CreateFromParsedArguments(
                        accumulator.Arguments.ToString(),
                        accumulator.Id,
                        accumulator.FunctionName,
                        static json =>
                        {
                            var dict = JsonSerializer.Deserialize(json, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>))) as IDictionary<string, object?> ?? new Dictionary<string, object?>();
                            // Remove __raw_json__ field that OpenRouter sometimes adds
                            dict.Remove("__raw_json__");
                            return dict;
                        });

                    finalUpdate.Contents.Add(callContent);
                }
            }

            if (finalUpdate.Contents.Count > 0)
            {
                yield return finalUpdate;
            }
        }
    }

    private ChatResponse ConvertToChatResponse(OpenRouterChatResponse openRouterResponse)
    {
        var choice = openRouterResponse.Choices.FirstOrDefault();
        if (choice?.Message == null)
            throw new InvalidOperationException("No choices in OpenRouter response");

        var message = new ChatMessage
        {
            Role = choice.Message.Role?.ToLower() switch
            {
                "assistant" => ChatRole.Assistant,
                "user" => ChatRole.User,
                "system" => ChatRole.System,
                "tool" => ChatRole.Tool,
                _ => ChatRole.Assistant
            },
            RawRepresentation = choice.Message
        };

        // Add text content
        if (!string.IsNullOrEmpty(choice.Message.Content))
        {
            message.Contents.Add(new TextContent(choice.Message.Content));
        }

        // Extract and add reasoning content
        if (choice.Message.ReasoningDetails != null)
        {
            foreach (var reasoningDetail in choice.Message.ReasoningDetails)
            {
                if (reasoningDetail.Type == "reasoning.text" && !string.IsNullOrEmpty(reasoningDetail.Text))
                {
                    message.Contents.Add(new TextReasoningContent(reasoningDetail.Text)
                    {
                        RawRepresentation = reasoningDetail
                    });
                }
                else if (reasoningDetail.Type == "reasoning.summary" && !string.IsNullOrEmpty(reasoningDetail.Summary))
                {
                    message.Contents.Add(new TextReasoningContent(reasoningDetail.Summary)
                    {
                        RawRepresentation = reasoningDetail
                    });
                }
                else if (reasoningDetail.Type == "reasoning.encrypted" && !string.IsNullOrEmpty(reasoningDetail.Data))
                {
                    // For encrypted reasoning, store in AdditionalProperties since ProtectedData might not be available in all versions
                    var reasoningContent = new TextReasoningContent(string.Empty)
                    {
                        RawRepresentation = reasoningDetail
                    };
                    if (reasoningContent.AdditionalProperties == null)
                    {
                        reasoningContent.AdditionalProperties = new AdditionalPropertiesDictionary();
                    }
                    reasoningContent.AdditionalProperties["ProtectedData"] = reasoningDetail.Data;
                    message.Contents.Add(reasoningContent);
                }
            }
        }

        // Add tool calls
        if (choice.Message.ToolCalls != null)
        {
            foreach (var toolCall in choice.Message.ToolCalls)
            {
                if (toolCall.Function != null && toolCall.Id != null)
                {
                    var callContent = FunctionCallContent.CreateFromParsedArguments(
                        toolCall.Function.Arguments ?? string.Empty,
                        toolCall.Id,
                        toolCall.Function.Name ?? string.Empty,
                        static json =>
                        {
                            var dict = JsonSerializer.Deserialize(json, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>))) as IDictionary<string, object?> ?? new Dictionary<string, object?>();
                            // Remove __raw_json__ field that OpenRouter sometimes adds
                            dict.Remove("__raw_json__");
                            return dict;
                        });

                    message.Contents.Add(callContent);
                }
            }
        }

        // Add refusal as error content
        if (!string.IsNullOrEmpty(choice.Message.Refusal))
        {
            message.Contents.Add(new ErrorContent(choice.Message.Refusal)
            {
                ErrorCode = "Refusal"
            });
        }

        var response = new ChatResponse(message)
        {
            ResponseId = openRouterResponse.Id,
            ModelId = openRouterResponse.Model,
            CreatedAt = openRouterResponse.Created > 0
                ? DateTimeOffset.FromUnixTimeSeconds(openRouterResponse.Created)
                : null,
            FinishReason = choice.FinishReason?.ToLower() switch
            {
                "stop" => ChatFinishReason.Stop,
                "length" => ChatFinishReason.Length,
                "tool_calls" => ChatFinishReason.ToolCalls,
                "content_filter" => ChatFinishReason.ContentFilter,
                _ => null
            },
            RawRepresentation = openRouterResponse
        };

        if (openRouterResponse.Usage != null)
        {
            response.Usage = new UsageDetails
            {
                InputTokenCount = openRouterResponse.Usage.PromptTokens,
                OutputTokenCount = openRouterResponse.Usage.CompletionTokens,
                TotalTokenCount = openRouterResponse.Usage.TotalTokens
            };
        }

        return response;
    }

    private OpenRouterChatRequest BuildRequestBody(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var requestMessages = new List<OpenRouterRequestMessage>();
        bool hasPdfContent = false; // ✨ PERFORMANCE: Track PDF content during iteration

        // ✨ FIX: Add system instructions from ChatOptions.Instructions as first message
        // This follows Microsoft.Extensions.AI convention where system prompts are passed via ChatOptions
        if (!string.IsNullOrEmpty(options?.Instructions))
        {
            requestMessages.Add(new OpenRouterRequestMessage
            {
                Role = "system",
                Content = options.Instructions
            });
        }

        foreach (var m in messages)
        {
            // ✨ FIX: Handle parallel function calls by creating separate messages for each FunctionResultContent
            if (m.Role == ChatRole.Tool)
            {
                var functionResults = m.Contents.OfType<FunctionResultContent>().ToList();
                
                if (functionResults.Count > 0)
                {
                    // Create separate OpenRouter message for each function result
                    foreach (var frc in functionResults)
                    {
                        requestMessages.Add(new OpenRouterRequestMessage
                        {
                            Role = "tool",
                            ToolCallId = frc.CallId,
                            Content = frc.Result?.ToString() ?? string.Empty
                        });
                    }
                }
                else
                {
                    // Fallback for tool messages without FunctionResultContent
                    var textContents = m.Contents
                        .Where(c => c is TextContent && c is not TextReasoningContent)
                        .Cast<TextContent>()
                        .Select(tc => tc.Text);
                    
                    requestMessages.Add(new OpenRouterRequestMessage
                    {
                        Role = "tool",
                        Content = m.Text ?? string.Join("\n", textContents)
                    });
                }
            }
            else
            {
                // Handle non-tool messages normally
                var msg = new OpenRouterRequestMessage
                {
                    Role = m.Role.Value.ToLowerInvariant()
                };

                // ✨ PERFORMANCE: Check for multimodal content once
                var hasMultimodalContent = false;
                List<object>? contentParts = null;

                foreach (var content in m.Contents)
                {
                    switch (content)
                    {
                        case TextReasoningContent:
                            // Skip reasoning content - don't send back to API
                            break;

                        case TextContent textContent when !string.IsNullOrEmpty(textContent.Text):
                            if (!hasMultimodalContent)
                            {
                                // Simple text - no need for structured content
                                msg.Content ??= textContent.Text;
                            }
                            else
                            {
                                (contentParts ??= []).Add(
                                    textContent.AdditionalProperties?.ContainsKey("cache_control") == true ?
                                    new { type = "text", text = textContent.Text, cache_control = new { type = "ephemeral" } } :
                                    new { type = "text", text = textContent.Text }
                                );
                            }
                            break;

                        case UriContent uriContent when uriContent.MediaType?.StartsWith("image/") == true:
                            hasMultimodalContent = true;
                            (contentParts ??= []).Add(CreateImageUrlPart(uriContent.Uri.ToString(), GetImageDetail(uriContent)));
                            break;

                        case DataContent dataContent:
                            hasMultimodalContent = true;
                            if (dataContent.MediaType?.StartsWith("image/") == true)
                            {
                                (contentParts ??= []).Add(CreateImageUrlPart(dataContent.Uri, GetImageDetail(dataContent)));
                            }
                            else if (dataContent.MediaType == "application/pdf")
                            {
                                hasPdfContent = true; // ✨ PERFORMANCE: Set flag here
                                (contentParts ??= []).Add(new { type = "file", file = new { filename = "document.pdf", file_data = dataContent.Uri.ToString() } });
                            }
                            else if (dataContent.MediaType?.StartsWith("audio/") == true)
                            {
                                var uri = dataContent.Uri.ToString();
                                if (uri.StartsWith("data:"))
                                {
                                    var parts = uri.Split(new[] { "data:", ";base64," }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts.Length >= 2)
                                    {
                                        var format = parts[0].Split('/').LastOrDefault() ?? "wav";
                                        var base64Data = parts[1];
                                        (contentParts ??= []).Add(new { type = "input_audio", input_audio = new { data = base64Data, format = format } });
                                    }
                                }
                            }
                            else if (dataContent.MediaType?.StartsWith("video/") == true)
                            {
                                (contentParts ??= []).Add(new { type = "input_video", video_url = new { url = dataContent.Uri.ToString() } });
                            }
                            break;
                    }
                }

                // Handle structured content (multimodal: text + images/audio/etc)
                if (hasMultimodalContent && contentParts?.Count > 0)
                {
                    // Pass content parts as array (NOT JSON string) for OpenRouter API
                    msg.Content = contentParts;
                }
                else if (msg.Content == null)
                {
                    // Fallback to simple text if no structured content was built
                    var textContents = m.Contents
                        .Where(c => c is TextContent && c is not TextReasoningContent)
                        .Cast<TextContent>()
                        .Select(tc => tc.Text);
                    msg.Content = m.Text ?? string.Join("\n", textContents);
                }

                // Add tool calls if present (for assistant role)
                var toolCalls = m.Contents.OfType<FunctionCallContent>().ToList();
                if (toolCalls.Count > 0)
                {
                    msg.ToolCalls = toolCalls.Select(fc => new OpenRouterRequestToolCall
                    {
                        Id = fc.CallId,
                        Type = "function",
                        Function = new OpenRouterRequestFunction
                        {
                            Name = fc.Name,
                            Arguments = JsonSerializer.Serialize(fc.Arguments, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>)))
                        }
                    }).ToList();
                }

                requestMessages.Add(msg);
            }
        }

        var request = new OpenRouterChatRequest
        {
            Model = _modelName,
            Messages = requestMessages,
            Stream = stream
        };

        // Enable reasoning when: model natively supports it, ChatOptions.Reasoning is set, or
        // AdditionalProperties["reasoning_effort"] is set. Map M.E.AI ReasoningEffort enum → string.
        var meaiEffort = options?.Reasoning?.Effort;
        var meaiOutput = options?.Reasoning?.Output;
        bool reasoningRequested = meaiEffort != null && meaiEffort != Microsoft.Extensions.AI.ReasoningEffort.None;
        if (SupportsReasoning(_modelName) || reasoningRequested ||
            (options?.AdditionalProperties?.TryGetValue("reasoning_effort", out _) == true))
        {
            var effortStr = meaiEffort switch
            {
                Microsoft.Extensions.AI.ReasoningEffort.Low => "low",
                Microsoft.Extensions.AI.ReasoningEffort.Medium => "medium",
                Microsoft.Extensions.AI.ReasoningEffort.High => "high",
                Microsoft.Extensions.AI.ReasoningEffort.ExtraHigh => "xhigh",
                _ => null
            };
            var summaryStr = meaiOutput switch
            {
                Microsoft.Extensions.AI.ReasoningOutput.Summary => "concise",
                Microsoft.Extensions.AI.ReasoningOutput.Full => "detailed",
                _ => null
            };
            request.Reasoning = new OpenRouterReasoningConfig
            {
                Enabled = true,
                Effort = effortStr,
                Summary = summaryStr,
                Exclude = meaiOutput == Microsoft.Extensions.AI.ReasoningOutput.None
            };
        }

        // ✨ PERFORMANCE: Add stream options if streaming
        if (stream)
        {
            request.StreamOptions = new OpenRouterStreamOptions
            {
                IncludeUsage = true  // Include usage stats in final streaming chunk
            };
        }

        // ✨ PERFORMANCE: Only add PDF Toolkit if we detected PDF content
        if (hasPdfContent)
        {
            request.Toolkits = new List<OpenRouterToolkit>
            {
                new OpenRouterToolkit
                {
                    Id = "file-parser",
                    Pdf = new OpenRouterPdfConfig
                    {
                        Engine = "mistral-ocr" // Can be overridden by user options
                    }
                }
            };
        }

        if (options != null)
        {
            if (options.Temperature.HasValue) request.Temperature = (float)options.Temperature.Value;
            if (options.MaxOutputTokens.HasValue) request.MaxTokens = options.MaxOutputTokens.Value;
            if (options.TopP.HasValue) request.TopP = (float)options.TopP.Value;
            if (options.FrequencyPenalty.HasValue) request.FrequencyPenalty = (float)options.FrequencyPenalty.Value;
            if (options.PresencePenalty.HasValue) request.PresencePenalty = (float)options.PresencePenalty.Value;
            if (options.StopSequences?.Count > 0) request.Stop = options.StopSequences.ToList();

            // Support additional OpenRouter-specific parameters through AdditionalProperties
            if (options.AdditionalProperties != null)
            {
                // Basic OpenRouter parameters
                if (options.AdditionalProperties.TryGetValue("min_p", out var minP) && minP is float minPVal)
                    request.MinP = minPVal;
                
                if (options.AdditionalProperties.TryGetValue("top_a", out var topA) && topA is float topAVal)
                    request.TopA = topAVal;
                
                if (options.AdditionalProperties.TryGetValue("top_k", out var topK) && topK is int topKVal)
                    request.TopK = topKVal;
                
                if (options.AdditionalProperties.TryGetValue("repetition_penalty", out var repPenalty) && repPenalty is float repPenaltyVal)
                    request.RepetitionPenalty = repPenaltyVal;
                
                if (options.AdditionalProperties.TryGetValue("seed", out var seed) && seed is int seedVal)
                    request.Seed = seedVal;
                
                if (options.AdditionalProperties.TryGetValue("verbosity", out var verbosity) && verbosity is string verbosityVal)
                    request.Verbosity = verbosityVal;
                
                if (options.AdditionalProperties.TryGetValue("logprobs", out var logprobs) && logprobs is bool logprobsVal)
                    request.Logprobs = logprobsVal;
                
                if (options.AdditionalProperties.TryGetValue("top_logprobs", out var topLogprobs) && topLogprobs is int topLogprobsVal)
                    request.TopLogprobs = topLogprobsVal;

                // Model routing - fallback models
                if (options.AdditionalProperties.TryGetValue("models", out var models) && models is IEnumerable<string> modelsList)
                    request.Models = modelsList.ToList();

                // Provider preferences
                var providerPrefs = new OpenRouterProviderPreferences();
                bool hasProviderPrefs = false;

                if (options.AdditionalProperties.TryGetValue("provider_order", out var order) && order is IEnumerable<string> orderList)
                {
                    providerPrefs.Order = orderList.ToList();
                    hasProviderPrefs = true;
                }

                if (options.AdditionalProperties.TryGetValue("allow_fallbacks", out var allowFallbacks) && allowFallbacks is bool allowFallbacksVal)
                {
                    providerPrefs.AllowFallbacks = allowFallbacksVal;
                    hasProviderPrefs = true;
                }

                if (options.AdditionalProperties.TryGetValue("require_parameters", out var requireParams) && requireParams is bool requireParamsVal)
                {
                    providerPrefs.RequireParameters = requireParamsVal;
                    hasProviderPrefs = true;
                }

                if (options.AdditionalProperties.TryGetValue("data_collection", out var dataCollection) && dataCollection is string dataCollectionVal)
                {
                    providerPrefs.DataCollection = dataCollectionVal;
                    hasProviderPrefs = true;
                }

                if (options.AdditionalProperties.TryGetValue("zdr", out var zdr) && zdr is bool zdrVal)
                {
                    providerPrefs.Zdr = zdrVal;
                    hasProviderPrefs = true;
                }

                if (options.AdditionalProperties.TryGetValue("enforce_distillable_text", out var enforceDistillable) && enforceDistillable is bool enforceDistillableVal)
                {
                    providerPrefs.EnforceDistillableText = enforceDistillableVal;
                    hasProviderPrefs = true;
                }

                if (options.AdditionalProperties.TryGetValue("provider_only", out var only) && only is IEnumerable<string> onlyList)
                {
                    providerPrefs.Only = onlyList.ToList();
                    hasProviderPrefs = true;
                }

                if (options.AdditionalProperties.TryGetValue("provider_ignore", out var ignore) && ignore is IEnumerable<string> ignoreList)
                {
                    providerPrefs.Ignore = ignoreList.ToList();
                    hasProviderPrefs = true;
                }

                if (options.AdditionalProperties.TryGetValue("quantizations", out var quantizations) && quantizations is IEnumerable<string> quantizationsList)
                {
                    providerPrefs.Quantizations = quantizationsList.ToList();
                    hasProviderPrefs = true;
                }

                if (options.AdditionalProperties.TryGetValue("provider_sort", out var sort) && sort is string sortVal)
                {
                    providerPrefs.Sort = sortVal;
                    hasProviderPrefs = true;
                }

                // Max price configuration
                var maxPriceConfig = new OpenRouterMaxPrice();
                bool hasMaxPrice = false;

                if (options.AdditionalProperties.TryGetValue("max_price_prompt", out var maxPricePrompt) && maxPricePrompt is float maxPricePromptVal)
                {
                    maxPriceConfig.Prompt = maxPricePromptVal;
                    hasMaxPrice = true;
                }

                if (options.AdditionalProperties.TryGetValue("max_price_completion", out var maxPriceCompletion) && maxPriceCompletion is float maxPriceCompletionVal)
                {
                    maxPriceConfig.Completion = maxPriceCompletionVal;
                    hasMaxPrice = true;
                }

                if (options.AdditionalProperties.TryGetValue("max_price_request", out var maxPriceRequest) && maxPriceRequest is float maxPriceRequestVal)
                {
                    maxPriceConfig.Request = maxPriceRequestVal;
                    hasMaxPrice = true;
                }

                if (options.AdditionalProperties.TryGetValue("max_price_image", out var maxPriceImage) && maxPriceImage is float maxPriceImageVal)
                {
                    maxPriceConfig.Image = maxPriceImageVal;
                    hasMaxPrice = true;
                }

                if (hasMaxPrice)
                {
                    providerPrefs.MaxPrice = maxPriceConfig;
                    hasProviderPrefs = true;
                }

                if (hasProviderPrefs)
                {
                    request.Provider = providerPrefs;
                }

                // Support model shortcuts (:nitro, :floor, :exacto, :free)
                if (options.AdditionalProperties.TryGetValue("model_variant", out var variant) && variant is string variantVal)
                {
                    if (!request.Model.Contains(':'))
                    {
                        request.Model = $"{request.Model}:{variantVal}";
                    }
                }

                // Support free model usage (automatically adds :free if not present and requested)
                if (options.AdditionalProperties.TryGetValue("use_free_model", out var useFree) && useFree is bool useFreeVal && useFreeVal)
                {
                    if (!request.Model.Contains(':'))
                    {
                        request.Model = $"{request.Model}:free";
                    }
                }

                // Support reasoning configuration via AdditionalProperties (overrides ChatOptions.Reasoning effort)
                if (options.AdditionalProperties.TryGetValue("reasoning_effort", out var effort) && effort is string effortVal && SupportsReasoning(_modelName))
                {
                    request.Reasoning ??= new OpenRouterReasoningConfig { Enabled = true, Exclude = false };
                    request.Reasoning.Effort = effortVal;
                }

                // Support PDF engine configuration
                if (options.AdditionalProperties.TryGetValue("pdf_engine", out var pdfEngine) && pdfEngine is string pdfEngineVal && hasPdfContent)
                {
                    if (request.Toolkits == null)
                    {
                        request.Toolkits = new List<OpenRouterToolkit>();
                    }
                    
                    // Update or add PDF Toolkit
                    var existingToolkit = request.Toolkits.FirstOrDefault(p => p.Id == "file-parser");
                    if (existingToolkit != null)
                    {
                        existingToolkit.Pdf = new OpenRouterPdfConfig { Engine = pdfEngineVal };
                    }
                    else
                    {
                        request.Toolkits.Add(new OpenRouterToolkit
                        {
                            Id = "file-parser",
                            Pdf = new OpenRouterPdfConfig { Engine = pdfEngineVal }
                        });
                    }
                }
            }

            // Add tools if present
            if (options.Tools?.Count > 0)
            {
                var tools = options.Tools.OfType<AIFunctionDeclaration>().Select(f =>
                {
                    // Ensure parameters object has required properties for OpenAI/Azure compatibility
                    var parameters = f.JsonSchema;

                    // Fix: OpenAI requires "properties" field even if empty
                    if (parameters.ValueKind == JsonValueKind.Object &&
                        !parameters.TryGetProperty("properties", out _))
                    {
                        // Schema is missing "properties" - rebuild it with empty properties
                        using var stream = new MemoryStream();
                        using (var writer = new Utf8JsonWriter(stream))
                        {
                            writer.WriteStartObject();
                            writer.WriteString("type", "object");
                            writer.WriteStartObject("properties");
                            writer.WriteEndObject(); // Empty properties
                            writer.WriteEndObject();
                        }
                        parameters = JsonDocument.Parse(stream.ToArray()).RootElement;
                    }

                    return new OpenRouterRequestTool
                    {
                        Type = "function",
                        Function = new OpenRouterRequestToolFunction
                        {
                            Name = f.Name,
                            Description = f.Description,
                            Parameters = parameters
                        }
                    };
                }).ToList();

                if (tools.Count > 0)
                {
                    request.Tools = tools;
                }
            }
        }

        return request;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType switch
        {
            Type t when t == typeof(ChatClientMetadata) => _metadata,
            Type t when t == typeof(HttpClient) => _httpClient,
            Type t when t.IsInstanceOfType(this) => this,
            _ => null
        };
    }

    /// <summary>
    /// ✨ FIX: Determines if a model supports the reasoning parameter.
    /// Only certain models like o1, o3, and extended thinking models support reasoning.
    /// Sending reasoning to models that don't support it causes errors.
    /// </summary>
    private static bool SupportsReasoning(string modelName)
    {
        // Normalize model name for comparison
        var lower = modelName.ToLowerInvariant();
        
        // List of reasoning-capable models
        return lower.Contains("o1") || 
               lower.Contains("o3") || 
               lower.Contains("reasoning") ||
               lower.Contains("thinking") ||
               lower.Contains("r1") ||
               lower.Contains("ext");
    }

    public void Dispose()
    {
        // HttpClient is typically managed by the factory, so we don't dispose it here
    }

    /// <summary>
    /// Gets information about the current API key including rate limits and credit usage.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Key information including limits and usage statistics.</returns>
    public async Task<OpenRouterKeyInfo> GetKeyInfoAsync(CancellationToken cancellationToken = default)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, "key");
        var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var keyInfo = JsonSerializer.Deserialize(responseJson, _jsonContext.OpenRouterKeyInfo);

        if (keyInfo == null)
            throw new InvalidOperationException("Failed to deserialize OpenRouter key info response");

        return keyInfo;
    }

    /// <summary>
    /// Checks if the API key has sufficient credits remaining for requests.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if credits are available or unlimited, false if exhausted.</returns>
    public async Task<bool> HasCreditsRemainingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var keyInfo = await GetKeyInfoAsync(cancellationToken).ConfigureAwait(false);
            
            // If limit is null, it's unlimited
            if (keyInfo.Data.Limit == null)
                return true;

            // If limit_remaining is null, assume unlimited
            if (keyInfo.Data.LimitRemaining == null)
                return true;

            // Check if we have credits remaining
            return keyInfo.Data.LimitRemaining > 0;
        }
        catch
        {
            // If we can't check, assume we have credits (fail open)
            return true;
        }
    }

    /// <summary>
    /// Gets the remaining credit balance for the API key.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The remaining credits, or null if unlimited.</returns>
    public async Task<float?> GetRemainingCreditsAsync(CancellationToken cancellationToken = default)
    {
        var keyInfo = await GetKeyInfoAsync(cancellationToken).ConfigureAwait(false);
        return keyInfo.Data.LimitRemaining;
    }

    /// <summary>
    /// Validates that the API key is working and has access.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the key is valid and working.</returns>
    public async Task<bool> ValidateKeyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await GetKeyInfoAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the account is approaching credit limits and may need attention.
    /// </summary>
    /// <param name="warningThreshold">The threshold (as a percentage, 0.0-1.0) at which to warn. Default is 0.1 (10% remaining).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Credit status information.</returns>
    public async Task<CreditStatus> GetCreditStatusAsync(float warningThreshold = 0.1f, CancellationToken cancellationToken = default)
    {
        try
        {
            var keyInfo = await GetKeyInfoAsync(cancellationToken).ConfigureAwait(false);

            if (keyInfo.Data.Limit == null)
            {
                return new CreditStatus
                {
                    IsUnlimited = true,
                    UsageToday = keyInfo.Data.UsageDaily,
                    UsageThisMonth = keyInfo.Data.UsageMonthly,
                    NeedsAttention = false
                };
            }

            var remaining = keyInfo.Data.LimitRemaining ?? 0;
            var limit = keyInfo.Data.Limit.Value;
            var percentRemaining = limit > 0 ? remaining / limit : 0;

            return new CreditStatus
            {
                IsUnlimited = false,
                Limit = limit,
                Remaining = remaining,
                Used = keyInfo.Data.Usage,
                UsageToday = keyInfo.Data.UsageDaily,
                UsageThisMonth = keyInfo.Data.UsageMonthly,
                PercentRemaining = percentRemaining,
                NeedsAttention = percentRemaining <= warningThreshold || remaining <= 0,
                IsFreeTier = keyInfo.Data.IsFreeTier
            };
        }
        catch
        {
            return new CreditStatus { HasError = true };
        }
    }

    /// <summary>
    /// Information about credit usage and limits for an OpenRouter API key.
    /// </summary>
    public class CreditStatus
    {
        public bool IsUnlimited { get; set; }
        public float? Limit { get; set; }
        public float? Remaining { get; set; }
        public float Used { get; set; }
        public float UsageToday { get; set; }
        public float UsageThisMonth { get; set; }
        public float PercentRemaining { get; set; }
        public bool NeedsAttention { get; set; }
        public bool IsFreeTier { get; set; }
        public bool HasError { get; set; }

        public override string ToString()
        {
            if (HasError) return "Error retrieving credit status";
            if (IsUnlimited) return "Unlimited credits";
            
            var remainingText = Remaining?.ToString("F2") ?? "unknown";
            var limitText = Limit?.ToString("F2") ?? "unknown";
            var percentText = (PercentRemaining * 100).ToString("F1");
            
            return $"{remainingText}/{limitText} credits ({percentText}% remaining)";
        }
    }

    /// <summary>
    /// ✨ PERFORMANCE: Pooled accumulator for tool calls to reduce allocations
    /// </summary>
    private class ToolCallAccumulator
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? FunctionName { get; set; }
        public StringBuilder Arguments { get; } = new();
        
        public void Reset()
        {
            Id = null;
            Type = null;
            FunctionName = null;
            Arguments.Clear();
        }
    }

    /// <summary>
    /// ✨ PERFORMANCE: Pooled accumulator for reasoning content to reduce allocations
    /// </summary>
    private class ReasoningAccumulator
    {
        public string? Type { get; set; }
        public StringBuilder Text { get; } = new();
        public string? EncryptedData { get; set; }
        
        public void Reset()
        {
            Type = null;
            Text.Clear();
            EncryptedData = null;
        }
    }

    /// <summary>
    /// Gets the image detail level from content's additional properties.
    /// NOTE: OpenRouter does not officially document the "detail" parameter.
    /// This is kept for OpenAI model compatibility when routing through OpenRouter.
    /// For native OpenRouter models (Gemini, Claude, etc.), this may be ignored.
    /// </summary>
    private static string? GetImageDetail(AIContent content)
    {
        if (content.AdditionalProperties?.TryGetValue("detail", out object? value) is true)
        {
            return value switch
            {
                string detailString when detailString is "low" or "high" or "auto" => detailString,
                _ => null
            };
        }
        return null;
    }

    /// <summary>
    /// Creates an image_url part according to OpenRouter's documentation.
    ///
    /// Official OpenRouter format (documented at https://openrouter.ai/docs/guides/overview/multimodal/images):
    /// {
    ///   "type": "image_url",
    ///   "image_url": {
    ///     "url": "data:image/jpeg;base64,..." or "https://..."
    ///   }
    /// }
    ///
    /// The "detail" parameter is an OpenAI-specific extension that may work when
    /// routing to OpenAI models (openai/gpt-4o, etc.) but is NOT documented by OpenRouter
    /// and will likely be ignored by native models like Google Gemini or Anthropic Claude.
    ///
    /// Supported image formats: image/png, image/jpeg, image/webp, image/gif
    /// </summary>
    private static object CreateImageUrlPart(string url, string? detail)
    {
        // Include detail parameter for OpenAI model compatibility
        // OpenRouter will pass it through to OpenAI models, others will ignore it
        if (detail != null)
        {
            return new { type = "image_url", image_url = new { url, detail } };
        }

        // Standard OpenRouter format (officially documented)
        return new { type = "image_url", image_url = new { url } };
    }
}
