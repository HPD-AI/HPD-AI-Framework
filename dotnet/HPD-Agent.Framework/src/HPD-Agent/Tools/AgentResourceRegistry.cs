namespace HPD.Agent;

/// <summary>Agent-lifetime resources explicitly exposed to generated ToolHarness activation.</summary>
internal sealed class AgentResourceRegistry : IAsyncDisposable
{
    private readonly IReadOnlyDictionary<Type, object> _resources;
    private int _disposed;

    internal AgentResourceRegistry(IEnumerable<ToolHarnessAgentResourceDescriptor> descriptors)
    {
        var materializedDescriptors = descriptors.ToArray();
        var conflict = materializedDescriptors
            .GroupBy(static descriptor => descriptor.ResourceType)
            .FirstOrDefault(static group => group.Select(descriptor => descriptor.ImplementationType).Distinct().Skip(1).Any());
        if (conflict is not null)
            throw new InvalidOperationException(
                $"Conflicting Agent resource implementations were declared for '{conflict.Key}': " +
                string.Join(", ", conflict.Select(static descriptor => descriptor.ImplementationType.FullName)));

        var resources = new Dictionary<Type, object>();
        try
        {
            foreach (var descriptor in materializedDescriptors)
            {
                if (resources.ContainsKey(descriptor.ResourceType))
                    continue;
                var resource = descriptor.Factory() ??
                    throw new InvalidOperationException($"Agent resource factory for '{descriptor.ResourceType}' returned null.");
                if (!descriptor.ResourceType.IsInstanceOfType(resource))
                    throw new InvalidOperationException($"Agent resource factory for '{descriptor.ResourceType}' returned incompatible type '{resource.GetType()}'.");
                resources.Add(descriptor.ResourceType, resource);
            }
            _resources = resources;
        }
        catch
        {
            DisposeConstructed(resources.Values);
            throw;
        }
    }

    internal IReadOnlyDictionary<Type, object> Resources => _resources;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        List<Exception>? failures = null;
        foreach (var resource in _resources.Values.Reverse())
        {
            try
            {
                if (resource is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (resource is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (failures is { Count: > 0 })
            throw new AggregateException("One or more Agent resources failed to dispose.", failures);
    }

    private static void DisposeConstructed(IEnumerable<object> resources)
    {
        foreach (var resource in resources.Reverse())
        {
            if (resource is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            else if (resource is IDisposable disposable) disposable.Dispose();
        }
    }
}
