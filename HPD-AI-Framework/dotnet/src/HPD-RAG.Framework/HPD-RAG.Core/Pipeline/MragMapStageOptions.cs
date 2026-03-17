namespace HPD.RAG.Core.Pipeline;

/// <summary>
/// Options for Map stage nodes in MragPipeline.AddMapStage.
/// </summary>
public sealed class MragMapStageOptions
{
    /// <summary>Max concurrent sub-pipeline executions. Default: 4.</summary>
    public int MaxParallelTasks { get; set; } = 4;

    /// <summary>
    /// How a failed batch item affects the rest of the map stage.
    /// Default: ContinueOmitFailures — failed batches are dropped, not fatal.
    /// A partial enrichment result is always better than an aborted ingestion run.
    /// </summary>
    public MragMapErrorMode ErrorMode { get; set; } = MragMapErrorMode.ContinueOmitFailures;

    /// <summary>Per-batch execution timeout. Null = inherits node-level timeout.</summary>
    public TimeSpan? BatchTimeout { get; set; }
}

public enum MragMapErrorMode
{
    /// <summary>Abort the entire map stage on the first batch failure.</summary>
    FailFast,

    /// <summary>Continue remaining batches; failed batch slots become null in output.</summary>
    ContinueWithNulls,

    /// <summary>Continue remaining batches; failed batches are omitted from output entirely (default).</summary>
    ContinueOmitFailures
}
