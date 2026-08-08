using System.Collections.Immutable;

namespace HPD.Gateway.Admin;

public sealed record GatewayBackupArtifact(string PublicReference, Stream Destination);

public interface IGatewayBackupSink
{
    string Name { get; }
    ValueTask<GatewayBackupArtifact> OpenAsync(string? artifactLabel, CancellationToken cancellationToken = default);
}

public sealed class GatewayBackupSinkRegistry
{
    private readonly ImmutableDictionary<string, IGatewayBackupSink> _sinks;

    public GatewayBackupSinkRegistry(IEnumerable<IGatewayBackupSink> sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        IGatewayBackupSink[] values = sinks.ToArray();
        if (values.Length > 64 || values.Any(static value => value is null || !ValidName(value.Name)))
            throw new InvalidOperationException("The Gateway backup sink catalog is invalid.");
        try { _sinks = values.ToImmutableDictionary(static value => value.Name, StringComparer.Ordinal); }
        catch (ArgumentException) { throw new InvalidOperationException("The Gateway backup sink catalog contains duplicate names."); }
    }

    public bool TryGet(string name, out IGatewayBackupSink? sink) => _sinks.TryGetValue(name, out sink);

    internal static bool ValidName(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(static character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.');
}
