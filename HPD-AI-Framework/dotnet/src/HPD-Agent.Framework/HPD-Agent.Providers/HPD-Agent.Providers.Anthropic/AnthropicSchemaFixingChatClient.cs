using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Anthropic;

/// <summary>
/// A delegating IChatClient wrapper that fixes the Anthropic SDK's tool schema bug.
///
/// WHY THIS EXISTS:
/// The Anthropic C# SDK v12.x has a bug where it transforms AIFunction schemas incorrectly,
/// placing properties at the top level instead of nesting them under "properties" key.
/// This causes API rejection: "tools.X.custom.input_schema: JSON schema is invalid"
///
/// HOW IT WORKS:
/// 1. Intercepts ChatOptions.Tools before they reach the SDK
/// 2. For each AIFunctionDeclaration, creates a properly formatted Tool object
/// 3. Wraps the fixed Tool using SDK's AsAITool() extension
/// 4. The SDK sees the wrapped Tool and uses it directly, bypassing the buggy transformation
///
/// TEMPORARY NATURE:
/// Once Anthropic fixes the SDK, this wrapper can be removed entirely.
/// </summary>
internal sealed class AnthropicSchemaFixingChatClient : IChatClient
{
    private readonly IChatClient _innerClient;

    public AnthropicSchemaFixingChatClient(IChatClient innerClient)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
    }

    public ChatClientMetadata Metadata =>
        _innerClient.GetService<ChatClientMetadata>() ??
        new ChatClientMetadata("anthropic");

    public void Dispose() => _innerClient.Dispose();

    public object? GetService(System.Type serviceType, object? serviceKey = null)
        => _innerClient.GetService(serviceType, serviceKey);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fixedOptions = FixToolSchemas(options);
        return _innerClient.GetResponseAsync(messages, fixedOptions, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fixedOptions = FixToolSchemas(options);
        return _innerClient.GetStreamingResponseAsync(messages, fixedOptions, cancellationToken);
    }

    /// <summary>
    /// Fixes tool schemas by converting AIFunctionDeclarations to pre-fixed Tool objects.
    /// </summary>
    private static ChatOptions? FixToolSchemas(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 })
            return options;

        // Check if any tools need fixing
        bool needsFixing = options.Tools.Any(t => t is AIFunctionDeclaration);
        if (!needsFixing)
            return options;

        // Create fixed tools
        var fixedTools = new List<AITool>();
        foreach (var tool in options.Tools)
        {
            if (tool is AIFunctionDeclaration af)
            {
                // Create the fixed Tool and wrap it
                var fixedTool = CreateFixedTool(af);
                var toolUnion = new ToolUnion(fixedTool);
                fixedTools.Add(toolUnion.AsAITool());
            }
            else
            {
                // Keep non-AIFunction tools as-is
                fixedTools.Add(tool);
            }
        }

        // Clone the options with fixed tools
        return new ChatOptions
        {
            Tools = fixedTools,
            ModelId = options.ModelId,
            MaxOutputTokens = options.MaxOutputTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK,
            FrequencyPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            StopSequences = options.StopSequences,
            ResponseFormat = options.ResponseFormat,
            Seed = options.Seed,
            ToolMode = options.ToolMode,
            AdditionalProperties = options.AdditionalProperties,
        };
    }

    /// <summary>
    /// Creates a Tool with properly nested InputSchema.
    ///
    /// This is the fix for the Anthropic SDK bug. The SDK's buggy code does:
    ///   InputSchema = new(properties) { Required = required }
    ///
    /// Which places properties at the top level. We fix it by:
    ///   InputSchema = InputSchema.FromRawUnchecked(inputSchemaData)
    ///
    /// With inputSchemaData properly structured as:
    /// {
    ///   "type": "object",
    ///   "properties": { ... },  // ← Nested correctly
    ///   "required": [ ... ]
    /// }
    /// </summary>
    private static Tool CreateFixedTool(AIFunctionDeclaration af)
    {
        Dictionary<string, JsonElement> properties = [];
        List<string> required = [];
        JsonElement inputSchema = af.JsonSchema;

        // Extract properties and required from the AIFunction's JsonSchema
        if (inputSchema.ValueKind is JsonValueKind.Object)
        {
            if (inputSchema.TryGetProperty("properties", out JsonElement propsElement) &&
                propsElement.ValueKind is JsonValueKind.Object)
            {
                foreach (JsonProperty p in propsElement.EnumerateObject())
                {
                    properties[p.Name] = p.Value;
                }
            }

            if (inputSchema.TryGetProperty("required", out JsonElement reqElement) &&
                reqElement.ValueKind is JsonValueKind.Array)
            {
                foreach (JsonElement r in reqElement.EnumerateArray())
                {
                    if (r.ValueKind is JsonValueKind.String && r.GetString() is { } s)
                    {
                        required.Add(s);
                    }
                }
            }
        }

        // Build InputSchema with properly nested "properties" key
        var inputSchemaData = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object", AnthropicJsonContext.Default.String)
        };

        if (properties.Count > 0)
        {
            inputSchemaData["properties"] = JsonSerializer.SerializeToElement(properties, AnthropicJsonContext.Default.DictionaryStringJsonElement);
        }

        if (required.Count > 0)
        {
            inputSchemaData["required"] = JsonSerializer.SerializeToElement(required, AnthropicJsonContext.Default.ListString);
        }

        // Use FromRawUnchecked to bypass the buggy InputSchema constructor
        return new Tool
        {
            Name = af.Name,
            Description = af.Description,
            InputSchema = InputSchema.FromRawUnchecked(inputSchemaData),
        };
    }
}
