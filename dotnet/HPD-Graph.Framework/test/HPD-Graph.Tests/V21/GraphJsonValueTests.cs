using FluentAssertions;
using HPD.Graph.Abstractions.Serialization;
using HPD.Graph.Core.Builders;

namespace HPD.Graph.Tests.V21;

public sealed class GraphJsonValueTests
{
    [Fact]
    public void ToJsonElement_WritesSupportedGraphValuesWithoutReflection()
    {
        var element = GraphJsonValue.ToJsonElement(
            new Dictionary<string, object?>
            {
                ["name"] = "orders",
                ["count"] = 3,
                ["active"] = true,
                ["tags"] = new[] { "daily", "gold" },
                ["nested"] = new Dictionary<string, object?>
                {
                    ["mode"] = GraphJsonValueMode.Fast
                }
            },
            "test value");

        element.GetProperty("name").GetString().Should().Be("orders");
        element.GetProperty("count").GetInt32().Should().Be(3);
        element.GetProperty("active").GetBoolean().Should().BeTrue();
        element.GetProperty("tags").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("daily", "gold");
        element.GetProperty("nested").GetProperty("mode").GetString().Should().Be("Fast");
    }

    [Fact]
    public void ToJsonElement_ThrowsClearlyForUnsupportedCustomObjects()
    {
        var act = () => GraphJsonValue.ToJsonElement(new UnsupportedGraphJsonValue(), "custom");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unsupported type*Native AOT-safe graph JSON values*");
    }

    [Fact]
    public void GraphBuilder_ConfigRejectsUnsupportedCustomObjects()
    {
        var act = () => new GraphBuilder()
            .WithName("unsupported")
            .AddHandlerNode("work", "Work", "handler", node => node
                .WithConfig("custom", new UnsupportedGraphJsonValue()))
            .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*node config.custom*unsupported type*");
    }

    private enum GraphJsonValueMode
    {
        Fast
    }

    private sealed class UnsupportedGraphJsonValue
    {
        public string Name { get; init; } = "custom";
    }
}
