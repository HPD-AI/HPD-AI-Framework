using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>
/// Reference to a middleware in config. Supports JSON shorthand (string) or full object.
/// </summary>
/// <remarks>
/// <para>
/// MiddlewareReference enables flexible JSON configuration syntax:
/// </para>
/// <para>
/// <b>Simple syntax (just name):</b>
/// <code>
/// { "middlewares": ["LoggingMiddleware", "RetryMiddleware"] }
/// </code>
/// </para>
/// <para>
/// <b>Rich syntax (with configuration):</b>
/// <code>
/// {
///   "middlewares": [
///     "LoggingMiddleware",
///     { "name": "RateLimitMiddleware", "config": { "requestsPerMinute": 60 } }
///   ]
/// }
/// </code>
/// </para>
/// </remarks>
[JsonConverter(typeof(MiddlewareReferenceConverter))]
public class MiddlewareReference
{
    /// <summary>
    /// Name of the middleware (class name or [Middleware(Name = "...")]).
    /// This is the lookup key in the source-generated middleware registry.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Middleware-specific configuration.
    /// Deserialized using the middleware's registered config type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source generator detects constructors with a single config parameter
    /// and stores the config type. At resolution time, this JsonElement is
    /// deserialized to that type and passed to the constructor.
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code>
    /// { "name": "RateLimitMiddleware", "config": { "requestsPerMinute": 60, "burstLimit": 10 } }
    /// </code>
    /// </para>
    /// </remarks>
    public JsonElement? Config { get; set; }

    /// <summary>
    /// Implicit conversion from string for simple syntax support.
    /// </summary>
    /// <param name="name">The middleware name.</param>
    public static implicit operator MiddlewareReference(string name) => new() { Name = name };

    /// <summary>
    /// Returns the middleware name for debugging.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>
/// JSON converter that supports both string and object syntax for MiddlewareReference.
/// </summary>
/// <remarks>
/// <para>
/// Enables polymorphic JSON deserialization:
/// - String value: "LoggingMiddleware" -> MiddlewareReference { Name = "LoggingMiddleware" }
/// - Object value: { "name": "...", "config": {...} } -> Full MiddlewareReference
/// </para>
/// </remarks>
public class MiddlewareReferenceConverter : JsonConverter<MiddlewareReference>
{
    /// <summary>
    /// Reads a MiddlewareReference from JSON.
    /// </summary>
    public override MiddlewareReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // Simple syntax: "LoggingMiddleware"
            var name = reader.GetString();
            return new MiddlewareReference { Name = name ?? "" };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Rich syntax: { "name": "...", "config": {...} }
            var reference = new MiddlewareReference();

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
                    case "config":
                        reference.Config = JsonElement.ParseValue(ref reader);
                        break;
                    default:
                        // Skip unknown properties
                        reader.Skip();
                        break;
                }
            }

            return reference;
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} when reading MiddlewareReference");
    }

    /// <summary>
    /// Writes a MiddlewareReference to JSON.
    /// Uses simple syntax when only name is set, object syntax otherwise.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, MiddlewareReference value, JsonSerializerOptions options)
    {
        // Use simple syntax if only name is set
        if (!value.Config.HasValue)
        {
            writer.WriteStringValue(value.Name);
            return;
        }

        // Use object syntax for rich configuration
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);

        if (value.Config.HasValue)
        {
            writer.WritePropertyName("config");
            value.Config.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
