using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Schema;

public sealed class RequiredNullableSemanticsTests
{
    [Fact]
    public void OptionalNonNullableIsRepresentable()
    {
        var field = new FieldDefinition
        {
            Id = "slug",
            ApplicationName = "slug", WireName = "slug",
            Type = BaseFieldTypes.String,
            Presence = BaseFieldPresence.Optional,
            Nullability = BaseFieldNullability.NonNullable
        };

        Assert.Equal(BaseFieldPresence.Optional, field.Presence);
        Assert.Equal(BaseFieldNullability.NonNullable, field.Nullability);
    }
}
