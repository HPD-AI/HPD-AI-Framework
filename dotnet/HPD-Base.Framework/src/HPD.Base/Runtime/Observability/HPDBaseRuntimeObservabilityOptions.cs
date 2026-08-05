namespace HPD.Base;

/// <summary>
/// Configures HPD.BASE runtime observability behavior.
/// </summary>
public sealed class HPDBaseRuntimeObservabilityOptions
{
    /// <summary>
    /// Gets or sets whether runtime spans and measurements are emitted when listeners exist.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
