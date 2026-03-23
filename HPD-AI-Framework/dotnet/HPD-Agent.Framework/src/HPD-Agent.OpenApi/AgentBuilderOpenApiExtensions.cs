namespace HPD.Agent.OpenApi;

/// <summary>
/// Extension methods on <see cref="AgentBuilder"/> for loading OpenAPI specifications
/// as agent tools at build time.
///
/// Functions generated from OpenAPI specs participate in the full collapsing and permission
/// infrastructure — they can be flat members of a toolkit, nested inside a toolkit's collapse
/// container, or standalone top-level tools — with the same visibility rules as native
/// <c>[AIFunction]</c> methods.
/// </summary>
public static class AgentBuilderOpenApiExtensions
{
    /// <summary>
    /// Adds OpenAPI functions from a local JSON spec file.
    /// Each operation in the spec becomes a top-level <c>AIFunction</c> at build time.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="name">Display name and function name prefix (e.g., "stripe" → "stripe_listCustomers").</param>
    /// <param name="specPath">Path to the JSON OpenAPI spec file.</param>
    /// <param name="configure">Optional callback to configure authentication, filtering, etc.</param>
    public static AgentBuilder WithOpenApi(
        this AgentBuilder builder,
        string name,
        string specPath,
        Action<OpenApiConfig>? configure = null)
    {
        var config = new OpenApiConfig { SpecPath = specPath };
        configure?.Invoke(config);
        builder.AddOpenApiSource(new OpenApiSourceRegistration(
            Name: name,
            ParentContainer: null,
            CollapseWithinToolkit: false,
            Config: config));
        return builder;
    }

    /// <summary>
    /// Adds OpenAPI functions by fetching a spec from a URI.
    /// Each operation in the spec becomes a top-level <c>AIFunction</c> at build time.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="name">Display name and function name prefix.</param>
    /// <param name="specUri">URI to fetch the JSON OpenAPI spec from.</param>
    /// <param name="configure">Optional callback to configure authentication, filtering, etc.</param>
    public static AgentBuilder WithOpenApi(
        this AgentBuilder builder,
        string name,
        Uri specUri,
        Action<OpenApiConfig>? configure = null)
    {
        var config = new OpenApiConfig { SpecUri = specUri };
        configure?.Invoke(config);
        builder.AddOpenApiSource(new OpenApiSourceRegistration(
            Name: name,
            ParentContainer: null,
            CollapseWithinToolkit: false,
            Config: config));
        return builder;
    }

    /// <summary>
    /// Adds OpenAPI functions using a fully pre-configured <see cref="OpenApiConfig"/>.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="name">Display name and function name prefix.</param>
    /// <param name="config">Pre-configured OpenAPI config with spec location, auth, and other settings.</param>
    public static AgentBuilder WithOpenApi(
        this AgentBuilder builder,
        string name,
        OpenApiConfig config)
    {
        builder.AddOpenApiSource(new OpenApiSourceRegistration(
            Name: name,
            ParentContainer: null,
            CollapseWithinToolkit: false,
            Config: config));
        return builder;
    }
}
