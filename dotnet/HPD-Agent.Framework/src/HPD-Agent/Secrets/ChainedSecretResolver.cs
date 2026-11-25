namespace HPD.Agent.Secrets;

/// <summary>
/// Chains multiple resolvers in priority order. First non-null result wins.
/// This is the default resolver wired up by AgentBuilder.
/// </summary>
public sealed class ChainedSecretResolver : ISecretResolver, ISecretResolverDiagnostics
{
    private readonly ISecretResolver[] _resolvers;

    public ChainedSecretResolver(params ISecretResolver[] resolvers)
    {
        _resolvers = resolvers;
    }

    public ChainedSecretResolver(IEnumerable<ISecretResolver> resolvers)
    {
        _resolvers = resolvers.ToArray();
    }

    public async ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default)
    {
        foreach (var resolver in _resolvers)
        {
            var result = await resolver.ResolveAsync(key, ct);
            if (result.HasValue)
                return result;
        }

        return null;
    }

    /// <inheritdoc />
    public string? GetSetupGuidance(string key) => _resolvers
        .OfType<ISecretResolverDiagnostics>()
        .Select(resolver => resolver.GetSetupGuidance(key))
        .FirstOrDefault(static guidance => !string.IsNullOrWhiteSpace(guidance));
}
