namespace HPD.Base;

internal sealed class BaseCollectionRegistry(IReadOnlyDictionary<string, CollectionDefinition> collections)
{
    internal IReadOnlyDictionary<string, CollectionDefinition> Collections { get; } = collections;
}
