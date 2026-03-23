using Microsoft.Extensions.AI;
using HPD.OpenApi.Core;

namespace HPD.Agent.OpenApi;

/// <summary>
/// Agent-specific OpenAPI configuration. Extends <see cref="OpenApiCoreConfig"/> with
/// HPD-Agent concerns (collapsing, permissions, schema transforms, response optimization).
/// </summary>
public class OpenApiConfig : OpenApiCoreConfig
{
    /// <summary>
    /// Whether all generated functions require user permission before execution.
    /// Default: false (consistent with [AIFunction], [Skill], [MCPServer]).
    /// Use [RequiresPermission] on the [OpenApi] method to opt in.
    /// </summary>
    public bool RequiresPermission { get; set; } = false;

    /// <summary>
    /// When true, generated functions are grouped behind their own container nested
    /// inside the parent toolkit container (two-level expand required).
    /// When false (default), generated functions appear directly under the parent
    /// toolkit when it is expanded (single expand required).
    /// Only relevant when used inside a toolkit class with [Collapse].
    /// </summary>
    public bool CollapseWithinToolkit { get; set; } = false;

    /// <summary>
    /// Optional schema transform options applied to generated parameter schemas
    /// after OpenAPI schema conversion, using AIJsonUtilities.TransformSchema().
    /// Useful for DisallowAdditionalProperties, ConvertBooleanSchemas, etc.
    ///
    /// If null or AIJsonSchemaTransformOptions.Default, no post-processing is applied.
    /// Note: AIJsonUtilities.TransformSchema throws if passed the Default instance —
    /// guard against this before calling.
    /// </summary>
    public AIJsonSchemaTransformOptions? SchemaTransformOptions { get; set; }

    /// <summary>
    /// Response optimization settings for reducing token consumption when
    /// API responses are returned to the LLM. Agent-specific concern.
    /// When set, hints are encoded as function metadata and read by
    /// ResponseOptimizationMiddleware during AfterFunctionAsync.
    /// </summary>
    public ResponseOptimizationConfig? ResponseOptimization { get; set; }
}

/// <summary>
/// Configuration for how API responses are optimized before the LLM sees them.
/// Inspired by n8n's "Optimize Tool Response" feature.
/// Hints are encoded as AdditionalProperties on the AIFunction by OpenApiFunctionFactory
/// and consumed by ResponseOptimizationMiddleware at runtime.
/// </summary>
public class ResponseOptimizationConfig
{
    /// <summary>
    /// Extract data from a nested field before applying other optimizations.
    /// Many APIs wrap actual data in an envelope:
    ///   Stripe: { "data": [...] }  GitHub: { "items": [...] }
    /// Supports dot notation for nested paths (e.g., "result.data").
    /// </summary>
    public string? DataField { get; set; }

    /// <summary>
    /// Whitelist of field names to include in the response.
    /// Mutually exclusive with FieldsToExclude.
    /// </summary>
    public IList<string>? FieldsToInclude { get; set; }

    /// <summary>
    /// Blacklist of field names to exclude from the response.
    /// Mutually exclusive with FieldsToInclude.
    /// </summary>
    public IList<string>? FieldsToExclude { get; set; }

    /// <summary>
    /// Maximum character length for the serialized response.
    /// Default: 0 (no per-config truncation; middleware DefaultMaxLength applies).
    /// </summary>
    public int MaxLength { get; set; } = 0;
}
