using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

internal interface IBaseSerializerMetadataSource
{
    IReadOnlyList<JsonTypeInfo> Roots { get; }
    bool Generated { get; }
    BaseSerializerContextRegistration? Registration { get; }
    IReadOnlyList<Type> RootTypes { get; }
    IReadOnlyList<BaseSerializerPropertyDeclaration>? SerializerDeclarations { get; }
    void Bind(BaseSerializerMetadataOwner owner);
    CollectionDefinition? CollectionDefinition { get; }
}

internal sealed class BaseSerializerMetadataOwner
{
    private readonly IReadOnlyDictionary<Type, JsonSerializerContext> _contexts;
    private BaseSerializerMetadataOwner(IReadOnlyDictionary<Type, JsonSerializerContext> contexts) => _contexts = contexts;
    internal int ContextCount => _contexts.Count;

    internal static BaseSerializerMetadataOwner Create(IEnumerable<IBaseSerializerMetadataSource> sourceEnumerable)
    {
        IBaseSerializerMetadataSource[] sources = sourceEnumerable.ToArray();
        Dictionary<Type, JsonSerializerContext> contexts = sources.Where(static source => source.Registration is not null)
            .Select(static source => source.Registration!).GroupBy(static registration => registration.ContextType)
            .ToDictionary(static group => group.Key, static group => group.First().CreateOwned());
        var roots = new List<(JsonTypeInfo Info, IReadOnlyList<BaseSerializerPropertyDeclaration>? Declarations)>();
        foreach (IBaseSerializerMetadataSource source in sources)
        {
            if (source.Registration is null)
                roots.AddRange(source.Roots.Select(info => (info, source.SerializerDeclarations)));
            else
            {
                JsonSerializerContext context = contexts[source.Registration.ContextType];
                roots.AddRange(source.RootTypes.Select(type => (
                    context.GetTypeInfo(type) ?? throw new InvalidOperationException("base.schema.serializer.metadataInvalid"),
                    source.SerializerDeclarations)));
            }
        }
        var owner = new BaseSerializerMetadataOwner(contexts);
        var contracts = new Dictionary<Type, string>();
        foreach ((JsonTypeInfo root, IReadOnlyList<BaseSerializerPropertyDeclaration>? declarations) in roots
                     .OrderBy(static item => item.Info.Type.FullName, StringComparer.Ordinal))
        {
            foreach (JsonTypeInfo reachable in BaseSerializerContract.Reachable(root))
            {
                if (reachable.Kind != JsonTypeInfoKind.Object || reachable.Type == typeof(System.Text.Json.JsonElement))
                    continue;
                string fingerprint = BaseSerializerContract.GraphFingerprint(reachable, declarations);
                if (contracts.TryGetValue(reachable.Type, out string? existing) &&
                    !string.Equals(existing, fingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("base.schema.serializer.contextContractAmbiguous");
                contracts[reachable.Type] = fingerprint;
            }
        }
        foreach (IBaseSerializerMetadataSource source in sources) source.Bind(owner);
        return owner;
    }

    internal JsonTypeInfo<T> Resolve<T>(BaseCollection<T> collection)
    {
        BaseSerializerContextRegistration? registration = ((IBaseSerializerMetadataSource)collection).Registration;
        if (registration is null) return collection.JsonTypeInfo;
        return _contexts[registration.ContextType].GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
            ?? throw new InvalidOperationException("base.schema.serializer.metadataInvalid");
    }

    internal JsonTypeInfo Resolve(IBaseSerializerMetadataSource source, Type type)
    {
        if (source.Registration is null)
            return source.Roots.Single(root => root.Type == type);
        return _contexts[source.Registration.ContextType].GetTypeInfo(type)
            ?? throw new InvalidOperationException("base.schema.serializer.metadataInvalid");
    }
}
