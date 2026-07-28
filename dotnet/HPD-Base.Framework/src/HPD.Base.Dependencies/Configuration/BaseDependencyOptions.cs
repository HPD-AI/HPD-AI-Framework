namespace HPD.Base.Dependencies.Configuration;

/// <summary>Configures dependency-reference protection and output bounds.</summary>
public sealed class BaseDependencyOptions
{
    /// <summary>Gets or sets the host secret used for keyed opaque references.</summary>
    public byte[] ProtectionKey { get; set; } = [];

    /// <summary>Gets or sets the maximum references emitted for one mutation.</summary>
    public int MaxReferencesPerInvalidation { get; set; } = 32;
}
