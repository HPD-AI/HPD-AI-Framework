using HPDAgent.Graph.Abstractions.Artifacts;

namespace HPD.RAG.Core.Pipeline;

/// <summary>
/// Per-node options passed to MragPipeline.AddHandler's options lambda.
/// Controls artifact declarations and resilience policy overrides.
/// Built-in handlers ship with sensible defaults; this lambda is only needed for overrides.
/// </summary>
public sealed class MragNodeOptions
{
    /// <summary>
    /// Maps directly to Node.ProducesArtifact on HPD.Graph's Node record.
    /// Set on writer nodes to register the corpus artifact they produce.
    /// </summary>
    public ArtifactKey? ProducesArtifact { get; set; }

    /// <summary>
    /// Maps directly to Node.RequiresArtifacts on HPD.Graph's Node record.
    /// Set on nodes that depend on artifacts produced by other pipelines.
    /// </summary>
    public ArtifactKey[]? RequiresArtifacts { get; set; }

    /// <summary>
    /// Override the handler's built-in retry policy.
    /// Null = use the handler's default.
    /// </summary>
    public MragRetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// Override the handler's built-in error propagation behavior.
    /// Null = use the handler's default.
    /// </summary>
    public MragErrorPropagation? ErrorPropagation { get; set; }
}

public sealed record MragRetryPolicy
{
    public required int MaxAttempts { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public MragBackoffStrategy Strategy { get; init; } = MragBackoffStrategy.JitteredExponential;
    public TimeSpan? MaxDelay { get; init; }

    public static MragRetryPolicy Attempts(int n) =>
        new() { MaxAttempts = n, InitialDelay = TimeSpan.FromSeconds(1) };

    public static MragRetryPolicy None =>
        new() { MaxAttempts = 1, InitialDelay = TimeSpan.Zero };
}

public enum MragBackoffStrategy { Constant, Linear, Exponential, JitteredExponential }

/// <summary>Controls what happens to downstream nodes when a node fails after exhausting retries.</summary>
public sealed record MragErrorPropagation
{
    /// <summary>Stop the entire pipeline run immediately.</summary>
    public static MragErrorPropagation StopPipeline { get; } = new() { Mode = PropagationMode.Stop };

    /// <summary>Skip all downstream nodes that depend on this node's output. Independent branches continue.</summary>
    public static MragErrorPropagation SkipDependents { get; } = new() { Mode = PropagationMode.Skip };

    /// <summary>Route to a named fallback node when this node fails.</summary>
    public static MragErrorPropagation FallbackTo(string fallbackNodeId) =>
        new() { Mode = PropagationMode.Fallback, FallbackNodeId = fallbackNodeId };

    /// <summary>Continue — downstream nodes receive null/empty for this node's missing output.</summary>
    public static MragErrorPropagation Isolate { get; } = new() { Mode = PropagationMode.Isolate };

    internal PropagationMode Mode { get; private init; }
    internal string? FallbackNodeId { get; private init; }

    internal enum PropagationMode { Stop, Skip, Fallback, Isolate }
}
