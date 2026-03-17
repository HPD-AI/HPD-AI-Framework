using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>
/// Reference to a toolkit in config. Supports JSON shorthand (string) or full object.
/// </summary>
/// <remarks>
/// <para>
/// ToolkitReference enables flexible JSON configuration syntax:
/// </para>
/// <para>
/// <b>Simple syntax (just name):</b>
/// <code>
/// { "toolkits": ["MathToolkit", "SearchToolkit"] }
/// </code>
/// </para>
/// <para>
/// <b>Rich syntax (with configuration):</b>
/// <code>
/// {
///   "toolkits": [
///     "MathToolkit",
///     { "name": "FileToolkit", "functions": ["ReadFile", "WriteFile"] },
///     { "name": "ApiToolkit", "config": { "apiKey": "${API_KEY}" } },
///     { "name": "SearchToolkit", "metadata": { "providerName": "Tavily" } }
///   ]
/// }
/// </code>
/// </para>
/// </remarks>
[JsonConverter(typeof(ToolkitReferenceConverter))]
public class ToolkitReference
{
    /// <summary>
    /// Name of the toolkit (always the class name).
    /// This is the lookup key in the source-generated toolkit registry.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Specific functions to include from this toolkit.
    /// Null = include all functions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this for selective function registration when you want to expose
    /// only a subset of a toolkit's functions to the LLM.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// { "name": "FileToolkit", "functions": ["ReadFile", "ListFiles"] }
    /// </code>
    /// This exposes only ReadFile and ListFiles, hiding WriteFile and DeleteFile.
    /// </para>
    /// </remarks>
    public List<string>? Functions { get; set; }

    /// <summary>
    /// Toolkit-specific configuration (constructor parameters, API keys, etc.).
    /// Deserialized using the toolkit's registered config type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source generator detects constructors with a single config parameter
    /// and stores the config type in ToolkitFactory.ConfigType. At resolution time,
    /// this JsonElement is deserialized to that type and passed to the constructor.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// { "name": "SearchToolkit", "config": { "apiKey": "${SEARCH_API_KEY}", "maxResults": 10 } }
    /// </code>
    /// </para>
    /// </remarks>
    public JsonElement? Config { get; set; }

    /// <summary>
    /// Toolkit metadata for dynamic descriptions and conditional functions.
    /// Deserialized to the toolkit's IToolMetadata type from [AIFunction&lt;TMetadata&gt;].
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
    ///   "name": "SearchToolkit",
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
    /// Per-middleware config overrides for toolkit-scoped middleware with config-constructor factories
    /// . Keys are middleware simple type names (e.g. <c>"DbRateLimitMiddleware"</c>);
    /// values are raw JSON objects passed to the generated config-constructor factory delegate.
    /// Ignored when the toolkit has no matching <c>CollapseMiddlewareConfigFactories</c> entry.
    /// </summary>
    /// <remarks>
    /// <b>Example:</b>
    /// <code>
    /// {
    ///   "name": "DatabaseToolkit",
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
    /// <param name="name">The toolkit name.</param>
    public static implicit operator ToolkitReference(string name) => new() { Name = name };

    /// <summary>
    /// Returns the toolkit name for debugging.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>
/// JSON converter that supports both string and object syntax for ToolkitReference.
/// </summary>
/// <remarks>
/// <para>
/// Enables polymorphic JSON deserialization:
/// - String value: "MathToolkit" -> ToolkitReference { Name = "MathToolkit" }
/// - Object value: { "name": "...", "config": {...} } -> Full ToolkitReference
/// </para>
/// </remarks>
public class ToolkitReferenceConverter : JsonConverter<ToolkitReference>
{
    /// <summary>
    /// Reads a ToolkitReference from JSON.
    /// </summary>
    public override ToolkitReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // Simple syntax: "MathToolkit"
            var name = reader.GetString();
            return new ToolkitReference { Name = name ?? "" };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Rich syntax: { "name": "...", ... }
            var reference = new ToolkitReference();

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

        throw new JsonException($"Unexpected token type {reader.TokenType} when reading ToolkitReference");
    }

    /// <summary>
    /// Writes a ToolkitReference to JSON.
    /// Uses simple syntax when only name is set, object syntax otherwise.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, ToolkitReference value, JsonSerializerOptions options)
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
