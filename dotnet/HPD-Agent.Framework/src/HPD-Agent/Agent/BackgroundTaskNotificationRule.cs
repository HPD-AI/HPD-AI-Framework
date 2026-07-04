using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>
/// Describes how a runtime-owned background task wants final-state facts handled.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BackgroundTaskNotificationRule.NoneRule), "none")]
[JsonDerivedType(typeof(BackgroundTaskNotificationRule.OnFinalStateRule), "on_final_state")]
[JsonDerivedType(typeof(BackgroundTaskNotificationRule.StrategyRule), "strategy")]
public abstract record BackgroundTaskNotificationRule
{
    /// <summary>
    /// A rule that never queues a model notification.
    /// </summary>
    public static readonly BackgroundTaskNotificationRule None = new NoneRule();

    /// <summary>
    /// Suppresses all final-state notifications for the task.
    /// </summary>
    public sealed record NoneRule : BackgroundTaskNotificationRule;

    /// <summary>
    /// Queues notifications for selected final lifecycle states.
    /// </summary>
    /// <param name="Completed">Whether completed task events should queue a notification.</param>
    /// <param name="Faulted">Whether faulted task events should queue a notification.</param>
    /// <param name="Cancelled">Whether cancelled task events should queue a notification.</param>
    public sealed record OnFinalStateRule(
        bool Completed = false,
        bool Faulted = false,
        bool Cancelled = false) : BackgroundTaskNotificationRule;

    /// <summary>
    /// Delegates notification selection and formatting to a named strategy.
    /// </summary>
    /// <param name="Name">The strategy name to resolve at dispatch time.</param>
    /// <param name="Parameters">Optional source-neutral parameters for the strategy.</param>
    /// <param name="Fallback">Optional rule to evaluate if the named strategy is unavailable.</param>
    public sealed record StrategyRule(
        string Name,
        IReadOnlyDictionary<string, string>? Parameters = null,
        BackgroundTaskNotificationRule? Fallback = null) : BackgroundTaskNotificationRule;
}

/// <summary>
/// Result of evaluating a background task final-state notification rule.
/// </summary>
public abstract record BackgroundTaskNotificationDecision
{
    /// <summary>
    /// Suppresses the model notification with an observable reason.
    /// </summary>
    /// <param name="Reason">Machine-readable reason for suppression.</param>
    public sealed record Suppress(string Reason) : BackgroundTaskNotificationDecision;

    /// <summary>
    /// Queues a model notification with selected summary and metadata.
    /// </summary>
    /// <param name="Summary">Model-visible summary of the final-state fact.</param>
    /// <param name="Metadata">Optional model-visible metadata.</param>
    /// <param name="BatchKey">Optional semantic batch key for grouping related notifications.</param>
    public sealed record Queue(
        string Summary,
        IReadOnlyDictionary<string, string>? Metadata = null,
        string? BatchKey = null) : BackgroundTaskNotificationDecision;
}

/// <summary>
/// Runtime-neutral context passed to background task notification strategies.
/// </summary>
public sealed record BackgroundTaskNotificationContext
{
    /// <summary>
    /// Identifier of the agent runtime that will receive the notification input.
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Session scope for the notification.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Thread scope for the notification.
    /// </summary>
    public required string ThreadId { get; init; }

    /// <summary>
    /// Normalized final-state status, such as completed, cancelled, or faulted.
    /// </summary>
    public required string FinalStateStatus { get; init; }

    /// <summary>
    /// Latest run configuration known to the dispatcher.
    /// </summary>
    public AgentRunConfig? RunConfig { get; init; }
}

/// <summary>
/// Source-specific evaluator for background task final-state notifications.
/// </summary>
public interface IBackgroundTaskNotificationStrategy
{
    /// <summary>
    /// Decides whether a background task final-state event should wake the model.
    /// </summary>
    ValueTask<BackgroundTaskNotificationDecision> DecideAsync(
        BackgroundTaskEvent evt,
        BackgroundTaskNotificationContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves named background task notification strategies.
/// </summary>
public interface IBackgroundTaskNotificationStrategyRegistry
{
    /// <summary>
    /// Attempts to resolve a named notification strategy.
    /// </summary>
    bool TryGetStrategy(
        string name,
        [NotNullWhen(true)] out IBackgroundTaskNotificationStrategy? strategy);
}
