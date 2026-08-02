namespace HPD.Base;

/// <summary>Configures bounded server-side live-query coordination.</summary>
public sealed class BaseLiveQueryOptions
{
    /// <summary>Gets or sets the max active subscriptions.</summary>
    public int MaxActiveSubscriptions { get; set; } = 1024;
    /// <summary>Gets or sets the max dependencies per evaluation.</summary>
    public int MaxDependenciesPerEvaluation { get; set; } = 64;
    /// <summary>Gets or sets the transition buffer capacity.</summary>
    public int TransitionBufferCapacity { get; set; } = 8;
    /// <summary>Gets or sets the max query ID length.</summary>
    public int MaxQueryIdLength { get; set; } = 128;
    /// <summary>Gets or sets the max evaluation duration.</summary>
    public TimeSpan MaxEvaluationDuration { get; set; } = TimeSpan.FromSeconds(30);
}
