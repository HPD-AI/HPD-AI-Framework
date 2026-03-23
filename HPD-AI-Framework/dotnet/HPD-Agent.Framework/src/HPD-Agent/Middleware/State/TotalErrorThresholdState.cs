using System.Collections.Immutable;

namespace HPD.Agent;

/// <summary>
/// State for total error threshold tracking. Immutable record with static abstract key.
/// Tracks the total number of errors encountered across all iterations (regardless of type).
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b></para>
/// <para>
/// This state is immutable and flows through the context.
/// It is NOT stored in middleware instance fields, preserving thread safety
/// for concurrent RunAsync() calls.
/// </para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// // Read state
/// var thresholdState = context.State.MiddlewareState.TotalErrorThreshold ?? new();
/// var totalErrors = thresholdState.TotalErrorCount;
///
/// // Update state
/// context.UpdateState(s => s with
/// {
///     MiddlewareState = s.MiddlewareState.WithTotalErrorThreshold(thresholdState with
///     {
///         TotalErrorCount = thresholdState.TotalErrorCount + 1
///     })
/// });
/// </code>
///
/// <para><b>Difference from ErrorTrackingStateData:</b></para>
/// <list type="table">
/// <listheader>
///   <term>Aspect</term>
///   <description>TotalErrorThresholdStateData</description>
/// </listheader>
/// <item>
///   <term>What it counts</term>
///   <description>ALL errors, regardless of type or consecutiveness</description>
/// </item>
/// <item>
///   <term>Resets after</term>
///   <description>Never - accumulates over entire agent run</description>
/// </item>
/// <item>
///   <term>Use case</term>
///   <description>Prevent total degradation from mixed error scenarios</description>
/// </item>
/// </list>
/// </remarks>
[MiddlewareState]
public sealed record TotalErrorThresholdStateData
{

    /// <summary>
    /// Total number of errors encountered in this agent run.
    /// Cumulative - never resets (unlike ErrorTrackingState which resets on success).
    /// </summary>
    public int TotalErrorCount { get; init; } = 0;
}
