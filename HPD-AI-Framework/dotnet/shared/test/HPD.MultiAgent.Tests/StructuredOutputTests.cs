using HPD.Agent;
using HPD.MultiAgent;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for structured output modes (Structured, Union).
/// </summary>
public class StructuredOutputTests
{
    private static AgentConfig CreateTestConfig() => new() { Name = "Test", SystemInstructions = "Test" };

    #region StructuredOutput<T> Tests

    [Fact]
    public void StructuredOutput_Sets_OutputMode_To_Structured()
    {
        var options = new AgentNodeOptions()
            .StructuredOutput<AnalysisResult>();

        options.OutputMode.Should().Be(AgentOutputMode.Structured);
    }

    [Fact]
    public void StructuredOutput_Sets_StructuredType()
    {
        var options = new AgentNodeOptions()
            .StructuredOutput<AnalysisResult>();

        options.StructuredType.Should().Be(typeof(AnalysisResult));
    }

    [Fact]
    public void StructuredOutput_Defaults_To_Native_Mode()
    {
        var options = new AgentNodeOptions()
            .StructuredOutput<AnalysisResult>();

        options.StructuredOutputMode.Should().Be(StructuredOutputMode.Native);
    }

    [Fact]
    public void StructuredOutput_With_Tool_Mode()
    {
        var options = new AgentNodeOptions()
            .StructuredOutput<AnalysisResult>(StructuredOutputMode.Tool);

        options.StructuredOutputMode.Should().Be(StructuredOutputMode.Tool);
    }

    [Fact]
    public void StructuredOutput_Can_Be_Chained_With_Other_Options()
    {
        var options = new AgentNodeOptions()
            .StructuredOutput<AnalysisResult>()
            .WithTimeout(TimeSpan.FromSeconds(30))
            .WithRetry(3);

        options.OutputMode.Should().Be(AgentOutputMode.Structured);
        options.StructuredType.Should().Be(typeof(AnalysisResult));
        options.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        options.RetryPolicy.Should().NotBeNull();
    }

    [Fact]
    public void AddAgent_With_StructuredOutput_Works()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("analyzer", config, o => o.StructuredOutput<AnalysisResult>());

        builder.Should().NotBeNull();
    }

    #endregion

    #region UnionOutput<T1, T2, ...> Tests

    [Fact]
    public void UnionOutput_Two_Types_Sets_OutputMode_To_Union()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, GeneralRoute>();

        options.OutputMode.Should().Be(AgentOutputMode.Union);
    }

    [Fact]
    public void UnionOutput_Two_Types_Sets_UnionTypes()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, GeneralRoute>();

        options.UnionTypes.Should().HaveCount(2);
        options.UnionTypes.Should().Contain(typeof(MathRoute));
        options.UnionTypes.Should().Contain(typeof(GeneralRoute));
    }

    [Fact]
    public void UnionOutput_Three_Types_Sets_UnionTypes()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, ResearchRoute, GeneralRoute>();

        options.UnionTypes.Should().HaveCount(3);
        options.UnionTypes.Should().Contain(typeof(MathRoute));
        options.UnionTypes.Should().Contain(typeof(ResearchRoute));
        options.UnionTypes.Should().Contain(typeof(GeneralRoute));
    }

    [Fact]
    public void UnionOutput_Four_Types_Sets_UnionTypes()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, ResearchRoute, GeneralRoute, CodeRoute>();

        options.UnionTypes.Should().HaveCount(4);
    }

    [Fact]
    public void UnionOutput_Five_Types_Sets_UnionTypes()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, ResearchRoute, GeneralRoute, CodeRoute, DataRoute>();

        options.UnionTypes.Should().HaveCount(5);
    }

    [Fact]
    public void UnionOutput_Defaults_To_Native_Mode()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, GeneralRoute>();

        options.StructuredOutputMode.Should().Be(StructuredOutputMode.Native);
    }

    [Fact]
    public void UnionOutput_With_Tool_Mode()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, GeneralRoute>(StructuredOutputMode.Tool);

        options.StructuredOutputMode.Should().Be(StructuredOutputMode.Tool);
    }

    #endregion

    #region RouteByType Integration Tests

    [Fact]
    public void RouteByType_With_UnionOutput_Creates_Valid_Workflow()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config, o => o.UnionOutput<MathRoute, GeneralRoute>())
            .AddAgent("solver", config)
            .AddAgent("general", config)
            .From("START").To("classifier")
            .From("classifier").RouteByType()
                .When<MathRoute>("solver")
                .When<GeneralRoute>("general")
            .From("solver", "general").To("END");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void RouteByType_With_Default_Creates_Fallback_Edge()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config, o => o.UnionOutput<MathRoute, GeneralRoute>())
            .AddAgent("solver", config)
            .AddAgent("fallback", config)
            .From("START").To("classifier")
            .From("classifier").RouteByType()
                .When<MathRoute>("solver")
                .Default("fallback")
            .From("solver", "fallback").To("END");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void RouteByType_With_Three_Types_Creates_Three_Edges()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config, o => o.UnionOutput<MathRoute, ResearchRoute, GeneralRoute>())
            .AddAgent("math", config)
            .AddAgent("research", config)
            .AddAgent("general", config)
            .From("START").To("classifier")
            .From("classifier").RouteByType()
                .When<MathRoute>("math")
                .When<ResearchRoute>("research")
                .When<GeneralRoute>("general")
            .From("math", "research", "general").To("END");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void RouteByType_Can_Chain_To_More_Edges()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config, o => o.UnionOutput<MathRoute, GeneralRoute>())
            .AddAgent("solver", config)
            .AddAgent("general", config)
            .AddAgent("verifier", config)
            .From("START").To("classifier")
            .From("classifier").RouteByType()
                .When<MathRoute>("solver")
                .When<GeneralRoute>("general")
            .From("solver").To("verifier")
            .From("verifier", "general").To("END");

        builder.Should().NotBeNull();
    }

    #endregion

    #region Complex Workflow Tests

    [Fact]
    public void Mixed_Routing_Conditions_Work_Together()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .WithName("MixedRoutingWorkflow")
            .AddAgent("classifier", config, o => o.UnionOutput<MathRoute, GeneralRoute>())
            .AddAgent("highConfidence", config)
            .AddAgent("lowConfidence", config)
            .AddAgent("general", config)
            .From("START").To("classifier")
            // Route by type
            .From("classifier").RouteByType()
                .When<MathRoute>("highConfidence")
                .When<GeneralRoute>("general")
            // Additional condition-based routing (separate edge)
            .From("classifier").To("lowConfidence").WhenLessThan("confidence", 0.5)
            .From("highConfidence", "lowConfidence", "general").To("END");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Workflow_With_Multiple_Union_Outputs()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("firstClassifier", config, o => o.UnionOutput<MathRoute, GeneralRoute>())
            .AddAgent("secondClassifier", config, o => o.UnionOutput<ResearchRoute, CodeRoute>())
            .AddAgent("math", config)
            .AddAgent("general", config)
            .AddAgent("research", config)
            .AddAgent("code", config)
            .From("START").To("firstClassifier")
            .From("firstClassifier").RouteByType()
                .When<MathRoute>("math")
                .When<GeneralRoute>("secondClassifier")
            .From("secondClassifier").RouteByType()
                .When<ResearchRoute>("research")
                .When<CodeRoute>("code")
            .From("math", "research", "code").To("END");

        builder.Should().NotBeNull();
    }

    #endregion

    // Test types
    public record AnalysisResult(string Sentiment, float Confidence, string[] Keywords);
    public record MathRoute(string Question, float Confidence);
    public record ResearchRoute(string Topic, string[] Keywords);
    public record GeneralRoute(string Message);
    public record CodeRoute(string Language, string Code);
    public record DataRoute(string Source, string Format);
}
