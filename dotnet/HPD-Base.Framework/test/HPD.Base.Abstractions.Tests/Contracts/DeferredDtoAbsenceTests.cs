namespace HPD.Base.Abstractions.Tests.Contracts;

public sealed class DeferredDtoAbsenceTests
{
    [Theory]
    [InlineData("HPD.Base.Records.RecordUpsertRequest")]
    [InlineData("HPD.Base.Stores.IUpsertRecordStore")]
    [InlineData("HPD.Base.Stores.IBatchRecordStore")]
    [InlineData("HPD.Base.Stores.ITransactionalRecordStore")]
    [InlineData("HPD.Base.Stores.INativePolicyRecordStore")]
    [InlineData("HPD.Base.Stores.IRelationalIncludeRecordStore")]
    [InlineData("HPD.Base.Stores.ISearchRecordStore")]
    [InlineData("HPD.Base.Stores.IVectorRecordStore")]
    [InlineData("HPD.Base.Events.IRecordStoreEventFeed")]
    public void DeferredTypesAreAbsent(string fullName)
    {
        var type = typeof(RecordId).Assembly.GetType(fullName, throwOnError: false);

        Assert.Null(type);
    }
}
