namespace HPD.Base;

/// <summary>Configures bounded vector HTTP request handling.</summary>
public sealed class HPDBaseVectorHttpOptions
{
    /// <summary>Gets or sets the maximum request-body bytes.</summary>
    public long MaxRequestBodyBytes { get; set; } = 256 * 1024;
}

internal sealed record HPDBaseVectorHttpSnapshot(long MaxRequestBodyBytes);
