using System.Text.Json;
using FluentAssertions;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Routing;

/// <summary>
/// Tests for conditional edge routing and condition evaluation.
/// </summary>
public class ConditionalRoutingTests
{
    #region Basic Condition Tests

    [Fact]
    public void Evaluate_AlwaysCondition_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition { Type = ConditionType.Always };
        var outputs = new Dictionary<string, object> { ["key"] = "value" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NullCondition_ReturnsTrue()
    {
        // Arrange
        EdgeCondition? condition = null;
        var outputs = new Dictionary<string, object> { ["key"] = "value" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert - Null condition means unconditional edge
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldEquals_MatchingValue_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "status",
            Value = "success"
        };
        var outputs = new Dictionary<string, object> { ["status"] = "success" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldEquals_DifferentValue_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "status",
            Value = "success"
        };
        var outputs = new Dictionary<string, object> { ["status"] = "failure" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldEquals_NumericValues_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "count",
            Value = 42
        };
        var outputs = new Dictionary<string, object> { ["count"] = 42 };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldNotEquals_DifferentValue_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldNotEquals,
            Field = "status",
            Value = "success"
        };
        var outputs = new Dictionary<string, object> { ["status"] = "failure" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldNotEquals_SameValue_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldNotEquals,
            Field = "status",
            Value = "success"
        };
        var outputs = new Dictionary<string, object> { ["status"] = "success" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Field Existence Tests

    [Fact]
    public void Evaluate_FieldExists_NonNullField_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldExists,
            Field = "data"
        };
        var outputs = new Dictionary<string, object> { ["data"] = "some value" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldExists_NullField_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldExists,
            Field = "data"
        };
        var outputs = new Dictionary<string, object> { ["data"] = null! };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldExists_MissingField_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldExists,
            Field = "data"
        };
        var outputs = new Dictionary<string, object> { ["other"] = "value" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldNotExists_MissingField_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldNotExists,
            Field = "data"
        };
        var outputs = new Dictionary<string, object> { ["other"] = "value" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldNotExists_NullField_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldNotExists,
            Field = "data"
        };
        var outputs = new Dictionary<string, object> { ["data"] = null! };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Comparison Tests - Numbers

    [Fact]
    public void Evaluate_FieldGreaterThan_Numbers_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldGreaterThan,
            Field = "score",
            Value = 50
        };
        var outputs = new Dictionary<string, object> { ["score"] = 75 };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldGreaterThan_Numbers_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldGreaterThan,
            Field = "score",
            Value = 50
        };
        var outputs = new Dictionary<string, object> { ["score"] = 25 };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldLessThan_Numbers_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldLessThan,
            Field = "score",
            Value = 50
        };
        var outputs = new Dictionary<string, object> { ["score"] = 25 };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldLessThan_Numbers_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldLessThan,
            Field = "score",
            Value = 50
        };
        var outputs = new Dictionary<string, object> { ["score"] = 75 };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldGreaterThan_DifferentNumericTypes_Works()
    {
        // Arrange - Comparing int field with long value
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldGreaterThan,
            Field = "count",
            Value = 50L  // long
        };
        var outputs = new Dictionary<string, object> { ["count"] = 100 };  // int

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Comparison Tests - Strings

    [Fact]
    public void Evaluate_FieldGreaterThan_Strings_OrdinalComparison()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldGreaterThan,
            Field = "name",
            Value = "Alice"
        };
        var outputs = new Dictionary<string, object> { ["name"] = "Bob" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert - "Bob" > "Alice" (ordinal comparison)
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldLessThan_Strings_OrdinalComparison()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldLessThan,
            Field = "name",
            Value = "Bob"
        };
        var outputs = new Dictionary<string, object> { ["name"] = "Alice" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert - "Alice" < "Bob" (ordinal comparison)
        result.Should().BeTrue();
    }

    #endregion

    #region Contains Tests

    [Fact]
    public void Evaluate_FieldContains_String_CaseInsensitive_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContains,
            Field = "message",
            Value = "error"
        };
        var outputs = new Dictionary<string, object> { ["message"] = "An ERROR occurred" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert - Case insensitive
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldContains_String_NotFound_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContains,
            Field = "message",
            Value = "error"
        };
        var outputs = new Dictionary<string, object> { ["message"] = "Success" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldContains_Collection_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContains,
            Field = "tags",
            Value = "important"
        };
        var outputs = new Dictionary<string, object>
        {
            ["tags"] = new List<string> { "urgent", "important", "reviewed" }
        };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldContains_Collection_NotFound_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContains,
            Field = "tags",
            Value = "important"
        };
        var outputs = new Dictionary<string, object>
        {
            ["tags"] = new List<string> { "urgent", "reviewed" }
        };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Evaluate_NoOutputs_FieldCondition_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "status",
            Value = "success"
        };
        Dictionary<string, object>? outputs = null;

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert - Can't evaluate field conditions without outputs
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NoOutputs_AlwaysCondition_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition { Type = ConditionType.Always };
        Dictionary<string, object>? outputs = null;

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert - Always condition doesn't need outputs
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_EmptyOutputs_FieldCondition_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "status",
            Value = "success"
        };
        var outputs = new Dictionary<string, object>();

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NullValues_Comparison_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldGreaterThan,
            Field = "score",
            Value = 50
        };
        var outputs = new Dictionary<string, object> { ["score"] = null! };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert - Can't compare null values
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldEquals_BothNull_ReturnsTrue()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "data",
            Value = null
        };
        var outputs = new Dictionary<string, object> { ["data"] = null! };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert - null == null
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_MissingField_ReturnsFalse()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "missing",
            Value = "value"
        };
        var outputs = new Dictionary<string, object> { ["other"] = "value" };

        // Act
        var result = ConditionEvaluator.Evaluate(condition, outputs);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region EdgeCondition GetDescription Tests

    [Fact]
    public void GetDescription_Always_ReturnsCorrectString()
    {
        // Arrange
        var condition = new EdgeCondition { Type = ConditionType.Always };

        // Act
        var description = condition.GetDescription();

        // Assert
        description.Should().Be("Always");
    }

    [Fact]
    public void GetDescription_FieldEquals_ReturnsCorrectString()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "status",
            Value = "success"
        };

        // Act
        var description = condition.GetDescription();

        // Assert
        description.Should().Be("status == success");
    }

    [Fact]
    public void GetDescription_FieldGreaterThan_ReturnsCorrectString()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldGreaterThan,
            Field = "score",
            Value = 50
        };

        // Act
        var description = condition.GetDescription();

        // Assert
        description.Should().Be("score > 50");
    }

    [Fact]
    public void GetDescription_FieldContains_ReturnsCorrectString()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContains,
            Field = "message",
            Value = "error"
        };

        // Act
        var description = condition.GetDescription();

        // Assert
        description.Should().Be("message contains error");
    }

    [Fact]
    public void GetDescription_Default_ReturnsCorrectString()
    {
        // Arrange
        var condition = new EdgeCondition
        {
            Type = ConditionType.Default
        };

        // Act
        var description = condition.GetDescription();

        // Assert
        description.Should().Be("Default (fallback)");
    }

    #endregion

    #region Default Edge Tests

    [Fact]
    public void DefaultEdge_FiresWhenNoRegularConditionsMatch()
    {
        // Arrange
        var condition1 = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "status",
            Value = "low"
        };

        var condition2 = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "status",
            Value = "high"
        };

        var defaultCondition = new EdgeCondition
        {
            Type = ConditionType.Default
        };

        var outputs = new Dictionary<string, object> { ["status"] = "medium" };

        // Act
        var result1 = ConditionEvaluator.Evaluate(condition1, outputs);
        var result2 = ConditionEvaluator.Evaluate(condition2, outputs);

        // Assert
        result1.Should().BeFalse("regular condition should not match");
        result2.Should().BeFalse("regular condition should not match");
        // Default edge should be used when both regular conditions fail
        // This will be tested in integration tests with the orchestrator
    }

    [Fact]
    public void DefaultEdge_DoesNotFireWhenRegularConditionMatches()
    {
        // Arrange
        var regularCondition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "status",
            Value = "success"
        };

        var outputs = new Dictionary<string, object> { ["status"] = "success" };

        // Act
        var regularResult = ConditionEvaluator.Evaluate(regularCondition, outputs);

        // Assert
        regularResult.Should().BeTrue("regular condition should match");
        // Default edge should NOT be used - verified in integration tests
    }

    #endregion

    #region Phase 1 — Compound Logic Tests (And / Or / Not)

    [Fact]
    public void Evaluate_And_BothTrue_ReturnsTrue()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.And,
            Conditions = [
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "a", Value = "x" },
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "b", Value = "y" }
            ]
        };
        var outputs = new Dictionary<string, object> { ["a"] = "x", ["b"] = "y" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_And_OneFalse_ReturnsFalse()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.And,
            Conditions = [
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "a", Value = "x" },
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "b", Value = "y" }
            ]
        };
        var outputs = new Dictionary<string, object> { ["a"] = "x", ["b"] = "WRONG" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_And_EmptyConditions_ReturnsTrue()
    {
        var condition = new EdgeCondition { Type = ConditionType.And, Conditions = [] };
        var outputs = new Dictionary<string, object> { ["x"] = "1" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue("vacuously true");
    }

    [Fact]
    public void Evaluate_Or_OneTrue_ReturnsTrue()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.Or,
            Conditions = [
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "status", Value = "urgent" },
                new EdgeCondition { Type = ConditionType.FieldGreaterThan, Field = "priority", Value = 8 }
            ]
        };
        var outputs = new Dictionary<string, object> { ["status"] = "normal", ["priority"] = 10 };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Or_AllFalse_ReturnsFalse()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.Or,
            Conditions = [
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "status", Value = "urgent" },
                new EdgeCondition { Type = ConditionType.FieldGreaterThan, Field = "priority", Value = 8 }
            ]
        };
        var outputs = new Dictionary<string, object> { ["status"] = "normal", ["priority"] = 5 };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Or_EmptyConditions_ReturnsFalse()
    {
        var condition = new EdgeCondition { Type = ConditionType.Or, Conditions = [] };
        var outputs = new Dictionary<string, object> { ["x"] = "1" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse("vacuously false");
    }

    [Fact]
    public void Evaluate_Not_True_ReturnsFalse()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.Not,
            Conditions = [
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "verified", Value = "true" }
            ]
        };
        var outputs = new Dictionary<string, object> { ["verified"] = "true" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Not_False_ReturnsTrue()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.Not,
            Conditions = [
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "verified", Value = "true" }
            ]
        };
        var outputs = new Dictionary<string, object> { ["verified"] = "false" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NestedAndOr_EvaluatesCorrectly()
    {
        // And(Or(a=="x", b=="y"), Not(c=="z"))
        // outputs: a="x", b="no", c="other" → Or is true (a matches), Not is true (c!="z") → And=true
        var condition = new EdgeCondition
        {
            Type = ConditionType.And,
            Conditions = [
                new EdgeCondition
                {
                    Type = ConditionType.Or,
                    Conditions = [
                        new EdgeCondition { Type = ConditionType.FieldEquals, Field = "a", Value = "x" },
                        new EdgeCondition { Type = ConditionType.FieldEquals, Field = "b", Value = "y" }
                    ]
                },
                new EdgeCondition
                {
                    Type = ConditionType.Not,
                    Conditions = [
                        new EdgeCondition { Type = ConditionType.FieldEquals, Field = "c", Value = "z" }
                    ]
                }
            ]
        };
        var outputs = new Dictionary<string, object> { ["a"] = "x", ["b"] = "no", ["c"] = "other" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_And_ContainingDefault_ThrowsInvalidOperationException()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.And,
            Conditions = [
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "a", Value = "x" },
                new EdgeCondition { Type = ConditionType.Default }
            ]
        };
        var outputs = new Dictionary<string, object> { ["a"] = "x" };

        var act = () => ConditionEvaluator.Evaluate(condition, outputs);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Default*");
    }

    [Fact]
    public void Evaluate_Or_ContainingDefault_ThrowsInvalidOperationException()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.Or,
            Conditions = [
                new EdgeCondition { Type = ConditionType.Default }
            ]
        };
        var outputs = new Dictionary<string, object> { ["a"] = "x" };

        var act = () => ConditionEvaluator.Evaluate(condition, outputs);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Default*");
    }

    [Fact]
    public void Evaluate_Not_ContainingDefault_ThrowsInvalidOperationException()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.Not,
            Conditions = [new EdgeCondition { Type = ConditionType.Default }]
        };
        var outputs = new Dictionary<string, object> { ["a"] = "x" };

        var act = () => ConditionEvaluator.Evaluate(condition, outputs);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Default*");
    }

    [Fact]
    public void GetDescription_And_ReturnsNestedString()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.And,
            Conditions = [
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "a", Value = "x" },
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "b", Value = "y" }
            ]
        };

        condition.GetDescription().Should().Be("(a == x AND b == y)");
    }

    [Fact]
    public void GetDescription_Or_ReturnsNestedString()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.Or,
            Conditions = [
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "status", Value = "urgent" },
                new EdgeCondition { Type = ConditionType.FieldGreaterThan, Field = "priority", Value = 8 }
            ]
        };

        condition.GetDescription().Should().Be("(status == urgent OR priority > 8)");
    }

    [Fact]
    public void GetDescription_Not_ReturnsNestedString()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.Not,
            Conditions = [
                new EdgeCondition { Type = ConditionType.FieldEquals, Field = "verified", Value = true }
            ]
        };

        condition.GetDescription().Should().Be("NOT (verified == True)");
    }

    #endregion

    #region Phase 2 — Advanced String Condition Tests

    [Fact]
    public void Evaluate_FieldStartsWith_Match_ReturnsTrue()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldStartsWith, Field = "intent", Value = "billing/" };
        var outputs = new Dictionary<string, object> { ["intent"] = "billing/general" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldStartsWith_NoMatch_ReturnsFalse()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldStartsWith, Field = "intent", Value = "billing/" };
        var outputs = new Dictionary<string, object> { ["intent"] = "general/billing" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldEndsWith_Match_ReturnsTrue()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldEndsWith, Field = "code", Value = "_billing" };
        var outputs = new Dictionary<string, object> { ["code"] = "code_billing" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldEndsWith_NoMatch_ReturnsFalse()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldEndsWith, Field = "code", Value = "_billing" };
        var outputs = new Dictionary<string, object> { ["code"] = "billing_code" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldMatchesRegex_Match_ReturnsTrue()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldMatchesRegex, Field = "response", Value = @"^(yes|sure|ok)$" };
        var outputs = new Dictionary<string, object> { ["response"] = "yes" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldMatchesRegex_NoMatch_ReturnsFalse()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldMatchesRegex, Field = "response", Value = @"^(yes|sure|ok)$" };
        var outputs = new Dictionary<string, object> { ["response"] = "nope" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldMatchesRegex_IgnoreCase_ReturnsTrue()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldMatchesRegex,
            Field = "intent",
            Value = "^yes$",
            RegexOptions = "IgnoreCase"
        };
        var outputs = new Dictionary<string, object> { ["intent"] = "YES" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldMatchesRegex_Timeout_ReturnsFalse()
    {
        // Catastrophically backtracking pattern with a short timeout override
        var original = ConditionEvaluator.RegexMatchTimeout;
        ConditionEvaluator.RegexMatchTimeout = TimeSpan.FromMilliseconds(1);
        try
        {
            // Clear the cache so our tiny timeout applies
            var condition = new EdgeCondition
            {
                Type = ConditionType.FieldMatchesRegex,
                Field = "body",
                // ReDoS pattern: exponential backtracking on long "aaa..." strings that don't match
                Value = @"^(a+)+$"
            };
            var outputs = new Dictionary<string, object> { ["body"] = new string('a', 30) + "!" };

            // Should not throw — timeout is caught internally
            var act = () => ConditionEvaluator.Evaluate(condition, outputs);
            act.Should().NotThrow();
            // Result should be false (timeout → non-match)
            ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
        }
        finally
        {
            ConditionEvaluator.RegexMatchTimeout = original;
        }
    }

    [Fact]
    public void Evaluate_FieldIsEmpty_NullField_ReturnsTrue()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldIsEmpty, Field = "summary" };
        var outputs = new Dictionary<string, object> { ["summary"] = null! };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldIsEmpty_EmptyString_ReturnsTrue()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldIsEmpty, Field = "summary" };
        var outputs = new Dictionary<string, object> { ["summary"] = "" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldIsEmpty_WhitespaceOnly_ReturnsTrue()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldIsEmpty, Field = "summary" };
        var outputs = new Dictionary<string, object> { ["summary"] = "   " };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldIsEmpty_NonEmpty_ReturnsFalse()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldIsEmpty, Field = "summary" };
        var outputs = new Dictionary<string, object> { ["summary"] = "hello" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldIsNotEmpty_NonEmpty_ReturnsTrue()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldIsNotEmpty, Field = "draft" };
        var outputs = new Dictionary<string, object> { ["draft"] = "Some content" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldIsNotEmpty_EmptyString_ReturnsFalse()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldIsNotEmpty, Field = "draft" };
        var outputs = new Dictionary<string, object> { ["draft"] = "" };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldStartsWith_JsonElementField_Works()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldStartsWith, Field = "intent", Value = "billing/" };
        var je = JsonDocument.Parse("\"billing/general\"").RootElement;
        var outputs = new Dictionary<string, object> { ["intent"] = je };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    #endregion

    #region Phase 3 — Collection Condition Tests

    [Fact]
    public void Evaluate_FieldContainsAny_OneMatch_ReturnsTrue()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAny,
            Field = "tags",
            Value = new[] { "urgent", "escalate" }
        };
        var outputs = new Dictionary<string, object> { ["tags"] = new List<string> { "billing", "urgent" } };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldContainsAny_NoMatch_ReturnsFalse()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAny,
            Field = "tags",
            Value = new[] { "urgent", "escalate" }
        };
        var outputs = new Dictionary<string, object> { ["tags"] = new List<string> { "billing" } };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldContainsAny_EmptyField_ReturnsFalse()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAny,
            Field = "tags",
            Value = new[] { "urgent" }
        };
        var outputs = new Dictionary<string, object> { ["tags"] = new List<string>() };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldContainsAny_EmptyValues_ReturnsFalse()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAny,
            Field = "tags",
            Value = Array.Empty<string>()
        };
        var outputs = new Dictionary<string, object> { ["tags"] = new List<string> { "urgent" } };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldContainsAll_AllMatch_ReturnsTrue()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAll,
            Field = "required_steps",
            Value = new[] { "verified", "payment_ok" }
        };
        var outputs = new Dictionary<string, object> { ["required_steps"] = new List<string> { "verified", "payment_ok", "reviewed" } };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldContainsAll_PartialMatch_ReturnsFalse()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAll,
            Field = "required_steps",
            Value = new[] { "verified", "payment_ok" }
        };
        var outputs = new Dictionary<string, object> { ["required_steps"] = new List<string> { "verified" } };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FieldContainsAll_EmptyValues_ReturnsTrue()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAll,
            Field = "required_steps",
            Value = Array.Empty<string>()
        };
        var outputs = new Dictionary<string, object> { ["required_steps"] = new List<string> { "verified" } };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue("vacuously true");
    }

    [Fact]
    public void Evaluate_FieldContainsAny_JsonElementArray_ReturnsTrue()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAny,
            Field = "tags",
            Value = new[] { "urgent", "escalate" }
        };
        var je = JsonDocument.Parse("[\"billing\",\"urgent\"]").RootElement;
        var outputs = new Dictionary<string, object> { ["tags"] = je };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldContainsAll_JsonElementArray_ReturnsTrue()
    {
        var condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAll,
            Field = "required_steps",
            Value = new[] { "verified", "payment_ok" }
        };
        var je = JsonDocument.Parse("[\"verified\",\"payment_ok\",\"reviewed\"]").RootElement;
        var outputs = new Dictionary<string, object> { ["required_steps"] = je };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    #endregion

    #region Phase 4 — Numeric Gap Fix Tests

    [Fact]
    public void Evaluate_FieldGreaterThan_ShortType_Works()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldGreaterThan, Field = "val", Value = 10 };
        var outputs = new Dictionary<string, object> { ["val"] = (short)50 };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldGreaterThan_ByteType_Works()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldGreaterThan, Field = "val", Value = 10 };
        var outputs = new Dictionary<string, object> { ["val"] = (byte)200 };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_FieldGreaterThan_JsonElementNumeric_Works()
    {
        var condition = new EdgeCondition { Type = ConditionType.FieldGreaterThan, Field = "score", Value = 50 };
        var je = JsonDocument.Parse("75").RootElement;
        var outputs = new Dictionary<string, object> { ["score"] = je };

        ConditionEvaluator.Evaluate(condition, outputs).Should().BeTrue();
    }

    #endregion
}
