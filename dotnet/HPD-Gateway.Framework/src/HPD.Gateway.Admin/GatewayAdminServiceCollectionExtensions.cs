using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway.Admin;

public static class GatewayAdminServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGatewayAdmin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddProblemDetails();
        services.AddOpenApi("hpd-gateway-v1");
        services.AddSingleton<GatewayBackupSinkRegistry>();
        return services;
    }
}
