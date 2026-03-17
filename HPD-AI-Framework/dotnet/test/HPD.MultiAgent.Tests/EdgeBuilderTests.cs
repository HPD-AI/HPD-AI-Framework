using System.Text.RegularExpressions;
using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Routing;
using HPDAgent.Graph.Abstractions;
using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.MultiAgent.Tests;

public class EdgeBuilderTests
{
    private static AgentConfig CreateTestConfig() => new() { Name = "Test", SystemInstructions = "Test" };

    [Fact]
    public void To_Creates_Unconditional_Edge()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("a", config)
            .AddAgent("b", config)
            .From("a").To("b");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void To_Multiple_Targets_Creates_Multiple_Edges()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("start", config)
            .AddAgent("a", config)
            .AddAgent("b", config)
            .AddAgent("c", config)
            .From("start").To("a", "b", "c");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenEquals_Adds_FieldEquals_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config)
            .AddAgent("solver", config)
            .From("classifier").To("solver").WhenEquals("category", "math");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenNotEquals_Adds_FieldNotEquals_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config)
            .AddAgent("general", config)
            .From("classifier").To("general").WhenNotEquals("category", "math");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenExists_Adds_FieldExists_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("extractor", config)
            .AddAgent("processor", config)
            .From("extractor").To("processor").WhenExists("data");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenNotExists_Adds_FieldNotExists_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("validator", config)
            .AddAgent("errorHandler", config)
            .From("validator").To("errorHandler").WhenNotExists("valid");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenGreaterThan_Adds_FieldGreaterThan_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("scorer", config)
            .AddAgent("highPriority", config)
            .From("scorer").To("highPriority").WhenGreaterThan("score", 0.8);

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenLessThan_Adds_FieldLessThan_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("scorer", config)
            .AddAgent("lowPriority", config)
            .From("scorer").To("lowPriority").WhenLessThan("score", 0.2);

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenContains_Adds_FieldContains_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config)
            .AddAgent("codeHandler", config)
            .From("classifier").To("codeHandler").WhenContains("tags", "code");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void AsDefault_Adds_Default_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config)
            .AddAgent("fallback", config)
            .From("classifier").To("fallback").AsDefault();

        builder.Should().NotBeNull();
    }

    [Fact]
    public void RouteByType_Returns_TypeRouteBuilder()
    {
        var config = CreateTestConfig();

        var typeRouteBuilder = AgentWorkflow.Create()
            .AddAgent("classifier", config, o => o.UnionOutput<MathRoute, GeneralRoute>())
            .From("classifier").RouteByType();

        typeRouteBuilder.Should().NotBeNull();
    }

    [Fact]
    public void RouteByType_With_Multiple_Sources_Throws()
    {
        var config = CreateTestConfig();

        Action act = () => AgentWorkflow.Create()
            .AddAgent("a", config)
            .AddAgent("b", config)
            .From("a", "b").RouteByType();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TypeRouteBuilder_When_Creates_FieldEquals_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config, o => o.UnionOutput<MathRoute, GeneralRoute>())
            .AddAgent("solver", config)
            .AddAgent("general", config)
            .From("classifier").RouteByType()
                .When<MathRoute>("solver")
                .When<GeneralRoute>("general");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void TypeRouteBuilder_Default_Creates_Default_Edge()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config, o => o.UnionOutput<MathRoute, GeneralRoute>())
            .AddAgent("solver", config)
            .AddAgent("fallback", config)
            .From("classifier").RouteByType()
                .When<MathRoute>("solver")
                .Default("fallback");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void From_After_Conditions_Continues_Building()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("a", config)
            .AddAgent("b", config)
            .AddAgent("c", config)
            .From("a").To("b").WhenEquals("x", 1)
            .From("b").To("c");

        builder.Should().NotBeNull();
    }

    // ========================================
    // Phase 1 — Compound Logic Builder Tests
    // ========================================

    [Fact]
    public void When_AndCondition_RegistersCompoundEdge()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("triage", config)
            .AddAgent("vip-billing", config)
            .From("triage").To("vip-billing")
                .When(Condition.And(
                    Condition.Equals("intent", "billing"),
                    Condition.Equals("tier", "VIP")
                ));

        builder.Should().NotBeNull();
    }

    [Fact]
    public void When_OrCondition_RegistersCompoundEdge()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config)
            .AddAgent("escalate", config)
            .From("classifier").To("escalate")
                .When(Condition.Or(
                    Condition.Equals("status", "urgent"),
                    Condition.GreaterThan("priority", 8)
                ));

        builder.Should().NotBeNull();
    }

    [Fact]
    public void When_NotCondition_RegistersCompoundEdge()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config)
            .AddAgent("skip", config)
            .From("classifier").To("skip")
                .When(Condition.Not(Condition.Exists("summary")));

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Condition_And_ReturnsCorrectEdgeCondition()
    {
        var result = Condition.And(
            Condition.Equals("intent", "billing"),
            Condition.Equals("tier", "VIP")
        );

        result.Type.Should().Be(ConditionType.And);
        result.Conditions.Should().HaveCount(2);
        result.Conditions![0].Type.Should().Be(ConditionType.FieldEquals);
        result.Conditions![0].Field.Should().Be("intent");
        result.Conditions![1].Field.Should().Be("tier");
    }

    [Fact]
    public void Condition_Or_ReturnsCorrectEdgeCondition()
    {
        var result = Condition.Or(
            Condition.Equals("a", "x"),
            Condition.Equals("b", "y")
        );

        result.Type.Should().Be(ConditionType.Or);
        result.Conditions.Should().HaveCount(2);
    }

    [Fact]
    public void Condition_Not_ReturnsCorrectEdgeCondition()
    {
        var result = Condition.Not(Condition.Exists("summary"));

        result.Type.Should().Be(ConditionType.Not);
        result.Conditions.Should().HaveCount(1);
        result.Conditions![0].Type.Should().Be(ConditionType.FieldExists);
        result.Conditions![0].Field.Should().Be("summary");
    }

    // ========================================
    // Phase 2 — String Condition Builder Tests
    // ========================================

    [Fact]
    public void WhenStartsWith_Adds_FieldStartsWith_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("router", config)
            .AddAgent("billing", config)
            .From("router").To("billing").WhenStartsWith("intent", "billing/");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenEndsWith_Adds_FieldEndsWith_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("router", config)
            .AddAgent("billing", config)
            .From("router").To("billing").WhenEndsWith("code", "_billing");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenMatchesRegex_Adds_FieldMatchesRegex_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config)
            .AddAgent("affirm", config)
            .From("classifier").To("affirm").WhenMatchesRegex("response", @"^(yes|sure|ok)$");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenMatchesRegex_WithRegexOptions_SetsOptions()
    {
        // Test that WhenMatchesRegex correctly serializes RegexOptions
        // We verify the edge condition by inspecting what was registered.
        // Since EdgeTargetBuilder is internal, we test via Condition factory instead.
        var result = Condition.MatchesRegex("intent", @"^yes$", RegexOptions.IgnoreCase);

        result.Type.Should().Be(ConditionType.FieldMatchesRegex);
        result.Field.Should().Be("intent");
        result.Value.Should().Be(@"^yes$");
        result.RegexOptions.Should().Be("IgnoreCase");
    }

    [Fact]
    public void WhenEmpty_Adds_FieldIsEmpty_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("drafter", config)
            .AddAgent("retry", config)
            .From("drafter").To("retry").WhenEmpty("draft");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenNotEmpty_Adds_FieldIsNotEmpty_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("drafter", config)
            .AddAgent("reviewer", config)
            .From("drafter").To("reviewer").WhenNotEmpty("draft");

        builder.Should().NotBeNull();
    }

    // ========================================
    // Phase 3 — Collection Condition Builder Tests
    // ========================================

    [Fact]
    public void WhenContainsAny_Adds_FieldContainsAny_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config)
            .AddAgent("escalate", config)
            .From("classifier").To("escalate").WhenContainsAny("tags", "urgent", "escalate", "manager");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void WhenContainsAll_Adds_FieldContainsAll_Condition()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("checker", config)
            .AddAgent("approve", config)
            .From("checker").To("approve").WhenContainsAll("required_steps", "verified", "payment_ok");

        builder.Should().NotBeNull();
    }

    // Test types
    public record MathRoute(string Question);
    public record GeneralRoute(string Message);
}
