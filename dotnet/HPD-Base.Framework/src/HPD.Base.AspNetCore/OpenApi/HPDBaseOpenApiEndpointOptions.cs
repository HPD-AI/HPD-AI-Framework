namespace HPD.Base.AspNetCore.OpenApi;

/// <summary>
/// Configures HPD.BASE OpenAPI endpoint mapping.
/// </summary>
public sealed class HPDBaseOpenApiEndpointOptions
{
    /// <summary>Gets or sets the JSON document route pattern.</summary>
    public string RoutePattern { get; set; } = "/base/openapi/{documentName}.json";

    /// <summary>Gets or sets whether a YAML document route is also mapped.</summary>
    public bool MapYaml { get; set; }

    /// <summary>Gets or sets the YAML document route pattern.</summary>
    public string YamlRoutePattern { get; set; } = "/base/openapi/{documentName}.yaml";

    /// <summary>Gets or sets whether OpenAPI document endpoints are hidden from OpenAPI output.</summary>
    public bool ExcludeOpenApiEndpointFromDescription { get; set; } = true;
}
