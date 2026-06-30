namespace HPD.Base.AspNetCore.Configuration;

/// <summary>
/// Configures HTTP transport limits enforced before requests reach HPD.BASE Runtime.
/// </summary>
public sealed class HPDBaseHttpLimitOptions
{
    /// <summary>
    /// Gets or sets the maximum accepted query string length.
    /// </summary>
    public int MaxQueryStringLength { get; set; } = 16_384;

    /// <summary>
    /// Gets or sets the maximum accepted serialized filter length.
    /// </summary>
    public int MaxFilterLength { get; set; } = 16_384;

    /// <summary>
    /// Gets or sets the maximum route record id length.
    /// </summary>
    public int MaxRouteIdLength { get; set; } = 512;

    /// <summary>
    /// Gets or sets the maximum repeated values accepted for one query parameter.
    /// </summary>
    public int MaxRepeatedParameterValues { get; set; } = 256;

    /// <summary>
    /// Gets or sets the maximum number of simple query list items accepted by transport binders.
    /// </summary>
    public int MaxQueryListItems { get; set; } = 256;

    /// <summary>
    /// Gets or sets the maximum request body length accepted before JSON binding.
    /// </summary>
    public long MaxRequestBodyLength { get; set; } = 1_048_576;
}
