using System.Text.Json;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Validation;

namespace HPDAgent.Graph.Core.Config;

/// <summary>
/// Registry-backed options used when compiling declarative graph config into runtime graph objects.
/// </summary>
public sealed class GraphConfigCompilerOptions
{
    private readonly Dictionary<string, Func<JsonElement?, PartitionDependencyMapping>> _partitionDependencyMappings =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, Func<JsonElement?, IInputValidator>> _inputValidators =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, Type> _types =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a custom partition dependency mapping factory by descriptor name.
    /// </summary>
    public GraphConfigCompilerOptions RegisterPartitionDependencyMapping(
        string name,
        Func<JsonElement?, PartitionDependencyMapping> factory)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Custom partition dependency mapping name is required.", nameof(name));
        }

        _partitionDependencyMappings[name] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>
    /// Registers a custom input validator factory by descriptor name.
    /// </summary>
    public GraphConfigCompilerOptions RegisterInputValidator(
        string name,
        Func<JsonElement?, IInputValidator> factory)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Custom input validator name is required.", nameof(name));
        }

        _inputValidators[name] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    /// <summary>
    /// Registers a type name that may be referenced by graph config.
    /// </summary>
    public GraphConfigCompilerOptions RegisterType<T>(string? name = null)
        => RegisterType(name ?? typeof(T).FullName ?? typeof(T).Name, typeof(T));

    /// <summary>
    /// Registers a type name that may be referenced by graph config.
    /// </summary>
    public GraphConfigCompilerOptions RegisterType(string name, Type type)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Type registration name is required.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(type);

        _types[name] = type;

        if (!string.IsNullOrWhiteSpace(type.FullName))
        {
            _types[type.FullName] = type;
        }

        if (!string.IsNullOrWhiteSpace(type.AssemblyQualifiedName))
        {
            _types[type.AssemblyQualifiedName] = type;
        }

        return this;
    }

    internal PartitionDependencyMapping ResolvePartitionDependencyMapping(string name, JsonElement? arguments)
    {
        if (!_partitionDependencyMappings.TryGetValue(name, out var factory))
        {
            throw new InvalidOperationException($"Custom partition dependency mapping '{name}' is not registered.");
        }

        return factory(arguments);
    }

    internal IInputValidator ResolveInputValidator(string name, JsonElement? arguments)
    {
        if (!_inputValidators.TryGetValue(name, out var factory))
        {
            throw new InvalidOperationException($"Custom input validator '{name}' is not registered.");
        }

        return factory(arguments);
    }

    internal Type? ResolveType(string name)
        => _types.TryGetValue(name, out var type) ? type : null;
}
