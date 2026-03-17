namespace HPD.Agent;

/// <summary>
/// Marks a method in a toolkit class as an OpenAPI spec provider.
/// The method must return <c>OpenApiConfig</c> (from HPD-Agent.OpenApi) and must be parameterless.
/// Each operation in the spec becomes an <c>AIFunction</c> at runtime during <c>Build()</c>.
///
/// <c>ISecretResolver</c> is available via constructor injection — declare it as a constructor
/// parameter on the toolkit class and AgentBuilder wires it automatically through
/// CompositeServiceProvider. Secrets are resolved inside the AuthCallback closure at
/// request time, so vault rotation and OAuth refresh work without rebuilding:
///
/// <code>
/// public class StripeToolkit(ISecretResolver secrets)
/// {
///     [OpenApi(Prefix = "stripe")]
///     public OpenApiConfig Stripe() => new()
///     {
///         SpecPath = "stripe.json",
///         AuthCallback = async (req, ct) =>
///         {
///             var key = await secrets.RequireAsync("stripe:ApiKey", "Stripe", ct: ct);
///             req.Headers.Authorization = new("Bearer", key);
///         }
///     };
/// }
/// </code>
///
/// <c>[OpenApi]</c> methods must be parameterless — HPDAG0403 is emitted if parameters are declared.
/// Use <c>[RequiresPermission]</c> on the method to require permission for all generated functions.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OpenApiAttribute : Attribute
{
    /// <summary>
    /// Optional prefix for generated function names.
    /// Functions are named: <c>{Prefix}_{OperationId}</c>.
    /// Default: the method name.
    /// </summary>
    public string? Prefix { get; set; }
}
