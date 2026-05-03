using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>
/// Reference to a harness in config. Supports JSON shorthand (string) or full object.
/// </summary>
/// <remarks>
/// <para>
/// HarnessReference enables flexible JSON configuration syntax:
/// </para>
/// <para>
/// <b>Simple syntax (just name):</b>
/// <code>
/// { "harnesses": ["MathHarness", "SearchHarness"] }
/// </code>
/// </para>
/// <para>
/// <b>Rich syntax (with configuration):</b>
/// <code>
/// {
///   "harnesses": [
///     "MathHarness",
///     { "name": "FileHarness", "functions": ["ReadFile", "WriteFile"] },
///     { "name": "ApiHarness", "config": { "apiKey": "${API_KEY}" } },
///     { "name": "SearchHarness", "metadata": { "providerName": "Tavily" } }
///   ]
/// }
/// </code>
/// </para>
/// </remarks>
[JsonConverter(typeof(HarnessReferenceConverter))]
public class HarnessReference
{
    /// <summary>
    /// Name of the harness (always the class name).
    /// This is the lookup key in the source-generated harness registry.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Specific functions to include from this harness.
    /// Null = include all functions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this for selective function registration when you want to expose
    /// only a subset of a harness's functions to the LLM.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// { "name": "FileHarness", "functions": ["ReadFile", "ListFiles"] }
    /// </code>
    /// This exposes only ReadFile and ListFiles, hiding WriteFile and DeleteFile.
    /// </para>
    /// </remarks>
    public List<string>? Functions { get; set; }

    /// <summary>
    /// Harness-specific configuration (constructor parameters, API keys, etc.).
    /// Deserialized using the harness's registered config type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source generator detects constructors with a single config parameter
    /// and stores the config type in HarnessFactory.ConfigType. At resolution time,
    /// this JsonElement is deserialized to that type and passed to the constructor.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// { "name": "SearchHarness", "config": { "apiKey": "${SEARCH_API_KEY}", "maxResults": 10 } }
    /// </code>
    /// </para>
    /// </remarks>
    public JsonElement? Config { get; set; }

    /// <summary>
    /// Harness metadata for dynamic descriptions and conditional functions.
    /// Deserialized to the harness's IToolMetadata type from [AIFunction&lt;TMetadata&gt;].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Metadata enables runtime configuration of:
    /// - Dynamic descriptions: [AIDescription("Search using {metadata.DefaultProvider}")]
    /// - Conditional functions: [ConditionalFunction("HasTavilyProvider")]
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// {
    ///   "name": "SearchHarness",
    ///   "metadata": {
    ///     "hasTavilyProvider": true,
    ///     "defaultProvider": "tavily"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public JsonElement? Metadata { get; set; }

    /// <summary>
    /// Per-middleware config overrides for harness-scoped middleware with config-constructor factories
    /// . Keys are middleware simple type names (e.g. <c>"DbRateLimitMiddleware"</c>);
    /// values are raw JSON objects passed to the generated config-constructor factory delegate.
    /// Ignored when the harness has no matching <c>CollapseMiddlewareConfigFactories</c> entry.
    /// </summary>
    /// <remarks>
    /// <b>Example:</b>
    /// <code>
    /// {
    ///   "name": "DatabaseHarness",
    ///   "middlewareConfigs": {
    ///     "DbRateLimitMiddleware": { "requestsPerMinute": 20 }
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public Dictionary<string, JsonElement>? MiddlewareConfigs { get; set; }

    /// <summary>
    /// Implicit conversion from string for simple syntax support.
    /// </summary>
    /// <param name="name">The harness name.</param>
    public static implicit operator HarnessReference(string name) => new() { Name = name };

    /// <summary>
    /// Returns the harness name for debugging.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>
/// JSON converter that supports both string and object syntax for HarnessReference.
/// </summary>
/// <remarks>
/// <para>
/// Enables polymorphic JSON deserialization:
/// - String value: "MathHarness" -> HarnessReference { Name = "MathHarness" }
/// - Object value: { "name": "...", "config": {...} } -> Full HarnessReference
/// </para>
/// </remarks>
public class HarnessReferenceConverter : JsonConverter<HarnessReference>
{
    /// <summary>
    /// Reads a HarnessReference from JSON.
    /// </summary>
    public override HarnessReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // Simple syntax: "MathHarness"
            var name = reader.GetString();
            return new HarnessReference { Name = name ?? "" };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Rich syntax: { "name": "...", ... }
            var reference = new HarnessReference();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                var propertyName = reader.GetString()?.ToLowerInvariant();
                reader.Read();

                switch (propertyName)
                {
                    case "name":
                        reference.Name = reader.GetString() ?? "";
                        break;
                    case "functions":
                        reference.Functions = JsonSerializer.Deserialize<List<string>>(ref reader, options);
                        break;
                    case "config":
                        reference.Config = JsonElement.ParseValue(ref reader);
                        break;
                    case "metadata":
                        reference.Metadata = JsonElement.ParseValue(ref reader);
                        break;
                    case "middlewareconfigs":
                        reference.MiddlewareConfigs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ref reader, options);
                        break;
                    default:
                        // Skip unknown properties
                        reader.Skip();
                        break;
                }
            }

            return reference;
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} when reading HarnessReference");
    }

    /// <summary>
    /// Writes a HarnessReference to JSON.
    /// Uses simple syntax when only name is set, object syntax otherwise.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, HarnessReference value, JsonSerializerOptions options)
    {
        // Use simple syntax if only name is set
        if (value.Functions == null && !value.Config.HasValue && !value.Metadata.HasValue && value.MiddlewareConfigs == null)
        {
            writer.WriteStringValue(value.Name);
            return;
        }

        // Use object syntax for rich configuration
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);

        if (value.Functions != null)
        {
            writer.WritePropertyName("functions");
            JsonSerializer.Serialize(writer, value.Functions, options);
        }

        if (value.Config.HasValue)
        {
            writer.WritePropertyName("config");
            value.Config.Value.WriteTo(writer);
        }

        if (value.Metadata.HasValue)
        {
            writer.WritePropertyName("metadata");
            value.Metadata.Value.WriteTo(writer);
        }

        if (value.MiddlewareConfigs != null)
        {
            writer.WritePropertyName("middlewareConfigs");
            JsonSerializer.Serialize(writer, value.MiddlewareConfigs, options);
        }

        writer.WriteEndObject();
    }
}
