using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Secrets;

namespace HPD.Agent;

/// <summary>Declares an owner-local default implementation resolved after application DI.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ToolHarnessDependencyAttribute(Type serviceType, Type implementationType) : Attribute
{
    public Type ServiceType { get; } = serviceType;
    public Type ImplementationType { get; } = implementationType;
}

/// <summary>Generated factory for an owner-local, HPD-owned dependency.</summary>
public sealed record ToolHarnessDependency(Type ServiceType, Func<ToolHarnessConstructionContext, object> Create);

/// <summary>Explicit input to generated construction. Resolved DI objects remain borrowed.</summary>
public sealed class ToolHarnessConstructionContext
{
    private readonly IServiceProvider? _services;
    private readonly IReadOnlyDictionary<Type, object> _overrides;
    private readonly ISecretResolver? _secrets;
    private readonly JsonElement? _config;
    private readonly IReadOnlyList<ToolHarnessDependency> _defaults;
    private readonly ToolHarnessOwnerLifetime _lifetime;
    private readonly Dictionary<Type, object> _created = [];
    private readonly HashSet<Type> _resolving = [];
    private readonly HashSet<Type> _usedOverrides = [];
    private bool _configurationRead;
    private readonly object? _configurationHandle;

    internal ToolHarnessConstructionContext(string owner, IServiceProvider? services,
        IReadOnlyDictionary<Type, object> overrides, ISecretResolver? secrets, JsonElement? config,
        IReadOnlyList<ToolHarnessDependency> defaults, ToolHarnessOwnerLifetime lifetime, object? configurationHandle = null)
    {
        OwnerIdentity = owner;
        _services = services;
        _overrides = overrides;
        _secrets = secrets;
        _config = config;
        _defaults = defaults;
        _lifetime = lifetime;
        _configurationHandle = configurationHandle;
        var duplicate = defaults.GroupBy(value => value.ServiceType).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Harness '{owner}' declares conflicting dependency defaults for '{duplicate.Key}'.");
    }

    internal void ValidateConsumedInputs()
    {
        if (_config is not null && !_configurationRead)
            throw new InvalidOperationException($"Harness '{OwnerIdentity}' constructor does not consume the supplied configuration.");
        var unused = _overrides.Keys.FirstOrDefault(type => !_usedOverrides.Contains(type));
        if (unused is not null)
            throw new InvalidOperationException($"Harness '{OwnerIdentity}' does not resolve overridden dependency '{unused}'. External DI graphs are not rewritten.");
    }

    public string OwnerIdentity { get; }
    public IHarnessConfig<T> GetHarnessConfig<T>() where T : class
    {
        _configurationRead = true;
        return _configurationHandle as IHarnessConfig<T>
            ?? throw new InvalidOperationException($"Harness '{OwnerIdentity}' has no compatible settings contract for '{typeof(T)}'.");
    }

    /// <summary>Resolves one borrowed dependency without reconstructing external DI graphs.</summary>
    public T GetRequiredService<T>() where T : class
        => GetOptionalService<T>() ?? throw new InvalidOperationException(
            $"Harness '{OwnerIdentity}' requires dependency '{typeof(T)}'. Register it in DI or use UseDependency.");

    public T? GetOptionalService<T>() where T : class
    {
        if (_overrides.TryGetValue(typeof(T), out var value))
        {
            _usedOverrides.Add(typeof(T));
            return (T)value;
        }
        if (typeof(T) == typeof(ISecretResolver) && _secrets is not null) return (T)(object)_secrets;
        if (_services?.GetService(typeof(T)) is T registered) return registered;
        if (_created.TryGetValue(typeof(T), out var created)) return (T)created;
        var candidates = _defaults.Where(candidate => candidate.ServiceType == typeof(T)).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length != 1)
            throw new InvalidOperationException($"Harness '{OwnerIdentity}' declares conflicting defaults for '{typeof(T)}'.");
        if (!_resolving.Add(typeof(T)))
            throw new InvalidOperationException($"Harness '{OwnerIdentity}' has a dependency cycle at '{typeof(T)}'.");
        try
        {
            var instance = candidates[0].Create(this);
            _lifetime.Own(instance);
            if (instance is not T typed)
                throw new InvalidOperationException($"Harness '{OwnerIdentity}' produced an incompatible default for '{typeof(T)}'.");
            _created.Add(typeof(T), typed);
            return typed;
        }
        finally { _resolving.Remove(typeof(T)); }
    }

    /// <summary>Binds a construction payload using the library's generated JSON contract.</summary>
    public T GetConfiguration<T>(JsonTypeInfo<T> codec, Func<T>? defaults = null)
    {
        _configurationRead = true;
        if (_config is { } json)
            return JsonSerializer.Deserialize(json, codec)
                ?? throw new InvalidOperationException($"Harness '{OwnerIdentity}' configuration cannot be null.");
        if (defaults is not null) return defaults();
        throw new InvalidOperationException($"Harness '{OwnerIdentity}' requires configuration '{typeof(T)}'.");
    }
}

/// <summary>One build-to-agent ownership ledger; borrowed dependencies never enter it.</summary>
internal sealed class ToolHarnessOwnerLifetime : IAsyncDisposable
{
    private readonly List<object> _owned = [];
    private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    internal void Own(object instance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_seen.Add(instance)) _owned.Add(instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        for (var index = _owned.Count - 1; index >= 0; index--)
        {
            try
            {
                if (_owned[index] is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (_owned[index] is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        _owned.Clear();
        _seen.Clear();
        if (failures is not null) throw new AggregateException("Harness owner cleanup failed.", failures);
    }
}
