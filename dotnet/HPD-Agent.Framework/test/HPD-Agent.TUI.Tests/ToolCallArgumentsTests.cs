using FluentAssertions;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.TUI.Tests;

public sealed class ToolCallArgumentsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    public void Parse_InvalidJson_ReturnsBoundedFailure(string json)
    {
        var arguments = ToolCallArguments.Parse(json);

        arguments.IsValidJson.Should().BeFalse();
        arguments.IsObject.Should().BeFalse();
        arguments.Diagnostic.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parse_ValidNonObject_ReportsActualRootKind()
    {
        var arguments = ToolCallArguments.Parse("\"launch\"");

        arguments.IsValidJson.Should().BeTrue();
        arguments.IsObject.Should().BeFalse();
        arguments.Root.Kind.Should().Be(ToolArgumentKind.String);
    }

    [Fact]
    public void TryGetObject_OnWrongKind_ReturnsFalseWithoutThrowing()
    {
        var arguments = ToolCallArguments.Parse("""{"request":"launch"}""");

        arguments.Root.TryGetObject("request", out _).Should().BeFalse();
        arguments.Root.TryGetString("request", out var request).Should().BeTrue();
        request.Should().Be("launch");
    }

    [Fact]
    public void NestedAccessors_ReadOnlyMatchingKinds()
    {
        var arguments = ToolCallArguments.Parse(
            """{"request":{"action":"launch","recursive":true,"limit":25}}""");

        arguments.Root.TryGetObject("request", out var request).Should().BeTrue();
        request.TryGetString("action", out var action).Should().BeTrue();
        request.TryGetBoolean("recursive", out var recursive).Should().BeTrue();
        request.TryGetInt32("limit", out var limit).Should().BeTrue();
        request.TryGetProperty("limit", out var limitValue).Should().BeTrue();
        request.TryGetInt32("action", out _).Should().BeFalse();

        action.Should().Be("launch");
        recursive.Should().BeTrue();
        limit.Should().Be(25);
        limitValue.Kind.Should().Be(ToolArgumentKind.Number);
    }
}
