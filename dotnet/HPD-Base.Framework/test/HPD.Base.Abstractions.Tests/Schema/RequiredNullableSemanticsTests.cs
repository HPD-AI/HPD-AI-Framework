using HPD.Base.Schema;

namespace HPD.Base.Abstractions.Tests.Schema;

public sealed class RequiredNullableSemanticsTests
{
    [Fact]
    public void RequiredFalseNullableFalseIsRepresentable()
    {
        var field = new FieldDefinition
        {
            Id = "slug",
            Name = "slug",
            Type = BaseFieldTypes.String,
            Required = false,
            Nullable = false
        };

        Assert.False(field.Required);
        Assert.False(field.Nullable);
    }
}
