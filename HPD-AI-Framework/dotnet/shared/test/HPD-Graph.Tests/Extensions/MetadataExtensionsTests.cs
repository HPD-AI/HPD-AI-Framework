using FluentAssertions;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Extensions;
using Xunit;

namespace HPDAgent.Graph.Tests.Extensions;

public class MetadataExtensionsTests
{
    private readonly List<Node> _testNodes;

    public MetadataExtensionsTests()
    {
        _testNodes = new List<Node>
        {
            new Node
            {
                Id = "node1",
                Name = "ML Trainer",
                Type = NodeType.Handler,
                Metadata = new Dictionary<string, string>
                {
                    ["team"] = "ml",
                    ["cost-center"] = "ml-infra",
                    ["priority"] = "high"
                }
            },
            new Node
            {
                Id = "node2",
                Name = "ML Server",
                Type = NodeType.Handler,
                Metadata = new Dictionary<string, string>
                {
                    ["team"] = "ml",
                    ["cost-center"] = "ml-infra",
                    ["priority"] = "medium"
                }
            },
            new Node
            {
                Id = "node3",
                Name = "Batch Processor",
                Type = NodeType.Handler,
                Metadata = new Dictionary<string, string>
                {
                    ["team"] = "data",
                    ["cost-center"] = "data-platform",
                    ["priority"] = "low"
                }
            },
            new Node
            {
                Id = "node4",
                Name = "API Handler",
                Type = NodeType.Handler,
                Metadata = new Dictionary<string, string>
                {
                    ["team"] = "backend",
                    ["priority"] = "high"
                }
            }
        };
    }

    [Fact]
    public void WithMetadata_FiltersByKey_ReturnsMatchingNodes()
    {
        var result = _testNodes.WithMetadata("cost-center");

        result.Should().HaveCount(3);
        result.Should().Contain(n => n.Id == "node1");
        result.Should().Contain(n => n.Id == "node2");
        result.Should().Contain(n => n.Id == "node3");
    }

    [Fact]
    public void WithMetadata_FiltersByKeyValue_ReturnsMatchingNodes()
    {
        var result = _testNodes.WithMetadata("team", "ml");

        result.Should().HaveCount(2);
        result.Should().Contain(n => n.Id == "node1");
        result.Should().Contain(n => n.Id == "node2");
    }

    [Fact]
    public void WithMetadata_NoMatches_ReturnsEmptyList()
    {
        var result = _testNodes.WithMetadata("team", "nonexistent");

        result.Should().BeEmpty();
    }

    [Fact]
    public void WithMetadataMatching_FiltersByPredicate_ReturnsMatchingNodes()
    {
        var result = _testNodes.WithMetadataMatching("priority",
            p => p == "high" || p == "critical");

        result.Should().HaveCount(2);
        result.Should().Contain(n => n.Id == "node1");
        result.Should().Contain(n => n.Id == "node4");
    }

    [Fact]
    public void GetMetadata_ExistingKey_ReturnsValue()
    {
        var node = _testNodes.First(n => n.Id == "node1");
        var value = node.GetMetadata("team");

        value.Should().Be("ml");
    }

    [Fact]
    public void GetMetadata_NonExistingKey_ReturnsNull()
    {
        var node = _testNodes.First(n => n.Id == "node1");
        var value = node.GetMetadata("nonexistent");

        value.Should().BeNull();
    }

    [Fact]
    public void GetMetadataWithParser_ExistingKey_ReturnsParsedValue()
    {
        var node = new Node
        {
            Id = "node5",
            Name = "Test",
            Type = NodeType.Handler,
            Metadata = new Dictionary<string, string>
            {
                ["max-retries"] = "5"
            }
        };

        var value = node.GetMetadata("max-retries", int.Parse);

        value.Should().Be(5);
    }

    [Fact]
    public void GetMetadataWithParser_NonExistingKey_ReturnsDefault()
    {
        var node = _testNodes.First();
        var value = node.GetMetadata("max-retries", int.Parse);

        value.Should().Be(0); // Default for int
    }

    [Fact]
    public void GetMetadataValues_ReturnsDistinctValues()
    {
        var values = _testNodes.GetMetadataValues("team");

        values.Should().HaveCount(3);
        values.Should().Contain("ml");
        values.Should().Contain("data");
        values.Should().Contain("backend");
    }

    [Fact]
    public void GetMetadataValues_NoMatches_ReturnsEmptyList()
    {
        var values = _testNodes.GetMetadataValues("nonexistent");

        values.Should().BeEmpty();
    }

    [Fact]
    public void GetMetadataValues_DuplicateValues_ReturnsDistinct()
    {
        var priorities = _testNodes.GetMetadataValues("priority");

        priorities.Should().HaveCount(3);
        priorities.Should().Contain("high");
        priorities.Should().Contain("medium");
        priorities.Should().Contain("low");
    }

    [Fact]
    public void ChainedFilters_WorkCorrectly()
    {
        var result = _testNodes
            .WithMetadata("team", "ml")
            .WithMetadataMatching("priority", p => p == "high");

        result.Should().HaveCount(1);
        result.Should().Contain(n => n.Id == "node1");
    }

    [Fact]
    public void WithMetadata_EmptyCollection_ReturnsEmptyList()
    {
        var emptyNodes = new List<Node>();
        var result = emptyNodes.WithMetadata("team");

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetMetadataValues_NodeWithNoMetadata_HandledCorrectly()
    {
        var nodes = new List<Node>
        {
            new Node
            {
                Id = "node1",
                Name = "Test",
                Type = NodeType.Handler,
                Metadata = new Dictionary<string, string> { ["team"] = "ml" }
            },
            new Node
            {
                Id = "node2",
                Name = "Test2",
                Type = NodeType.Handler,
                Metadata = new Dictionary<string, string>() // Empty metadata
            }
        };

        var values = nodes.GetMetadataValues("team");

        values.Should().HaveCount(1);
        values.Should().Contain("ml");
    }
}
