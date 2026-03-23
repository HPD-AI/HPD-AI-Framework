using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;


namespace HPD.Agent;

/// <summary>
/// JSON serialization context for Session types (AOT-compatible).
/// Combines M.E.AI JsonContext with HPD-specific session types.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
// Session types
[JsonSerializable(typeof(Session))]
[JsonSerializable(typeof(Branch))]
[JsonSerializable(typeof(SessionSnapshot))]
[JsonSerializable(typeof(UncommittedTurn))]

// HPD-specific types
[JsonSerializable(typeof(AgentLoopState))]
[JsonSerializable(typeof(ValidationErrorResponse))]

// M.E.AI types (explicitly added for session persistence)
// Note: Most M.E.AI types are registered via AIJsonUtilities.DefaultOptions
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(List<ChatMessage>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(JsonElement))]

// HPD-Agent Typed Content Classes (Phase 1 - Typed Content)
// These must be serializable for session persistence
[JsonSerializable(typeof(HPD.Agent.ImageContent))]
[JsonSerializable(typeof(HPD.Agent.AudioContent))]
[JsonSerializable(typeof(HPD.Agent.VideoContent))]
[JsonSerializable(typeof(HPD.Agent.DocumentContent))]

// Common .NET types that may appear in tool results
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]

public partial class SessionJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Combined options that merge SessionJsonContext with M.E.AI's AIJsonUtilities.DefaultOptions.
    /// Use this for session serialization to support all M.E.AI types including primitives in tool results.
    /// </summary>
    public static JsonSerializerOptions CombinedOptions { get; } = CreateCombinedOptions();

    private static JsonSerializerOptions CreateCombinedOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // Start with our SessionJsonContext for HPD-specific types
        options.TypeInfoResolverChain.Add(new SessionJsonContext());

        // Add M.E.AI's type resolvers for all primitives and M.E.AI types
        foreach (var resolver in AIJsonUtilities.DefaultOptions.TypeInfoResolverChain)
        {
            if (resolver != null)
            {
                options.TypeInfoResolverChain.Add(resolver);
            }
        }

        // Register HPD-Agent custom content types as AIContent derived types
        // These extend DataContent but need explicit registration for polymorphic serialization
        options.AddAIContentType<ImageContent>("hpd:image");
        options.AddAIContentType<AudioContent>("hpd:audio");
        options.AddAIContentType<VideoContent>("hpd:video");
        options.AddAIContentType<DocumentContent>("hpd:document");

        options.MakeReadOnly();
        return options;
    }
}
