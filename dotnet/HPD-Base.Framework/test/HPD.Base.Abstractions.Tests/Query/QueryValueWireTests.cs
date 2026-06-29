using HPD.Base.Query;

namespace HPD.Base.Abstractions.Tests.Query;

public sealed class QueryValueWireTests
{
    [Fact]
    public void DecimalValuesAreStringBacked()
    {
        Assert.Equal(typeof(string), typeof(QueryValue).GetProperty(nameof(QueryValue.Decimal))!.PropertyType);
    }
}
