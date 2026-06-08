namespace HPD.Agent.Bots;

/// <summary>
/// Overrides the HTTP methods used by the generated HTTP endpoint bridge
/// endpoint. Bots without this attribute use POST only.
/// </summary>
/// <param name="methods">HTTP methods accepted by the webhook endpoint.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HpdHttpMethodsAttribute(params string[] methods) : Attribute
{
    /// <summary>HTTP methods accepted by the webhook endpoint.</summary>
    public IReadOnlyList<string> Methods => methods;
}
