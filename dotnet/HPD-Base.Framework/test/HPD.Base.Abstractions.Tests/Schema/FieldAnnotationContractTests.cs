using HPD.Base.Schema;

namespace HPD.Base.Abstractions.Tests.Schema;

public sealed class FieldAnnotationContractTests
{
    [Fact]
    public void FieldTypeAndFormatRemainStringRegistries()
    {
        Assert.Equal(typeof(string), typeof(FieldDefinition).GetProperty(nameof(FieldDefinition.Type))!.PropertyType);
        Assert.Equal(typeof(string), typeof(FieldDefinition).GetProperty(nameof(FieldDefinition.Format))!.PropertyType);
    }
}
