using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

internal interface IBaseSerializerMetadataSource
{
    IReadOnlyList<JsonTypeInfo> Roots { get; }
    bool Generated { get; }
    BaseSerializerContextRegistration? Registration { get; }
    IReadOnlyList<Type> RootTypes { get; }
}

internal sealed class BaseSerializerMetadataOwner
{
    private readonly JsonSerializerContext[] _contexts;
    private BaseSerializerMetadataOwner(JsonSerializerContext[] contexts) => _contexts = contexts;

    internal static BaseSerializerMetadataOwner Create(IEnumerable<IBaseSerializerMetadataSource> sourceEnumerable)
    {
        IBaseSerializerMetadataSource[] sources = sourceEnumerable.ToArray();
        JsonSerializerContext[] contexts = sources.Where(static source => source.Registration is not null)
            .Select(static source => source.Registration!.CreateOwned()).ToArray();
        var roots = new List<JsonTypeInfo>();
        int contextIndex = 0;
        foreach (IBaseSerializerMetadataSource source in sources)
        {
            if (source.Registration is null) roots.AddRange(source.Roots);
            else
            {
                JsonSerializerContext context = contexts[contextIndex++];
                roots.AddRange(source.RootTypes.Select(type => context.GetTypeInfo(type)
                    ?? throw new InvalidOperationException("base.schema.serializer.metadataInvalid")));
            }
        }
        var contracts = new Dictionary<Type, string>();
        foreach (JsonTypeInfo root in roots
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
        return new BaseSerializerMetadataOwner(contexts);
    }
}
