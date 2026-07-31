using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Schema;

public sealed class FieldAnnotationContractTests
{
    [Fact]
    public void FieldTypeAndFormatRemainStringRegistries()
    {
        Assert.Equal(typeof(string), typeof(FieldDefinition).GetProperty(nameof(FieldDefinition.Type))!.PropertyType);
        Assert.Equal(typeof(string), typeof(FieldDefinition).GetProperty(nameof(FieldDefinition.Format))!.PropertyType);
    }
}
