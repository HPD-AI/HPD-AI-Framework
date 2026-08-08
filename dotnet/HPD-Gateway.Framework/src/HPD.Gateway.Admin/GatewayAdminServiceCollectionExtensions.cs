using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway.Admin;

public static class GatewayAdminServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGatewayAdmin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddProblemDetails();
        services.AddSingleton<GatewayBackupSinkRegistry>();
        return services;
    }
}
