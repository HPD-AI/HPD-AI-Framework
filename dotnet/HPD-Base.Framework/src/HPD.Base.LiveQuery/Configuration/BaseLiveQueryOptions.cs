namespace HPD.Base.LiveQuery.Configuration;

/// <summary>Configures bounded server-side live-query coordination.</summary>
public sealed class BaseLiveQueryOptions
{
    public int MaxActiveSubscriptions { get; set; } = 1024;
    public int MaxDependenciesPerEvaluation { get; set; } = 64;
    public int TransitionBufferCapacity { get; set; } = 8;
    public int MaxQueryIdLength { get; set; } = 128;
}
