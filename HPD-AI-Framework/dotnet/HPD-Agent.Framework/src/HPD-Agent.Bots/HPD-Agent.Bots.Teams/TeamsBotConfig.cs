using System.ComponentModel.DataAnnotations;

namespace HPD.Agent.Bots.Teams;

/// <summary>
/// Configuration for the Microsoft Teams bot bridge.
/// </summary>
public sealed class TeamsBotConfig
{
    /// <summary>
    /// Microsoft app/client ID for the Teams bot.
    /// </summary>
    [Required]
    public string AppId
    {
        get;
        set => field = value?.Trim()
            ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>
    /// Client secret authentication. Exactly one auth method must be configured.
    /// </summary>
    public string? AppPassword
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Certificate authentication. Exactly one auth method must be configured.
    /// </summary>
    public TeamsAuthCertificate? Certificate { get; set; }

    /// <summary>
    /// Workload identity authentication. Exactly one auth method must be configured.
    /// </summary>
    public TeamsAuthFederated? Federated { get; set; }

    /// <summary>
    /// Tenant ID. Required for single-tenant bots and Graph access.
    /// </summary>
    public string? AppTenantId
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Microsoft app type. Common values are MultiTenant and SingleTenant.
    /// </summary>
    public string AppType
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value)
            ? "MultiTenant"
            : value.Trim();
    } = "MultiTenant";

    /// <summary>
    /// Bot display name used when opening direct-message conversations.
    /// </summary>
    public string? UserName
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Optional HPD agent name to route inbound Teams messages to.
    /// </summary>
    public string? AgentName
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Validates configuration values that span multiple properties.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AppId))
        {
            throw new InvalidOperationException("Teams AppId is required.");
        }

        var authMethodCount = 0;
        if (!string.IsNullOrWhiteSpace(AppPassword)) authMethodCount++;
        if (Certificate is not null) authMethodCount++;
        if (Federated is not null) authMethodCount++;

        if (authMethodCount != 1)
        {
            throw new InvalidOperationException(
                "Exactly one Teams authentication method must be configured: AppPassword, Certificate, or Federated.");
        }

        Certificate?.Validate();
        Federated?.Validate();

        if (AppType.Equals("SingleTenant", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(AppTenantId))
        {
            throw new InvalidOperationException("Teams AppTenantId is required when AppType is SingleTenant.");
        }
    }
}

/// <summary>
/// Certificate-based Teams bot authentication settings.
/// </summary>
public sealed class TeamsAuthCertificate
{
    public string CertificatePrivateKey
    {
        get;
        set => field = value?.Trim()
            ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    public string? CertificateThumbprint
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string? X5c
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CertificatePrivateKey))
        {
            throw new InvalidOperationException("Teams certificate authentication requires CertificatePrivateKey.");
        }

        if (string.IsNullOrWhiteSpace(CertificateThumbprint) && string.IsNullOrWhiteSpace(X5c))
        {
            throw new InvalidOperationException("Teams certificate authentication requires CertificateThumbprint or X5c.");
        }
    }
}

/// <summary>
/// Workload identity Teams bot authentication settings.
/// </summary>
public sealed class TeamsAuthFederated
{
    public string ClientId
    {
        get;
        set => field = value?.Trim()
            ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    public string ClientAudience
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value)
            ? "api://AzureADTokenExchange"
            : value.Trim();
    } = "api://AzureADTokenExchange";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("Teams federated authentication requires ClientId.");
        }
    }
}
