using HPD.Graph.Connectors.Abstractions.IO;

namespace HPD.Graph.Connectors.Core.IO;

public interface IArtifactIOManagerRegistry
{
    IReadOnlyList<IArtifactIOManager> List();
    IArtifactIOManager? Find(string name);
    IArtifactIOManager GetRequired(string name);
}

public sealed class ArtifactIOManagerRegistry : IArtifactIOManagerRegistry
{
    private readonly IReadOnlyDictionary<string, IArtifactIOManager> _managers;

    public ArtifactIOManagerRegistry(IEnumerable<IArtifactIOManager> managers)
    {
        ArgumentNullException.ThrowIfNull(managers);
        _managers = managers.ToDictionary(static manager => manager.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<IArtifactIOManager> List() =>
        _managers.Values
            .OrderBy(static manager => manager.Name, StringComparer.Ordinal)
            .ToArray();

    public IArtifactIOManager? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _managers.TryGetValue(name, out var manager);
        return manager;
    }

    public IArtifactIOManager GetRequired(string name)
    {
        return Find(name)
            ?? throw new KeyNotFoundException($"Artifact IO manager '{name}' is not registered.");
    }
}
