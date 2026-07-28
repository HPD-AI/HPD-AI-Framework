using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Base.Auth.HPDAuth.Health;
using HPD.Base.Auth.HPDAuth.AspNetCore.Observability.Logging;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Auth.HPDAuth.AspNetCore.Health;

/// <summary>
/// Reports whether the ASP.NET Core HPD.Auth bridge can detect baseline HPD.Auth services.
/// </summary>
public sealed class HPDAuthBaseAspNetCoreHostIntegrationStatus : IHPDAuthBaseHostIntegrationStatus
{
    private static readonly (Type ServiceType, string Name)[] RequiredServices =
    [
        (typeof(ITenantContext), nameof(ITenantContext)),
        (typeof(UserManager<ApplicationUser>), "UserManager<ApplicationUser>"),
        (typeof(SignInManager<ApplicationUser>), "SignInManager<ApplicationUser>"),
        (typeof(IAuditLogger), nameof(IAuditLogger)),
        (typeof(ISessionManager), nameof(ISessionManager)),
        (typeof(IRefreshTokenStore), nameof(IRefreshTokenStore))
    ];

    private readonly string[] _missingRequiredServiceNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDAuthBaseAspNetCoreHostIntegrationStatus"/> class.
    /// </summary>
    /// <param name="serviceProviderIsService">The service registration probe.</param>
    /// <param name="logger">The host integration status logger.</param>
    public HPDAuthBaseAspNetCoreHostIntegrationStatus(
        IServiceProviderIsService serviceProviderIsService,
        ILogger<HPDAuthBaseAspNetCoreHostIntegrationStatus> logger)
    {
        _missingRequiredServiceNames = RequiredServices
            .Where(service => !serviceProviderIsService.IsService(service.ServiceType))
            .Select(static service => service.Name)
            .ToArray();
        if (_missingRequiredServiceNames.Length != 0)
        {
            HPDBaseHPDAuthAspNetCoreLog.HostIntegrationUnavailable(
                logger,
                HPDAuthBaseDiagnosticIds.MissingAuthServices);
        }
    }

    /// <inheritdoc />
    public bool HPDAuthServicesDetected => MissingRequiredServiceNames.Length == 0;

    /// <inheritdoc />
    public string Source => "hpd-auth-aspnetcore";

    /// <inheritdoc />
    public string[] MissingRequiredServiceNames => _missingRequiredServiceNames.ToArray();
}
