using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

internal interface IBaseSerializerMetadataSource
{
    IReadOnlyList<JsonTypeInfo> Roots { get; }
    bool Generated { get; }
}

internal static class BaseSerializerMetadataOwner
{
    internal static void Validate(IEnumerable<IBaseSerializerMetadataSource> sources)
    {
        var contracts = new Dictionary<Type, string>();
        foreach (JsonTypeInfo root in sources.SelectMany(static source => source.Roots)
                     .OrderBy(static info => info.Type.FullName, StringComparer.Ordinal))
        {
            foreach (JsonTypeInfo reachable in BaseSerializerContract.Reachable(root))
            {
                if (reachable.Kind != JsonTypeInfoKind.Object || reachable.Type == typeof(System.Text.Json.JsonElement))
                    continue;
                string fingerprint = BaseSerializerContract.GraphFingerprint(reachable);
                if (contracts.TryGetValue(reachable.Type, out string? existing) &&
                    !string.Equals(existing, fingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("base.schema.serializer.contextContractAmbiguous");
                contracts[reachable.Type] = fingerprint;
            }
        }
    }
}
