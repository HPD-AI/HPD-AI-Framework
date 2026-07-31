namespace HPD.Base.Tests.Abstractions.Contracts;

public sealed class DeferredDtoAbsenceTests
{
    [Theory]
    [InlineData("HPD.Base.IUpsertRecordStore")]
    [InlineData("HPD.Base.IBatchRecordStore")]
    [InlineData("HPD.Base.ITransactionalRecordStore")]
    [InlineData("HPD.Base.IRevisionedRecordStore")]
    [InlineData("HPD.Base.CrudCapability")]
    [InlineData("HPD.Base.StoreCrudCapabilityConstraints")]
    [InlineData("HPD.Base.INativePolicyRecordStore")]
    [InlineData("HPD.Base.IRelationalIncludeRecordStore")]
    [InlineData("HPD.Base.ISearchRecordStore")]
    [InlineData("HPD.Base.IVectorRecordStore")]
    [InlineData("HPD.Base.IRecordStoreEventFeed")]
    public void DeferredTypesAreAbsent(string fullName)
    {
        var type = typeof(RecordId).Assembly.GetType(fullName, throwOnError: false);

        Assert.Null(type);
    }
}
