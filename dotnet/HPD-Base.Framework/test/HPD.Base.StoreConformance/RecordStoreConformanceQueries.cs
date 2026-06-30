namespace HPD.Base.StoreConformance;

public static class RecordStoreConformanceQueries
{
    public static RecordQuery Empty => new() { Count = QueryCountMode.None };

    public static FilterExpression Equal(string field, string value) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = field,
        Operator = FilterOperator.Equal,
        Value = String(value)
    };

    public static FilterExpression UnsupportedLike(string field, string value) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = field,
        Operator = FilterOperator.Like,
        Value = String(value)
    };

    public static QueryValue String(string value) => new()
    {
        Kind = QueryValueKind.String,
        String = value
    };

    public static QueryValue Integer(long value) => new()
    {
        Kind = QueryValueKind.Integer,
        Integer = value
    };

    public static QueryValue Boolean(bool value) => new()
    {
        Kind = QueryValueKind.Boolean,
        Boolean = value
    };

    public static QueryValue Null => new()
    {
        Kind = QueryValueKind.Null
    };
}
