using System.Collections.Immutable;

namespace HPD.Gateway.ControlPlane;

public sealed class GatewayBackupSinkRegistry
{
    private readonly ImmutableDictionary<string, IGatewayBackupSink> _sinks;

    public GatewayBackupSinkRegistry(IEnumerable<IGatewayBackupSink> sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        var values = new List<IGatewayBackupSink>(64);
        using IEnumerator<IGatewayBackupSink> enumerator = sinks.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (values.Count == 64)
                throw new InvalidOperationException("The Gateway backup sink catalog exceeds its bound.");
            IGatewayBackupSink value = enumerator.Current
                ?? throw new InvalidOperationException("The Gateway backup sink catalog contains null.");
            if (!ValidName(value.Name))
                throw new InvalidOperationException("The Gateway backup sink catalog contains an invalid name.");
            values.Add(value);
        }
        try { _sinks = values.ToImmutableDictionary(static value => value.Name, StringComparer.Ordinal); }
        catch (ArgumentException) { throw new InvalidOperationException("The Gateway backup sink catalog contains duplicate names."); }
    }

    public bool TryGet(string name, out IGatewayBackupSink? sink) => _sinks.TryGetValue(name, out sink);

    public static bool ValidName(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(static character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.');
}
