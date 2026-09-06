using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Planning;

/// <summary>Closed action contract for the single model-facing plan tool.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "action", IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(CreatePlanAction), "create")]
[JsonDerivedType(typeof(UpdatePlanStepAction), "updateStep")]
[JsonDerivedType(typeof(AddPlanStepAction), "addStep")]
[JsonDerivedType(typeof(AddPlanNoteAction), "addNote")]
[JsonDerivedType(typeof(CompletePlanAction), "complete")]
public abstract record PlanAction;

[AIFunctionAction("create")]
[Description("Create a plan for a multi-step task. An unfinished plan must be completed before creating another.")]
public sealed record CreatePlanAction(
    [property: Description("The outcome this plan tracks.")] string Goal,
    [property: Description("Initial ordered step descriptions; at least one step is required.")] string[] Steps) : PlanAction;

[AIFunctionAction("updateStep")]
[Description("Update an existing step's status and optional progress or blocker notes.")]
public sealed record UpdatePlanStepAction(
    [property: Description("The existing step ID.")] string StepId,
    [property: Description("The step's new status: Pending, InProgress, Completed, or Blocked.")]
    [property: JsonConverter(typeof(PlanActionStatusJsonConverter))] PlanStepStatus Status,
    [property: Description("Optional findings, progress, or blockers for this step.")] string? Notes = null) : PlanAction;

[AIFunctionAction("addStep")]
[Description("Add newly discovered work to the active plan, optionally after an existing step.")]
public sealed record AddPlanStepAction(
    [property: Description("The new step description.")] string Description,
    [property: Description("Optional existing step ID to insert after; otherwise append.")] string? AfterStepId = null) : PlanAction;

[AIFunctionAction("addNote")]
[Description("Record an important discovery or piece of context in the active plan.")]
public sealed record AddPlanNoteAction(
    [property: Description("The context or discovery to retain.")] string Note) : PlanAction;

[AIFunctionAction("complete")]
[Description("Complete the plan only after every step is completed. This does not complete a persistent Goal.")]
public sealed record CompletePlanAction : PlanAction;

/// <summary>String status in tool arguments without changing existing durable plan-state serialization.</summary>
public sealed class PlanActionStatusJsonConverter() : JsonStringEnumConverter<PlanStepStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlanAction))]
internal partial class PlanActionJsonContext : JsonSerializerContext;
