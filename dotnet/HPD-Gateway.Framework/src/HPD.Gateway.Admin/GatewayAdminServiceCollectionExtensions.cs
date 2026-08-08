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
            options.SerializerOptions.Converters.Add(GatewayInt64JsonConverter.Instance);
            options.SerializerOptions.Converters.Add(GatewayUInt64JsonConverter.Instance);
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

internal sealed class GatewayInt64JsonConverter : System.Text.Json.Serialization.JsonConverter<long>
{
    internal static GatewayInt64JsonConverter Instance { get; } = new();
    public override long Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        string? text = reader.TokenType == System.Text.Json.JsonTokenType.String ? reader.GetString() : null;
        if (text is null || !long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out long value) ||
            value.ToString(System.Globalization.CultureInfo.InvariantCulture) != text)
            throw new System.Text.Json.JsonException("Expected a canonical signed 64-bit decimal string.");
        return value;
    }
    public override void Write(System.Text.Json.Utf8JsonWriter writer, long value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

internal sealed class GatewayUInt64JsonConverter : System.Text.Json.Serialization.JsonConverter<ulong>
{
    internal static GatewayUInt64JsonConverter Instance { get; } = new();
    public override ulong Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        string? text = reader.TokenType == System.Text.Json.JsonTokenType.String ? reader.GetString() : null;
        if (text is null || !ulong.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out ulong value) ||
            value.ToString(System.Globalization.CultureInfo.InvariantCulture) != text)
            throw new System.Text.Json.JsonException("Expected a canonical unsigned 64-bit decimal string.");
        return value;
    }
    public override void Write(System.Text.Json.Utf8JsonWriter writer, ulong value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
