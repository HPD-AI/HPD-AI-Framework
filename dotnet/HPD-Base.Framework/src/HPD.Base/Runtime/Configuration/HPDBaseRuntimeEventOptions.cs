namespace HPD.Base;

/// <summary>Represents a hpdbase runtime event options.</summary>
public sealed class HPDBaseRuntimeEventOptions
{
    /// <summary>Gets or sets the enabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets the publish failure mode.</summary>
    public BaseEventPublishFailureMode PublishFailureMode { get; set; } = BaseEventPublishFailureMode.BestEffort;
    /// <summary>Gets or sets the include no op event references.</summary>
    public bool IncludeNoOpEventReferences { get; set; }
    /// <summary>Gets or sets the post commit work timeout.</summary>
    public TimeSpan PostCommitWorkTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Defines the base event publish failure mode contract.</summary>
public enum BaseEventPublishFailureMode
{
    /// <summary>Identifies best effort.</summary>
BestEffort,
    /// <summary>Identifies disabled.</summary>
Disabled,
    /// <summary>Identifies require enqueue.</summary>
RequireEnqueue
}
