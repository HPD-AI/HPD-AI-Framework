using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Schema;

public sealed class RequiredNullableSemanticsTests
{
    [Fact]
    public void RequiredFalseNullableFalseIsRepresentable()
    {
        var field = new FieldDefinition
        {
            Id = "slug",
            ApplicationName = "slug", WireName = "slug",
            Type = BaseFieldTypes.String,
            Required = false,
            Nullable = false
        };

        Assert.False(field.Required);
        Assert.False(field.Nullable);
    }
}
