using System.Text.Json;

namespace HPD.Agent.Tests.Tools;

public sealed class HPDGeneratedToolArgumentBinderTests
{
    [Fact]
    public void ValidateProperties_RejectsUnknownAlias()
    {
        using var document = JsonDocument.Parse("""{"targetPath":"ok","targetpath":"alias"}""");

        var exception = Assert.Throws<HPDToolArgumentException>(() =>
            HPDGeneratedToolArgumentBinder.ValidateProperties(document.RootElement, "request", "targetPath"));

        Assert.Equal("request.targetpath", exception.PropertyName);
        Assert.Equal("unknown_property", exception.ErrorCode);
    }

    [Fact]
    public void GetRequiredProperty_RejectsDuplicateCanonicalName()
    {
        using var document = JsonDocument.Parse("""{"target":"one","target":"two"}""");

        var exception = Assert.Throws<HPDToolArgumentException>(() =>
            HPDGeneratedToolArgumentBinder.GetRequiredProperty(document.RootElement, "target", "request"));

        Assert.Equal("request.target", exception.PropertyName);
        Assert.Equal("duplicate_property", exception.ErrorCode);
    }

    [Fact]
    public void ScalarBinders_DoNotCoerceJsonKinds()
    {
        using var stringNumber = JsonDocument.Parse("\"42\"");
        using var numericBoolean = JsonDocument.Parse("1");

        Assert.Equal(
            "invalid_json_kind",
            Assert.Throws<HPDToolArgumentException>(() =>
                HPDGeneratedToolArgumentBinder.BindInt32(stringNumber.RootElement, "count")).ErrorCode);
        Assert.Equal(
            "invalid_json_kind",
            Assert.Throws<HPDToolArgumentException>(() =>
                HPDGeneratedToolArgumentBinder.BindBoolean(numericBoolean.RootElement, "enabled")).ErrorCode);
    }
}
