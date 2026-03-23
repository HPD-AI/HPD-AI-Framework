// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.StructuredOutput;

/// <summary>
/// Configuration for structured output mode.
/// </summary>
public class StructuredOutputOptions
{
    /// <summary>
    /// Output mode: "native" (default) or "tool".
    /// - native: Use provider's ResponseFormat with JSON schema (supports streaming partials)
    /// - tool: Use tool-based output (auto-generated output tool(s), no streaming partials)
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Native mode</b> is recommended for most use cases. It uses the provider's
    /// built-in JSON schema support via ResponseFormat, enabling real-time streaming
    /// of partial results as tokens arrive.
    /// </para>
    /// <para>
    /// Native mode supports both single type and multiple types (union):
    /// <list type="bullet">
    /// <item>Single type: Standard JSON schema for type T</item>
    /// <item>Multiple types: Set <see cref="UnionTypes"/> to generate an anyOf schema</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Tool mode</b> is for scenarios where you need to mix structured output with
    /// regular tools. In this mode, output tool(s) are auto-generated and added to
    /// the agent's tools. The LLM is forced to call one of these tools (provider-enforced
    /// via tool_choice: required).
    /// </para>
    /// <para>
    /// Tool mode also supports both single and multiple types:
    /// <list type="bullet">
    /// <item>Single type: Creates one tool (return_TypeName)</item>
    /// <item>Multiple types: Set <see cref="UnionTypes"/> to create one tool per type</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Comparison:</b>
    /// <list type="table">
    /// <item><term>Native</term><description>Streaming partials ✓, Cannot mix with other tools</description></item>
    /// <item><term>Tool</term><description>No streaming, Can mix with other tools</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "native";

    /// <summary>
    /// JSON schema for the output type.
    /// For C#: Inferred from generic type parameter T.
    /// For FFI: Provided directly in JSON.
    /// </summary>
    [JsonPropertyName("schema")]
    public JsonElement? Schema { get; set; }

    /// <summary>
    /// Schema/type name for observability and tool naming.
    /// Defaults to typeof(T).Name if not specified.
    /// </summary>
    [JsonPropertyName("schemaName")]
    public string? SchemaName { get; set; }

    /// <summary>
    /// Schema description for the LLM.
    /// Used in ResponseFormat and tool descriptions.
    /// </summary>
    [JsonPropertyName("schemaDescription")]
    public string? SchemaDescription { get; set; }

    /// <summary>
    /// Tool name override (tool mode only).
    /// Default: "return_{SchemaName}" or "return_{TypeName}".
    /// </summary>
    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    /// <summary>
    /// Enable streaming partial updates. Default: true.
    /// When false, only emits final complete result.
    /// </summary>
    /// <remarks>
    /// <b>NOTE:</b> Streaming partials only work in "native" mode.
    /// In "tool" mode, partials are never emitted because M.E.AI
    /// accumulates tool arguments internally before exposing them.
    /// For real-time partial updates, use native mode.
    /// </remarks>
    [JsonPropertyName("streamPartials")]
    public bool StreamPartials { get; set; } = true;

    /// <summary>
    /// Debounce interval for partial updates (ms). Default: 100.
    /// Reduces parse overhead for high-frequency token streams.
    /// </summary>
    [JsonPropertyName("partialDebounceMs")]
    public int PartialDebounceMs { get; set; } = 100;

    /// <summary>
    /// JSON serializer options for deserialization.
    /// Required for AOT/NativeAOT: Pass a JsonSerializerContext with your output type registered.
    /// Default: AIJsonUtilities.DefaultOptions (works with reflection, NOT AOT-safe for custom types).
    /// </summary>
    /// <example>
    /// // AOT-safe usage:
    /// [JsonSerializable(typeof(WeatherReport))]
    /// public partial class MyJsonContext : JsonSerializerContext;
    ///
    /// var options = new StructuredOutputOptions
    /// {
    ///     SerializerOptions = MyJsonContext.Default.Options
    /// };
    /// </example>
    [JsonIgnore] // Not FFI-serializable (JsonSerializerOptions is complex)
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Multiple output types for discriminated union behavior.
    /// Works with both native mode (anyOf schema) and tool mode (multiple return tools).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enables discriminated union-like behavior where the LLM can return one of
    /// several possible types. The behavior differs based on mode:
    /// </para>
    /// <para>
    /// <b>Native mode with UnionTypes:</b>
    /// <list type="bullet">
    /// <item>Generates an anyOf JSON schema combining all types</item>
    /// <item>Provider enforces schema via response_format</item>
    /// <item>Streaming partials supported</item>
    /// <item>Type detection via deserialization at parse time</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Tool mode with UnionTypes:</b>
    /// <list type="bullet">
    /// <item>Creates one output tool per type (e.g., return_Success, return_Error)</item>
    /// <item>LLM forced to call exactly one tool via tool_choice: required</item>
    /// <item>Type known at call time (from tool name)</item>
    /// <item>No streaming partials</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Usage (works with either mode):</b>
    /// </para>
    /// <code>
    /// public abstract record ApiResponse;
    /// public sealed record SuccessResponse(string Data) : ApiResponse;
    /// public sealed record ErrorResponse(string Code, string Message) : ApiResponse;
    ///
    /// // Native mode (with streaming):
    /// var options = new AgentRunConfig
    /// {
    ///     StructuredOutput = new StructuredOutputOptions
    ///     {
    ///         Mode = "native",  // or "tool" for tool-based routing
    ///         UnionTypes = new[] { typeof(SuccessResponse), typeof(ErrorResponse) }
    ///     }
    /// };
    ///
    /// await foreach (var evt in agent.RunStructuredAsync&lt;ApiResponse&gt;(messages, options: options))
    /// {
    ///     if (evt is StructuredResultEvent&lt;ApiResponse&gt; result)
    ///     {
    ///         switch (result.Value)
    ///         {
    ///             case SuccessResponse success: HandleSuccess(success); break;
    ///             case ErrorResponse error: HandleError(error); break;
    ///         }
    ///     }
    /// }
    /// </code>
    /// </remarks>
    [JsonIgnore] // Type[] not FFI-serializable
    public Type[]? UnionTypes { get; set; }
}
