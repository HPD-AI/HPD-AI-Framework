using HPD.RAG.Evaluation;
using Xunit;

namespace HPD.RAG.Handlers.Tests.Evaluation;

/// <summary>
/// Tests T-114 and T-115 — MragEvalPartitionDefinition.FromTestSuite.
/// Note: PartitionKey uses .Segments (not .Dimensions — the spec had a typo).
/// </summary>
public sealed class PartitionKeyTests
{
    [Fact] // T-114
    public void FromTestSuite_ProducesCorrectPartitionKeys()
    {
        var testSuite = new[]
        {
            new TestCase { ScenarioName = "s1", IterationName = "i1" },
            new TestCase { ScenarioName = "s2", IterationName = "i2" }
        };

        var keys = MragEvalPartitionDefinition.FromTestSuite(testSuite).ToList();

        Assert.Equal(2, keys.Count);

        // First key
        Assert.Equal(2, keys[0].Segments.Count);
        Assert.Equal("s1", keys[0].Segments[0]);
        Assert.Equal("i1", keys[0].Segments[1]);

        // Second key
        Assert.Equal(2, keys[1].Segments.Count);
        Assert.Equal("s2", keys[1].Segments[0]);
        Assert.Equal("i2", keys[1].Segments[1]);
    }

    [Fact] // T-115
    public void FromTestSuite_EmptyInput_ProducesEmptyEnumerable()
    {
        var keys = MragEvalPartitionDefinition.FromTestSuite(Array.Empty<TestCase>());

        Assert.False(keys.Any());
    }

    [Fact]
    public void PartitionKey_ToString_UsesSlashSeparator()
    {
        var key = new PartitionKey(["scenario-1", "iteration-1"]);
        Assert.Equal("scenario-1/iteration-1", key.ToString());
    }

    [Fact]
    public void PartitionKey_Equals_SameSegments_ReturnsTrue()
    {
        var a = new PartitionKey(["s1", "i1"]);
        var b = new PartitionKey(["s1", "i1"]);
        Assert.Equal(a, b);
    }
}
