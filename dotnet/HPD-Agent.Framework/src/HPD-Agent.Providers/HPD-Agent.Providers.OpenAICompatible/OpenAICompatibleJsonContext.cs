using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.OpenAICompatible;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OpenAICompatibleChatRequest))]
[JsonSerializable(typeof(OpenAICompatibleRequestMessage))]
[JsonSerializable(typeof(OpenAICompatibleRequestToolCall))]
[JsonSerializable(typeof(OpenAICompatibleRequestFunction))]
[JsonSerializable(typeof(OpenAICompatibleRequestTool))]
[JsonSerializable(typeof(OpenAICompatibleRequestToolFunction))]
[JsonSerializable(typeof(OpenAICompatibleRequestContentPart))]
[JsonSerializable(typeof(OpenAICompatibleImageUrlContentPart))]
[JsonSerializable(typeof(List<OpenAICompatibleRequestContentPart>))]
[JsonSerializable(typeof(OpenAICompatibleResponseFormatRequest))]
[JsonSerializable(typeof(OpenAICompatibleJsonSchemaResponseFormat))]
[JsonSerializable(typeof(OpenAICompatibleStreamOptions))]
[JsonSerializable(typeof(OpenAICompatibleChatResponseEnvelope))]
[JsonSerializable(typeof(OpenAICompatibleChoice))]
[JsonSerializable(typeof(OpenAICompatibleResponseMessage))]
[JsonSerializable(typeof(OpenAICompatibleResponseToolCall))]
[JsonSerializable(typeof(OpenAICompatibleResponseFunctionCall))]
[JsonSerializable(typeof(OpenAICompatibleUsage))]
[JsonSerializable(typeof(OpenAICompatibleStreamingResponse))]
[JsonSerializable(typeof(OpenAICompatibleStreamingChoice))]
[JsonSerializable(typeof(OpenAICompatibleDelta))]
[JsonSerializable(typeof(OpenAICompatibleToolCallDelta))]
[JsonSerializable(typeof(OpenAICompatibleFunctionCallDelta))]
[JsonSerializable(typeof(OpenAICompatibleError))]
[JsonSerializable(typeof(OpenAICompatibleTokenUsageDetails))]
[JsonSerializable(typeof(OpenAICompatibleAnnotation))]
[JsonSerializable(typeof(OpenAICompatibleUrlCitation))]
[JsonSerializable(typeof(OpenAICompatibleSearchResult))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(IDictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public sealed partial class OpenAICompatibleJsonContext : JsonSerializerContext;
