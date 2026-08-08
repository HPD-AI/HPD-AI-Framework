using HPD.Gateway.Abstractions.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway.Admin;

public static class GatewayAdminServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGatewayAdmin(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddProblemDetails();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, GatewayJsonSerializerContext.Default);
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, GatewayAdminJsonContext.Default);
        });
        services.AddOpenApi("hpd-gateway-v1", options =>
        {
            options.AddSchemaTransformer<GatewayAdminOpenApiSchemaTransformer>();
            options.AddDocumentTransformer<GatewayAdminOpenApiDocumentTransformer>();
        });
        services.AddSingleton<GatewayBackupSinkRegistry>();
        services.AddSingleton<GatewayAdminOpenApiContract>();
        return services;
    }
}
