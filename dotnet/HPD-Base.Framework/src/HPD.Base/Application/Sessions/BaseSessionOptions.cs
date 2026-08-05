namespace HPD.Base;

/// <summary>
/// Configures stable scope carried by one BASE application session.
/// </summary>
public sealed class BaseSessionOptions
{
    /// <summary>Gets or sets the operation mode.</summary>
    public OperationMode Mode { get; set; } = OperationMode.User;

    /// <summary>Gets or sets the stable tenant scope.</summary>
    public string? TenantId { get; set; }

    /// <summary>Gets or sets the stable project scope.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Gets or sets the safe correlation identifier.</summary>
    public string? CorrelationId { get; set; }
}
