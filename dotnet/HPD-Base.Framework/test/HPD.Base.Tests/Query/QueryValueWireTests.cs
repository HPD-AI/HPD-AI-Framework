using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Query;

public sealed class QueryValueWireTests
{
    [Fact]
    public void DecimalValuesAreStringBacked()
    {
        Assert.Equal(typeof(string), typeof(QueryValue).GetProperty(nameof(QueryValue.Decimal))!.PropertyType);
    }
}
