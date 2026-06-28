using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Moonshot;

/// <summary>
/// Serializable Moonshot/Kimi-specific chat request options.
/// </summary>
/// <remarks>
/// Generic runtime settings such as model, temperature, top-p, max output tokens,
/// seed, stop sequences, tools, response format, and reasoning belong on
/// <see cref="ChatRunConfig"/> or <see cref="ChatOptions"/>.
/// </remarks>
public sealed class MoonshotChatRequestOptions
{
    /// <summary>
    /// Preserves reasoning content from historical assistant messages.
    /// </summary>
    public MoonshotThinkingKeep? ThinkingKeep { get; set; }

    /// <summary>
    /// Converts the typed options to additional properties consumed by the Moonshot configured client.
    /// </summary>
    public Dictionary<string, object> ToAdditionalProperties()
    {
        var properties = new Dictionary<string, object>();

        if (ToWireString(ThinkingKeep) is { } thinkingKeep)
            properties[MoonshotChatRequestOptionKeys.ThinkingKeep] = thinkingKeep;

        return properties;
    }

    /// <summary>
    /// Applies these options to a serializable HPD chat run configuration.
    /// </summary>
    public void ApplyTo(ChatRunConfig chat)
    {
        ArgumentNullException.ThrowIfNull(chat);

        var properties = ToAdditionalProperties();
        if (properties.Count == 0)
            return;

        chat.AdditionalProperties ??= new Dictionary<string, object>();
        foreach (var property in properties)
        {
            chat.AdditionalProperties[property.Key] = property.Value;
        }
    }

    /// <summary>
    /// Applies these options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public void ApplyTo(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var properties = ToAdditionalProperties();
        if (properties.Count == 0)
            return;

        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        foreach (var property in properties)
        {
            options.AdditionalProperties[property.Key] = property.Value;
        }
    }

    private static string? ToWireString(MoonshotThinkingKeep? value)
        => value switch
        {
            MoonshotThinkingKeep.All => "all",
            _ => null
        };
}

/// <summary>
/// Moonshot/Kimi reasoning history retention mode.
/// </summary>
[JsonConverter(typeof(MoonshotThinkingKeepJsonConverter))]
public enum MoonshotThinkingKeep
{
    All
}

internal sealed class MoonshotThinkingKeepJsonConverter : JsonConverter<MoonshotThinkingKeep>
{
    public override MoonshotThinkingKeep Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "all" => MoonshotThinkingKeep.All,
            var value => throw new JsonException($"Unknown Moonshot thinking keep mode '{value}'.")
        };

    public override void Write(Utf8JsonWriter writer, MoonshotThinkingKeep value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            MoonshotThinkingKeep.All => "all",
            _ => throw new JsonException($"Unknown Moonshot thinking keep mode '{value}'.")
        });
    }
}

/// <summary>
/// Extension helpers for applying Moonshot/Kimi-specific chat request options.
/// </summary>
public static class MoonshotChatRequestOptionExtensions
{
    /// <summary>
    /// Applies Moonshot/Kimi-specific runtime options to a serializable HPD chat run configuration.
    /// </summary>
    public static ChatRunConfig UseMoonshotChatRequestOptions(
        this ChatRunConfig chat,
        MoonshotChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }

    /// <summary>
    /// Applies Moonshot/Kimi-specific runtime options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public static ChatOptions UseMoonshotChatRequestOptions(
        this ChatOptions chat,
        MoonshotChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }
}

internal static class MoonshotChatRequestOptionKeys
{
    public const string ThinkingKeep = "thinking_keep";
}
