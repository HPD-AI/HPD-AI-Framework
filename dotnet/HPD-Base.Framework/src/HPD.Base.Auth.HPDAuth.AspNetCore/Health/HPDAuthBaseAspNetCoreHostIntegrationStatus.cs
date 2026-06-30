using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Base.Auth.HPDAuth.Health;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

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

    private readonly IServiceProviderIsService _serviceProviderIsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDAuthBaseAspNetCoreHostIntegrationStatus"/> class.
    /// </summary>
    /// <param name="serviceProviderIsService">The service registration probe.</param>
    public HPDAuthBaseAspNetCoreHostIntegrationStatus(IServiceProviderIsService serviceProviderIsService)
    {
        _serviceProviderIsService = serviceProviderIsService;
    }

    /// <inheritdoc />
    public bool HPDAuthServicesDetected => MissingRequiredServiceNames.Length == 0;

    /// <inheritdoc />
    public string Source => "hpd-auth-aspnetcore";

    /// <inheritdoc />
    public string[] MissingRequiredServiceNames => RequiredServices
        .Where(service => !_serviceProviderIsService.IsService(service.ServiceType))
        .Select(static service => service.Name)
        .ToArray();
}
