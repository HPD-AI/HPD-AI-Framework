using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>
/// Reference to a toolharness in config. Supports JSON shorthand (string) or full object.
/// </summary>
/// <remarks>
/// <para>
/// ToolHarnessReference enables flexible JSON configuration syntax:
/// </para>
/// <para>
/// <b>Simple syntax (just name):</b>
/// <code>
/// { "toolharnesses": ["MathToolHarness", "SearchToolHarness"] }
/// </code>
/// </para>
/// <para>
/// <b>Rich syntax (with configuration):</b>
/// <code>
/// {
///   "toolharnesses": [
///     "MathToolHarness",
///     { "name": "FileToolHarness", "functions": ["ReadFile", "WriteFile"] },
///     { "name": "ApiToolHarness", "config": { "apiKey": "${API_KEY}" } },
///     { "name": "SearchToolHarness", "metadata": { "providerName": "Tavily" } }
///   ]
/// }
/// </code>
/// </para>
/// </remarks>
[JsonConverter(typeof(ToolHarnessReferenceConverter))]
public class ToolHarnessReference
{
    /// <summary>
    /// Name of the toolharness (always the class name).
    /// This is the lookup key in the source-generated toolharness registry.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Specific functions to include from this toolharness.
    /// Null = include all functions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this for selective function registration when you want to expose
    /// only a subset of a toolharness's functions to the LLM.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// { "name": "FileToolHarness", "functions": ["ReadFile", "ListFiles"] }
    /// </code>
    /// This exposes only ReadFile and ListFiles, hiding WriteFile and DeleteFile.
    /// </para>
    /// </remarks>
    public List<string>? Functions { get; set; }

    /// <summary>
    /// ToolHarness-specific configuration (constructor parameters, API keys, etc.).
    /// Deserialized using the toolharness's registered config type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source generator detects constructors with a single config parameter
    /// and stores the config type in ToolHarnessFactory.ConfigType. At resolution time,
    /// this JsonElement is deserialized to that type and passed to the constructor.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// { "name": "SearchToolHarness", "config": { "apiKey": "${SEARCH_API_KEY}", "maxResults": 10 } }
    /// </code>
    /// </para>
    /// </remarks>
    public JsonElement? Config { get; set; }

    /// <summary>
    /// ToolHarness metadata for dynamic descriptions and conditional functions.
    /// Deserialized to the toolharness's IToolMetadata type from [AIFunction&lt;TMetadata&gt;].
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
    ///   "name": "SearchToolHarness",
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
    /// Per-middleware config overrides for toolharness-scoped middleware with config-constructor factories
    /// . Keys are middleware simple type names (e.g. <c>"DbRateLimitMiddleware"</c>);
    /// values are raw JSON objects passed to the generated config-constructor factory delegate.
    /// Ignored when the ToolHarness has no matching generated middleware configuration descriptor.
    /// </summary>
    /// <remarks>
    /// <b>Example:</b>
    /// <code>
    /// {
    ///   "name": "DatabaseToolHarness",
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
    /// <param name="name">The toolharness name.</param>
    public static implicit operator ToolHarnessReference(string name) => new() { Name = name };

    /// <summary>
    /// Returns the toolharness name for debugging.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>
/// JSON converter that supports both string and object syntax for ToolHarnessReference.
/// </summary>
/// <remarks>
/// <para>
/// Enables polymorphic JSON deserialization:
/// - String value: "MathToolHarness" -> ToolHarnessReference { Name = "MathToolHarness" }
/// - Object value: { "name": "...", "config": {...} } -> Full ToolHarnessReference
/// </para>
/// </remarks>
public class ToolHarnessReferenceConverter : JsonConverter<ToolHarnessReference>
{
    /// <summary>
    /// Reads a ToolHarnessReference from JSON.
    /// </summary>
    public override ToolHarnessReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // Simple syntax: "MathToolHarness"
            var name = reader.GetString();
            return new ToolHarnessReference { Name = name ?? "" };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Rich syntax: { "name": "...", ... }
            var reference = new ToolHarnessReference();

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
                        reference.Functions = ReadStringList(ref reader);
                        break;
                    case "config":
                        reference.Config = JsonElement.ParseValue(ref reader);
                        break;
                    case "metadata":
                        reference.Metadata = JsonElement.ParseValue(ref reader);
                        break;
                    case "middlewareconfigs":
                        reference.MiddlewareConfigs = ReadJsonElementDictionary(ref reader);
                        break;
                    default:
                        // Skip unknown properties
                        reader.Skip();
                        break;
                }
            }

            return reference;
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} when reading ToolHarnessReference");
    }

    /// <summary>
    /// Writes a ToolHarnessReference to JSON.
    /// Uses simple syntax when only name is set, object syntax otherwise.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, ToolHarnessReference value, JsonSerializerOptions options)
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
            WriteStringList(writer, value.Functions);
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
            WriteJsonElementDictionary(writer, value.MiddlewareConfigs);
        }

        writer.WriteEndObject();
    }

    private static List<string> ReadStringList(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected an array of function names.");

        var values = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return values;

            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("Function names must be strings.");

            values.Add(reader.GetString() ?? string.Empty);
        }

        throw new JsonException("Unexpected end of JSON while reading function names.");
    }

    private static Dictionary<string, JsonElement> ReadJsonElementDictionary(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected an object for middleware configs.");

        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return values;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a middleware config property name.");

            var name = reader.GetString() ?? string.Empty;
            reader.Read();
            values[name] = JsonElement.ParseValue(ref reader);
        }

        throw new JsonException("Unexpected end of JSON while reading middleware configs.");
    }

    private static void WriteStringList(Utf8JsonWriter writer, IEnumerable<string> values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void WriteJsonElementDictionary(Utf8JsonWriter writer, IReadOnlyDictionary<string, JsonElement> values)
    {
        writer.WriteStartObject();
        foreach (var (key, value) in values)
        {
            writer.WritePropertyName(key);
            value.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
}
