namespace HPDAgent.Graph.Abstractions.Routing;

/// <summary>
/// Marks a class as a Map router for source-generated DI registration.
/// Pattern: Identical to [GraphNodeHandler] attribute.
/// Routers must implement IMapRouter interface.
/// Source generator will create DI registration extension methods.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class MapRouterAttribute : Attribute
{
    /// <summary>
    /// Optional custom name for router registration.
    /// If not specified, uses the class name.
    /// </summary>
    public string? Name { get; set; }
}
