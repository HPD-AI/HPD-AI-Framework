using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Results;

public sealed class ErrorRedactionContractTests
{
    [Fact]
    public void StoreErrorsCarryNativeCodesOnlyInDedicatedInfo()
    {
        Assert.NotNull(typeof(BaseError).GetProperty(nameof(BaseError.Store)));
        Assert.NotNull(typeof(StoreErrorInfo).GetProperty(nameof(StoreErrorInfo.NativeCode)));
        Assert.NotNull(typeof(StoreErrorInfo).GetProperty(nameof(StoreErrorInfo.NativeSubcode)));
        Assert.NotNull(typeof(StoreErrorInfo).GetProperty(nameof(StoreErrorInfo.NativeMessage)));
    }
}
