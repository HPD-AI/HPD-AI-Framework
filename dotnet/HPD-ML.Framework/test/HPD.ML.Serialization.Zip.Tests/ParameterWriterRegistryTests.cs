namespace HPD.ML.Serialization.Zip.Tests;

public class ParameterWriterRegistryTests
{
    [Fact]
    public void Register_And_GetWriter_ReturnsWriter()
    {
        var registry = new ParameterWriterRegistry();
        registry.Register(new TestParameterWriter());

        var parameters = new TestParameters { Weights = [1.0, 2.0], Bias = 0.5 };
        var writer = registry.GetWriter(parameters);
        Assert.NotNull(writer);
        Assert.Equal(nameof(TestParameters), writer.TypeName);
    }

    [Fact]
    public void GetWriter_Unregistered_ReturnsNull()
    {
        var registry = new ParameterWriterRegistry();
        var parameters = new TestParameters();
        Assert.Null(registry.GetWriter(parameters));
    }

    [Fact]
    public void GetWriterByTypeName_Works()
    {
        var registry = new ParameterWriterRegistry();
        registry.Register(new TestParameterWriter());

        Assert.NotNull(registry.GetWriterByTypeName(nameof(TestParameters)));
        Assert.Null(registry.GetWriterByTypeName("NonExistent"));
    }
}
