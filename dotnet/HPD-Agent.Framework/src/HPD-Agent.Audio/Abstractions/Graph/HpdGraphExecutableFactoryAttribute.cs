namespace HPD.Agent.Audio.Graph;

/// <summary>Declares metadata for one application-scoped graph executable factory without invoking it.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class HpdGraphExecutableFactoryAttribute : Attribute
{
    /// <summary>Initializes the class-target declaration form.</summary>
    /// <param name="nodeKey">The exact printable-ASCII topology node key.</param>
    /// <param name="implementationRevision">The positive implementation revision.</param>
    public HpdGraphExecutableFactoryAttribute(string nodeKey, uint implementationRevision)
    {
        NodeKey = nodeKey ?? throw new ArgumentNullException(nameof(nodeKey));
        ImplementationRevision = implementationRevision;
    }

    /// <summary>Initializes the assembly-target generated-contribution form.</summary>
    /// <param name="implementationType">The exact declared implementation type.</param>
    /// <param name="nodeKey">The exact printable-ASCII topology node key.</param>
    /// <param name="implementationRevision">The positive implementation revision.</param>
    public HpdGraphExecutableFactoryAttribute(Type implementationType, string nodeKey, uint implementationRevision)
    {
        ImplementationType = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
        NodeKey = nodeKey ?? throw new ArgumentNullException(nameof(nodeKey));
        ImplementationRevision = implementationRevision;
    }

    /// <summary>Gets the implementation type carried only by the assembly contribution form.</summary>
    public Type? ImplementationType { get; }

    /// <summary>Gets the exact topology node key.</summary>
    public string NodeKey { get; }

    /// <summary>Gets the positive implementation revision.</summary>
    public uint ImplementationRevision { get; }
}
