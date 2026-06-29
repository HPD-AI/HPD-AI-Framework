using HPD.Base.Query;

namespace HPD.Base.Abstractions.Tests.Query;

public sealed class FilterExpressionInvariantTests
{
    [Fact]
    public void FilterExpressionUsesOneRecordDiscriminatorShape()
    {
        var filter = new FilterExpression
        {
            Kind = FilterNodeKind.Compare,
            Field = "title",
            Operator = FilterOperator.Equal,
            Value = new QueryValue { Kind = QueryValueKind.String, String = "hello" }
        };

        Assert.Equal(FilterNodeKind.Compare, filter.Kind);
        Assert.Equal("title", filter.Field);
    }
}
