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
            options.CreateSchemaReferenceId = static typeInfo =>
                GatewayAdminSchemaReferenceIds.Create(typeInfo.Type);
            options.AddSchemaTransformer<GatewayAdminOpenApiSchemaTransformer>();
            options.AddDocumentTransformer<GatewayAdminOpenApiDocumentTransformer>();
        });
        services.AddSingleton<GatewayBackupSinkRegistry>();
        services.AddSingleton<GatewayAdminOpenApiContract>();
        return services;
    }
}

internal static class GatewayAdminSchemaReferenceIds
{
    internal static string? Create(Type type)
    {
        string? ns = type.Namespace;
        if (ns is null || !ns.StartsWith("HPD.Gateway", StringComparison.Ordinal)) return null;
        string source = type.FullName ?? throw new InvalidOperationException("Gateway OpenAPI type has no stable full name.");
        var builder = new System.Text.StringBuilder(source.Length);
        foreach (char value in source)
            builder.Append(char.IsAsciiLetterOrDigit(value) ? value : '_');
        if (builder.Length is < 1 or > 256)
            throw new InvalidOperationException("Gateway OpenAPI schema reference ID is outside its bound.");
        return builder.ToString();
    }
}
