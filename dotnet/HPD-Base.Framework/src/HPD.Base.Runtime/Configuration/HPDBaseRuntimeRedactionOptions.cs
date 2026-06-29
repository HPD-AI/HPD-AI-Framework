namespace HPD.Base.Runtime.Configuration;

public sealed class HPDBaseRuntimeRedactionOptions
{
    public bool RedactPublicErrors { get; set; } = true;
    public bool RedactPublicEventSnapshots { get; set; } = true;
    public bool RedactPublicHealthDependencies { get; set; } = true;
}
