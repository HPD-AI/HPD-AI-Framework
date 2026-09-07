using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Goals;

/// <summary>Generated metadata for the Goal domain and action contracts.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GoalAction))]
[JsonSerializable(typeof(GoalPersistentState))]
[JsonSerializable(typeof(GoalConfig))]
[JsonSerializable(typeof(GoalRunConfig))]
[JsonSerializable(typeof(GoalPolicyContext))]
[JsonSerializable(typeof(GoalPolicyDecision))]
[JsonSerializable(typeof(GoalUsageProjection))]
[JsonSerializable(typeof(GoalContinuationInputEvent))]
internal partial class GoalJsonContext : JsonSerializerContext;

/// <summary>Serializes Goal access as stable camel-case strings and rejects numeric values.</summary>
public sealed class GoalToolAccessJsonConverter : JsonStringEnumConverter<GoalToolAccess>
{
    /// <summary>Creates the closed string converter.</summary>
    public GoalToolAccessJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false) { }
}

/// <summary>Serializes Goal statuses as stable strings; unknown values fail closed.</summary>
public sealed class GoalStatusJsonConverter : JsonStringEnumConverter<GoalStatus>
{
    /// <summary>Creates the closed string converter.</summary>
    public GoalStatusJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false) { }
}

/// <summary>Serializes accounting quality as stable strings.</summary>
public sealed class GoalUsageQualityJsonConverter : JsonStringEnumConverter<GoalUsageQuality>
{
    /// <summary>Creates the closed string converter.</summary>
    public GoalUsageQualityJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false) { }
}

/// <summary>Serializes only documented blocker categories.</summary>
public sealed class GoalBlockerCategoryJsonConverter : JsonStringEnumConverter<GoalBlockerCategory>
{
    /// <summary>Creates the closed string converter.</summary>
    public GoalBlockerCategoryJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false) { }
}
