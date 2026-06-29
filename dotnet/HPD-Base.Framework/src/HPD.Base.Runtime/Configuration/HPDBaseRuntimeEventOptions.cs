namespace HPD.Base.Runtime.Configuration;

public sealed class HPDBaseRuntimeEventOptions
{
    public bool Enabled { get; set; } = true;
    public BaseEventPublishFailureMode PublishFailureMode { get; set; } = BaseEventPublishFailureMode.BestEffort;
    public bool IncludeNoOpEventReferences { get; set; }
}

public enum BaseEventPublishFailureMode
{
    BestEffort,
    Disabled,
    RequireEnqueue
}
