using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Audit;
using HPD.Base.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Auth;

/// <summary>
/// Reports whether the ASP.NET Core HPD.Auth bridge can detect baseline HPD.Auth services.
/// </summary>
internal sealed class HPDBaseAuthAspNetCoreHostIntegrationStatus : IHPDBaseAuthHostIntegrationStatus
{
    private static readonly (Type ServiceType, string Name)[] RequiredServices =
    [
        (typeof(ITenantContext), nameof(ITenantContext)),
        (typeof(UserManager<ApplicationUser>), "UserManager<ApplicationUser>"),
        (typeof(SignInManager<ApplicationUser>), "SignInManager<ApplicationUser>"),
        (typeof(IAuthAuditWriter), nameof(IAuthAuditWriter)),
        (typeof(ISessionManager), nameof(ISessionManager)),
        (typeof(IRefreshTokenStore), nameof(IRefreshTokenStore))
    ];

    private readonly string[] _missingRequiredServiceNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDBaseAuthAspNetCoreHostIntegrationStatus"/> class.
    /// </summary>
    /// <param name="serviceProviderIsService">The service registration probe.</param>
    /// <param name="logger">The host integration status logger.</param>
    public HPDBaseAuthAspNetCoreHostIntegrationStatus(
        IServiceProviderIsService serviceProviderIsService,
        ILogger<HPDBaseAuthAspNetCoreHostIntegrationStatus> logger)
    {
        _missingRequiredServiceNames = RequiredServices
            .Where(service => !serviceProviderIsService.IsService(service.ServiceType))
            .Select(static service => service.Name)
            .ToArray();
        _ = logger;
    }

    /// <inheritdoc />
    public bool HPDAuthServicesDetected => MissingRequiredServiceNames.Length == 0;

    /// <inheritdoc />
    public string Source => "hpd-auth-aspnetcore";

    /// <inheritdoc />
    public string[] MissingRequiredServiceNames => _missingRequiredServiceNames.ToArray();
}
