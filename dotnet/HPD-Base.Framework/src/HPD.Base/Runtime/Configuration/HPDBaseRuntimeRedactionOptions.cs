namespace HPD.Base;

/// <summary>Represents a hpdbase runtime redaction options.</summary>
public sealed class HPDBaseRuntimeRedactionOptions
{
    /// <summary>Gets or sets the redact public errors.</summary>
    public bool RedactPublicErrors { get; set; } = true;
    /// <summary>Gets or sets the redact public event snapshots.</summary>
    public bool RedactPublicEventSnapshots { get; set; } = true;
    /// <summary>Gets or sets the redact public health dependencies.</summary>
    public bool RedactPublicHealthDependencies { get; set; } = true;
}
