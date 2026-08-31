using System.Text.Json.Serialization;

namespace HPD.Agent.Tests.Tools;

public sealed class ReflectionActionContractTests
{
    [Fact]
    public void ReflectionRegistrationRejectsAnnotatedDerivedTypeOutsideUnion()
    {
        Assert.True(ReflectionToolFactory.TryCreateToolHarnessFactory(
            typeof(InvalidReflectionHarness), out var factory, out var error));
        Assert.Null(error);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.CreateFunctions(new InvalidReflectionHarness(), null, null));

        Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
    [JsonDerivedType(typeof(DeclaredReflectionAction), "declared")]
    public abstract record ReflectionRequest;

    [AIFunctionAction("declared")]
    public sealed record DeclaredReflectionAction(string Value) : ReflectionRequest;

    [AIFunctionAction("hidden")]
    public sealed record HiddenReflectionAction(string Value) : ReflectionRequest;

    [Collapse("Invalid reflection action union", FunctionResult = "ok")]
    public sealed class InvalidReflectionHarness
    {
        [AIFunction]
        public string Execute(ReflectionRequest request) => "ok";
    }
}
