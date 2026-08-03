using System.Reflection;
using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Records;

public sealed class CollectionMutationModeContractTests
{
    [Theory]
    [InlineData(BaseCollectionMutationMode.Mutable, true, true, true, true, true)]
    [InlineData(BaseCollectionMutationMode.AppendOnly, true, false, false, true, false)]
    [InlineData(BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge, true, false, false, true, false)]
    [InlineData(BaseCollectionMutationMode.ReadOnly, false, false, false, false, false)]
    public void OperationProjectionIsDerivedFromMutationMode(
        BaseCollectionMutationMode mode,
        bool create,
        bool patch,
        bool replace,
        bool upsert,
        bool delete)
    {
        var collection = new CollectionDefinition
        {
            Id = "items",
            Name = "items",
            Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Strict,
            UnknownFields = UnknownFieldPolicy.Reject,
            MutationMode = mode,
        };

        Assert.Equal(create, collection.Operations.Create);
        Assert.Equal(patch, collection.Operations.Patch);
        Assert.Equal(replace, collection.Operations.Replace);
        Assert.Equal(upsert, collection.Operations.Upsert);
        Assert.Equal(delete, collection.Operations.Delete);
    }

    [Fact]
    public void ObsoleteIndependentAuthoringSurfaceIsAbsent()
    {
        Assert.Null(typeof(CollectionDefinition).GetProperty("ReadOnly"));
        Assert.Null(typeof(CollectionDefinition).GetProperty("ReadOnlyReason"));
        Assert.False(typeof(CollectionDefinition).GetProperty(nameof(CollectionDefinition.Operations))!.CanWrite);
        Assert.Empty(typeof(CollectionOperationMatrix).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void PaginationUsesExplicitGuaranteeInsteadOfBoolean()
    {
        PropertyInfo cursor = typeof(PaginationCapability).GetProperty(nameof(PaginationCapability.Cursor))!;
        Assert.Equal(typeof(QueryCursorGuarantee), cursor.PropertyType);
        Assert.Equal(
            ["None", "Seek", "StableHistory"],
            Enum.GetNames<QueryCursorGuarantee>());
    }
}
