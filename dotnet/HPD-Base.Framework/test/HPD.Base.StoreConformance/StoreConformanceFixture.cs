namespace HPD.Base.StoreConformance;

/// <summary>
/// Creates stores for reusable HPD.BASE store conformance tests.
/// </summary>
public abstract class StoreConformanceFixture
{
    /// <summary>
    /// Creates a new isolated store instance.
    /// </summary>
    public abstract IRecordStore CreateStore();

    /// <summary>
    /// Creates a loose document collection for conformance tests.
    /// </summary>
    public virtual CollectionDefinition CreateCollection(string id = "items") => new()
    {
        Id = id,
        Name = id,
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };
}
