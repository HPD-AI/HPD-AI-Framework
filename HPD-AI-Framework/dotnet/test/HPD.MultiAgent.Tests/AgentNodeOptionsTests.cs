using HPD.MultiAgent;
using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.MultiAgent.Tests;

public class AgentNodeOptionsTests
{
    [Fact]
    public void Default_Values_Are_Correct()
    {
        var options = new AgentNodeOptions();

        options.OutputMode.Should().Be(AgentOutputMode.String);
        options.InputKey.Should().BeNull();
        options.OutputKey.Should().BeNull();
        options.InputTemplate.Should().BeNull();
        options.Timeout.Should().BeNull();
        options.RetryPolicy.Should().BeNull();
        options.StructuredType.Should().BeNull();
        options.UnionTypes.Should().BeNull();
        options.StructuredOutputMode.Should().Be(StructuredOutputMode.Native);
    }

    [Fact]
    public void StructuredOutput_Sets_Mode_And_Type()
    {
        var options = new AgentNodeOptions()
            .StructuredOutput<TestResult>();

        options.OutputMode.Should().Be(AgentOutputMode.Structured);
        options.StructuredType.Should().Be(typeof(TestResult));
        options.StructuredOutputMode.Should().Be(StructuredOutputMode.Native);
    }

    [Fact]
    public void StructuredOutput_With_Tool_Mode()
    {
        var options = new AgentNodeOptions()
            .StructuredOutput<TestResult>(StructuredOutputMode.Tool);

        options.OutputMode.Should().Be(AgentOutputMode.Structured);
        options.StructuredType.Should().Be(typeof(TestResult));
        options.StructuredOutputMode.Should().Be(StructuredOutputMode.Tool);
    }

    [Fact]
    public void UnionOutput_Two_Types()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, GeneralRoute>();

        options.OutputMode.Should().Be(AgentOutputMode.Union);
        options.UnionTypes.Should().HaveCount(2);
        options.UnionTypes.Should().Contain(typeof(MathRoute));
        options.UnionTypes.Should().Contain(typeof(GeneralRoute));
    }

    [Fact]
    public void UnionOutput_Three_Types()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, ResearchRoute, GeneralRoute>();

        options.OutputMode.Should().Be(AgentOutputMode.Union);
        options.UnionTypes.Should().HaveCount(3);
    }

    [Fact]
    public void UnionOutput_Four_Types()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, ResearchRoute, GeneralRoute, CodeRoute>();

        options.OutputMode.Should().Be(AgentOutputMode.Union);
        options.UnionTypes.Should().HaveCount(4);
    }

    [Fact]
    public void UnionOutput_Five_Types()
    {
        var options = new AgentNodeOptions()
            .UnionOutput<MathRoute, ResearchRoute, GeneralRoute, CodeRoute, DataRoute>();

        options.OutputMode.Should().Be(AgentOutputMode.Union);
        options.UnionTypes.Should().HaveCount(5);
    }

    [Fact]
    public void WithRetry_Sets_Default_Policy()
    {
        var options = new AgentNodeOptions()
            .WithRetry(maxAttempts: 3);

        options.RetryPolicy.Should().NotBeNull();
        options.RetryPolicy!.MaxAttempts.Should().Be(3);
        options.RetryPolicy.Strategy.Should().Be(BackoffStrategy.Exponential);
        options.RetryPolicy.InitialDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void WithRetry_With_Strategy()
    {
        var options = new AgentNodeOptions()
            .WithRetry(maxAttempts: 5, strategy: BackoffStrategy.Linear);

        options.RetryPolicy.Should().NotBeNull();
        options.RetryPolicy!.MaxAttempts.Should().Be(5);
        options.RetryPolicy.Strategy.Should().Be(BackoffStrategy.Linear);
    }

    [Fact]
    public void WithRetry_Custom_Policy()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 4,
            InitialDelay = TimeSpan.FromSeconds(2),
            Strategy = BackoffStrategy.Constant,
            MaxDelay = TimeSpan.FromSeconds(10)
        };

        var options = new AgentNodeOptions()
            .WithRetry(policy);

        options.RetryPolicy.Should().BeSameAs(policy);
    }

    [Fact]
    public void WithRetryTransient_Sets_Retryable_Exceptions()
    {
        var options = new AgentNodeOptions()
            .WithRetryTransient(maxAttempts: 3);

        options.RetryPolicy.Should().NotBeNull();
        options.RetryPolicy!.RetryableExceptions.Should().NotBeNull();
        options.RetryPolicy.RetryableExceptions.Should().Contain(typeof(TimeoutException));
        options.RetryPolicy.RetryableExceptions.Should().Contain(typeof(HttpRequestException));
    }

    [Fact]
    public void WithTimeout_Sets_Timeout()
    {
        var options = new AgentNodeOptions()
            .WithTimeout(TimeSpan.FromSeconds(30));

        options.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void WithInstructions_Sets_Additional_Instructions()
    {
        var options = new AgentNodeOptions()
            .WithInstructions("Be concise");

        options.AdditionalSystemInstructions.Should().Be("Be concise");
    }

    [Fact]
    public void WithInputKey_Sets_Input_Key()
    {
        var options = new AgentNodeOptions()
            .WithInputKey("user_query");

        options.InputKey.Should().Be("user_query");
    }

    [Fact]
    public void WithOutputKey_Sets_Output_Key()
    {
        var options = new AgentNodeOptions()
            .WithOutputKey("result");

        options.OutputKey.Should().Be("result");
    }

    [Fact]
    public void WithInputTemplate_Sets_Template()
    {
        var template = "Question: {{question}}\nContext: {{context}}";
        var options = new AgentNodeOptions()
            .WithInputTemplate(template);

        options.InputTemplate.Should().Be(template);
    }

    [Fact]
    public void Fluent_Chaining_Works()
    {
        var options = new AgentNodeOptions()
            .WithInputKey("query")
            .WithOutputKey("answer")
            .WithTimeout(TimeSpan.FromMinutes(1))
            .WithRetry(3)
            .WithInstructions("Be helpful");

        options.InputKey.Should().Be("query");
        options.OutputKey.Should().Be("answer");
        options.Timeout.Should().Be(TimeSpan.FromMinutes(1));
        options.RetryPolicy.Should().NotBeNull();
        options.AdditionalSystemInstructions.Should().Be("Be helpful");
    }

    // Test types for union output
    public record TestResult(string Value);
    public record MathRoute(string Question);
    public record ResearchRoute(string Topic);
    public record GeneralRoute(string Message);
    public record CodeRoute(string Language);
    public record DataRoute(string Source);
}
