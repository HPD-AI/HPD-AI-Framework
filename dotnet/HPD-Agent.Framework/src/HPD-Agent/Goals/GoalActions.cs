using System.ComponentModel;
using System.Text.Json.Serialization;

namespace HPD.Agent.Goals;

/// <summary>The closed model-facing action contract for the single goal function.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "action", IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(CreateGoalAction), "create")]
[JsonDerivedType(typeof(GetGoalAction), "get")]
[JsonDerivedType(typeof(ProposeGoalCompletionAction), "proposeCompletion")]
[JsonDerivedType(typeof(ReportGoalBlockerAction), "reportBlocker")]
[JsonDerivedType(typeof(PauseGoalAction), "pause")]
[JsonDerivedType(typeof(ResumeGoalAction), "resume")]
[JsonDerivedType(typeof(EditGoalAction), "edit")]
[JsonDerivedType(typeof(ClearGoalAction), "clear")]
public abstract record GoalAction;

/// <summary>Creates a Goal for explicitly requested persistence.</summary>
[AIFunctionAction("create")]
[Description("Create only when the user explicitly requests persistent work. Reject an unfinished existing Goal; never broaden task authority.")]
public sealed record CreateGoalAction(
    [property: Description("The complete concrete outcome, constraints, and verification criteria.")]
    string Objective) : GoalAction;

/// <summary>Inspects the current Goal without mutation.</summary>
[AIFunctionAction("get")]
[Description("Get the current Goal, status, objective, accounting, evidence, and continuation state without mutation.")]
public sealed record GetGoalAction : GoalAction;

/// <summary>Submits completion evidence to policy.</summary>
[AIFunctionAction("proposeCompletion")]
[Description("Propose completion only when the full objective and verification are satisfied. Policy validates the proposal; this action does not complete the Goal directly.")]
public sealed record ProposeGoalCompletionAction(
    [property: Description("Why the full objective is achieved and no required work remains.")]
    string Summary,
    [property: Description("Optional concrete tests, artifacts, evaluations, deployment facts, or approval evidence.")]
    IReadOnlyList<GoalEvidenceItem>? Evidence = null,
    [property: Description("List any remaining or unverified required work honestly. A nonempty list prevents completion.")]
    IReadOnlyList<string>? RemainingWork = null) : GoalAction;

/// <summary>Submits structured impasse evidence to policy.</summary>
[AIFunctionAction("reportBlocker")]
[Description("Report an impasse after exhausting meaningful alternatives. Policy computes recurrence and decides whether to block; transient errors or difficulty are not blockers.")]
public sealed record ReportGoalBlockerAction(
    [property: Description("The stable category identifying the blocking condition.")]
    GoalBlockerCategory Category,
    [property: Description("What concretely prevents meaningful progress.")]
    string Description,
    [property: Description("The input, authority, artifact, or external change required to resume.")]
    string RequiredChange,
    [property: Description("Optional observations supporting the blocker report.")]
    IReadOnlyList<string>? Evidence = null) : GoalAction;

/// <summary>Pauses on explicit user request.</summary>
[AIFunctionAction("pause")]
[Description("Pause only when the user explicitly requests it. Preserve state and prohibit automatic continuation.")]
public sealed record PauseGoalAction : GoalAction;

/// <summary>Resumes on explicit user request.</summary>
[AIFunctionAction("resume")]
[Description("Resume an explicitly resumable Goal only when the user requests continued work. Begin a fresh blocker audit.")]
public sealed record ResumeGoalAction : GoalAction;

/// <summary>Replaces the objective on explicit user request.</summary>
[AIFunctionAction("edit")]
[Description("Replace the objective only when the user explicitly requests the change. Preserve Goal identity and invalidate stale continuation.")]
public sealed record EditGoalAction(
    [property: Description("The complete replacement objective, including constraints and verification criteria; replaces rather than appends.")]
    string Objective) : GoalAction;

/// <summary>Clears on explicit user request.</summary>
[AIFunctionAction("clear")]
[Description("Remove the Goal only when the user explicitly asks to clear or abandon it. Clearing is not completion.")]
public sealed record ClearGoalAction : GoalAction;

/// <summary>Documented impasse categories compared by blocker policy.</summary>
[JsonConverter(typeof(GoalBlockerCategoryJsonConverter))]
public enum GoalBlockerCategory
{
    /// <summary>A required user decision has no authorized default.</summary>
    [Description("A required user decision has no authorized default.")]
    UserDecision,
    /// <summary>Required external authority is unavailable.</summary>
    [Description("Required external authority is unavailable.")]
    Authority,
    /// <summary>A required credential or artifact is unavailable.</summary>
    [Description("A required credential or artifact is unavailable; do not include secret values.")]
    MissingArtifact,
    /// <summary>Requirements materially contradict one another.</summary>
    [Description("Requirements materially contradict one another.")]
    ConflictingRequirements,
    /// <summary>A required external system persistently prevents all scoped alternatives.</summary>
    [Description("A required external system persistently prevents all scoped alternatives; excludes transient failures.")]
    ExternalSystem,
    /// <summary>The environment makes every in-scope approach impossible.</summary>
    [Description("The environment makes every in-scope approach impossible.")]
    Environment
}

/// <summary>Safe evidence supporting a Goal completion proposal.</summary>
public sealed record GoalEvidenceItem(
    [property: Description("Stable evidence kind such as test, build, file, evaluation, deployment, or approval.")]
    string Kind,
    [property: Description("What the evidence proves about the objective.")]
    string Description,
    [property: Description("Optional safe file path, execution identifier, or check name; never a secret.")]
    string? Reference = null);
