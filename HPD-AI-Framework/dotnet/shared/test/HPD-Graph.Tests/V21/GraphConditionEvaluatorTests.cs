using FluentAssertions;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Orchestration;

namespace HPD.Graph.Tests.V21;

public sealed class GraphConditionEvaluatorTests
{
    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(11, 10, true)]
    [InlineData(9, 10, false)]
    public void Evaluate_FieldGreaterThanOrEqual_UsesInclusiveNumericComparison(int actual, int threshold, bool expected)
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldGreaterThanOrEqual,
            Field = "score",
            Value = threshold
        };

        var outputs = new Dictionary<string, object> { ["score"] = actual };

        ConditionEvaluator.Evaluate(condition, outputs).Should().Be(expected);
    }

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(9, 10, true)]
    [InlineData(11, 10, false)]
    public void Evaluate_FieldLessThanOrEqual_UsesInclusiveNumericComparison(int actual, int threshold, bool expected)
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldLessThanOrEqual,
            Field = "score",
            Value = threshold
        };

        var outputs = new Dictionary<string, object> { ["score"] = actual };

        ConditionEvaluator.Evaluate(condition, outputs).Should().Be(expected);
    }

    [Fact]
    public void Evaluate_InclusiveComparisons_UnwrapJsonElementNumbers()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{"score":10}""");
        var outputs = new Dictionary<string, object>
        {
            ["score"] = document.RootElement.GetProperty("score").Clone()
        };

        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldGreaterThanOrEqual,
            Field = "score",
            Value = 10
        };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void EdgeCondition_GetDescription_DescribesInclusiveComparisons()
    {
        new EdgeCondition
        {
            Type = ConditionType.FieldGreaterThanOrEqual,
            Field = "score",
            Value = 10
        }.GetDescription().Should().Be("score >= 10");

        new EdgeCondition
        {
            Type = ConditionType.FieldLessThanOrEqual,
            Field = "score",
            Value = 10
        }.GetDescription().Should().Be("score <= 10");
    }
}
