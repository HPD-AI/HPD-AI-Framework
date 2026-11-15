using HPD.Agent.Providers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace HPD.Agent.AspNetCore.OpenApi;

internal static class HPDAgentOpenApiPortableSchema
{
    public const string ToolHarnessReferenceName = "ToolHarnessReference";
    public const string MiddlewareReferenceName = "MiddlewareReference";
    public const string ToolHarnessReferencesName = "ToolHarnessReferences";
    public const string MiddlewareReferencesName = "MiddlewareReferences";

    public static void EnsureReferenceComponents(OpenApiDocument document)
    {
        EnsureReferenceComponent(
            document,
            ToolHarnessReferenceName,
            BuildToolHarnessReference());
        EnsureReferenceComponent(
            document,
            MiddlewareReferenceName,
            BuildMiddlewareReference());
    }

    public static void ApplyAuthenticationDiscriminator(
        OpenApiSchema schema,
        Type type)
    {
        var discriminator = type == typeof(ApiKeyProviderAuthentication)
            ? "api-key"
            : type == typeof(OAuthProviderAuthentication)
                ? "oauth"
                : type == typeof(ExternalIdentityProviderAuthentication)
                    ? "external-identity"
                    : type == typeof(AnonymousProviderAuthentication)
                        ? "anonymous"
                        : null;
        if (discriminator is null)
            return;

        schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
        schema.Properties["type"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = [JsonValue.Create(discriminator)]
        };
        schema.Required ??= new HashSet<string>();
        schema.Required.Add("type");
    }

    public static void ApplyGeneratedDocumentSchemas(OpenApiDocument document)
    {
        EnsureReferenceComponents(document);
        ReplaceReferenceComponent(
            document,
            ToolHarnessReferencesName,
            new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = CollectionItemReference(document, ToolHarnessReferenceName)
            });
        ReplaceReferenceComponent(
            document,
            MiddlewareReferencesName,
            new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = CollectionItemReference(document, MiddlewareReferenceName)
            });
        ApplyCollectionReference(
            document,
            "AgentConfig",
            "toolHarnesses",
            ToolHarnessReferencesName);
        ApplyCollectionReference(
            document,
            "AgentConfig",
            "middlewares",
            MiddlewareReferencesName);
        ApplyCollectionReference(
            document,
            "AgentConfigUpdate",
            "toolHarnesses",
            ToolHarnessReferencesName);
        ApplyCollectionReference(
            document,
            "AgentConfigUpdate",
            "middlewares",
            MiddlewareReferencesName);
    }

    private static void EnsureReferenceComponent(
        OpenApiDocument document,
        string name,
        IOpenApiSchema schema)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??=
            new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        document.Components.Schemas.TryAdd(name, schema);
    }

    private static void ReplaceReferenceComponent(
        OpenApiDocument document,
        string name,
        IOpenApiSchema schema)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??=
            new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        document.Components.Schemas[name] = schema;
    }

    private static void ApplyCollectionReference(
        OpenApiDocument document,
        string componentName,
        string propertyName,
        string collectionComponentName)
    {
        if (document.Components?.Schemas?.TryGetValue(componentName, out var component) != true ||
            component is not OpenApiSchema schema ||
            schema.Properties?.ContainsKey(propertyName) != true)
            return;

        schema.Properties[propertyName] =
            new OpenApiSchemaReference(collectionComponentName, document);
    }

    private static IOpenApiSchema CollectionItemReference(
        OpenApiDocument document,
        string componentName) =>
        new OpenApiSchemaReference(componentName, document);

    private static OpenApiSchema BuildToolHarnessReference() =>
        new()
        {
            Description =
                "A ToolHarness owner name or a rich portable ToolHarness reference.",
            OneOf =
            [
                StringSchema(),
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["name"] = StringSchema(),
                        ["functions"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Array,
                            Items = StringSchema()
                        },
                        ["config"] = JsonObjectSchema(),
                        ["metadata"] = JsonObjectSchema(),
                        ["middlewareConfigs"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            AdditionalProperties = JsonObjectSchema()
                        }
                    },
                    Required = new HashSet<string>(StringComparer.Ordinal) { "name" }
                }
            ]
        };

    private static OpenApiSchema BuildMiddlewareReference() =>
        new()
        {
            Description =
                "A middleware name or a rich portable middleware reference.",
            OneOf =
            [
                StringSchema(),
                new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["name"] = StringSchema(),
                        ["config"] = JsonObjectSchema()
                    },
                    Required = new HashSet<string>(StringComparer.Ordinal) { "name" }
                }
            ]
        };

    private static OpenApiSchema StringSchema() =>
        new() { Type = JsonSchemaType.String };

    private static OpenApiSchema JsonObjectSchema() =>
        new()
        {
            Type = JsonSchemaType.Object,
            AdditionalProperties = new OpenApiSchema()
        };
}
