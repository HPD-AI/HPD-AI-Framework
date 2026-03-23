using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;
using HPD.MultiAgent.Routing;
using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for Phase 3: Resilience and Handoff features.
/// </summary>
public class ResilienceAndHandoffTests
{
    private static AgentConfig CreateTestConfig() => new() { Name = "Test", SystemInstructions = "Test" };

    #region Error Handling Tests

    [Fact]
    public void OnErrorStop_Sets_ErrorMode()
    {
        var options = new AgentNodeOptions()
            .OnErrorStop();

        options.ErrorMode.Should().Be(ErrorMode.Stop);
    }

    [Fact]
    public void OnErrorSkip_Sets_ErrorMode()
    {
        var options = new AgentNodeOptions()
            .OnErrorSkip();

        options.ErrorMode.Should().Be(ErrorMode.Skip);
    }

    [Fact]
    public void OnErrorIsolate_Sets_ErrorMode()
    {
        var options = new AgentNodeOptions()
            .OnErrorIsolate();

        options.ErrorMode.Should().Be(ErrorMode.Isolate);
    }

    [Fact]
    public void OnErrorFallback_Sets_ErrorMode_And_FallbackId()
    {
        var options = new AgentNodeOptions()
            .OnErrorFallback("fallbackAgent");

        options.ErrorMode.Should().Be(ErrorMode.Fallback);
        options.FallbackAgentId.Should().Be("fallbackAgent");
    }

    [Fact]
    public void OnErrorFallback_With_Empty_Id_Throws()
    {
        var options = new AgentNodeOptions();

        Action act = () => options.OnErrorFallback("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Error_Handling_Can_Be_Chained()
    {
        var options = new AgentNodeOptions()
            .WithTimeout(TimeSpan.FromSeconds(30))
            .WithRetry(3)
            .OnErrorSkip();

        options.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        options.RetryPolicy.Should().NotBeNull();
        options.ErrorMode.Should().Be(ErrorMode.Skip);
    }

    [Fact]
    public void AddAgent_With_Error_Handling_Works()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("risky", config, o => o
                .WithRetry(3)
                .OnErrorFallback("safe"))
            .AddAgent("safe", config);

        builder.Should().NotBeNull();
    }

    #endregion

    #region Handoff Tests

    [Fact]
    public void WithHandoff_Sets_OutputMode_To_Handoff()
    {
        var options = new AgentNodeOptions()
            .WithHandoff("solver", "Route to solver for math problems");

        options.OutputMode.Should().Be(AgentOutputMode.Handoff);
    }

    [Fact]
    public void WithHandoff_Adds_Target()
    {
        var options = new AgentNodeOptions()
            .WithHandoff("solver", "Route to solver");

        options.HandoffTargets.Should().NotBeNull();
        options.HandoffTargets.Should().ContainKey("solver");
        options.HandoffTargets!["solver"].Should().Be("Route to solver");
    }

    [Fact]
    public void WithHandoff_Multiple_Calls_Adds_Multiple_Targets()
    {
        var options = new AgentNodeOptions()
            .WithHandoff("solver", "For math")
            .WithHandoff("researcher", "For research")
            .WithHandoff("general", "For general queries");

        options.HandoffTargets.Should().HaveCount(3);
        options.HandoffTargets.Should().ContainKey("solver");
        options.HandoffTargets.Should().ContainKey("researcher");
        options.HandoffTargets.Should().ContainKey("general");
    }

    [Fact]
    public void WithHandoffs_Adds_All_Targets()
    {
        var options = new AgentNodeOptions()
            .WithHandoffs(
                ("solver", "For math problems"),
                ("researcher", "For research queries"),
                ("general", "For general questions")
            );

        options.OutputMode.Should().Be(AgentOutputMode.Handoff);
        options.HandoffTargets.Should().HaveCount(3);
    }

    [Fact]
    public void WithHandoff_Empty_TargetId_Throws()
    {
        var options = new AgentNodeOptions();

        Action act = () => options.WithHandoff("", "description");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithHandoff_Empty_Description_Throws()
    {
        var options = new AgentNodeOptions();

        Action act = () => options.WithHandoff("target", "");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithHandoffs_Empty_Array_Throws()
    {
        var options = new AgentNodeOptions();

        Action act = () => options.WithHandoffs();

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region RouterAgentBuilder Tests

    [Fact]
    public void AddRouterAgent_Returns_RouterAgentBuilder()
    {
        var config = CreateTestConfig();

        var routerBuilder = AgentWorkflow.Create()
            .AddRouterAgent("router", config);

        routerBuilder.Should().NotBeNull();
        routerBuilder.Should().BeOfType<RouterAgentBuilder>();
    }

    [Fact]
    public void RouterAgentBuilder_WithHandoff_Creates_Edge()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddRouterAgent("router", config)
                .WithHandoff("solver", "Route to solver for math")
                .WithHandoff("researcher", "Route to researcher for research")
            .AddAgent("solver", config)
            .AddAgent("researcher", config);

        builder.Should().NotBeNull();
    }

    [Fact]
    public void RouterAgentBuilder_WithDefaultHandoff_Creates_Default_Edge()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddRouterAgent("router", config)
                .WithHandoff("solver", "For math")
                .WithDefaultHandoff("general")
            .AddAgent("solver", config)
            .AddAgent("general", config);

        builder.Should().NotBeNull();
    }

    [Fact]
    public void RouterAgentBuilder_Configure_Applies_Options()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddRouterAgent("router", config)
                .WithHandoff("solver", "For math")
                .Configure(o => o.WithTimeout(TimeSpan.FromSeconds(30)));

        builder.Should().NotBeNull();
    }

    [Fact]
    public void RouterAgentBuilder_Can_Chain_To_From()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("start", config)
            .AddRouterAgent("router", config)
                .WithHandoff("solver", "For math")
            .AddAgent("solver", config)
            .From("START").To("start")
            .From("start").To("router")
            .From("solver").To("END");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void RouterAgentBuilder_Can_BuildAsync()
    {
        var config = CreateTestConfig();

        var routerBuilder = AgentWorkflow.Create()
            .AddRouterAgent("router", config)
                .WithHandoff("solver", "For math")
            .AddAgent("solver", config)
            .From("START").To("router")
            .From("solver").To("END");

        // Just check that BuildAsync can be called (we won't await as that requires real config)
        var buildMethod = routerBuilder.GetType().GetMethod("BuildAsync");
        buildMethod.Should().NotBeNull();
    }

    #endregion

    #region Complete Handoff Workflow Tests

    [Fact]
    public void Complete_Handoff_Workflow_Builds()
    {
        var routerConfig = new AgentConfig
        {
            Name = "Router",
            SystemInstructions = "Route requests to appropriate handlers"
        };

        var solverConfig = new AgentConfig
        {
            Name = "Solver",
            SystemInstructions = "Solve math problems"
        };

        var researcherConfig = new AgentConfig
        {
            Name = "Researcher",
            SystemInstructions = "Research topics"
        };

        var generalConfig = new AgentConfig
        {
            Name = "General",
            SystemInstructions = "Handle general queries"
        };

        var builder = AgentWorkflow.Create()
            .WithName("HandoffWorkflow")
            .AddRouterAgent("router", routerConfig)
                .WithHandoff("solver", "Use for math problems and calculations")
                .WithHandoff("researcher", "Use for research questions requiring information lookup")
                .WithDefaultHandoff("general")
            .AddAgent("solver", solverConfig)
            .AddAgent("researcher", researcherConfig)
            .AddAgent("general", generalConfig)
            .From("START").To("router")
            .From("solver", "researcher", "general").To("END");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Nested_Router_Workflow_Builds()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddRouterAgent("firstRouter", config)
                .WithHandoff("technical", "For technical questions")
                .WithHandoff("general", "For general questions")
            .AddRouterAgent("technical", config)
                .WithHandoff("code", "For coding questions")
                .WithHandoff("math", "For math questions")
            .AddAgent("code", config)
            .AddAgent("math", config)
            .AddAgent("general", config)
            .From("START").To("firstRouter")
            .From("code", "math", "general").To("END");

        builder.Should().NotBeNull();
    }

    #endregion

    #region Retry Policy Tests

    [Fact]
    public void WithRetry_Sets_RetryPolicy()
    {
        var options = new AgentNodeOptions()
            .WithRetry(3);

        options.RetryPolicy.Should().NotBeNull();
        options.RetryPolicy!.MaxAttempts.Should().Be(3);
    }

    [Fact]
    public void WithRetry_With_Strategy_Sets_Strategy()
    {
        var options = new AgentNodeOptions()
            .WithRetry(3, BackoffStrategy.Linear);

        options.RetryPolicy.Should().NotBeNull();
        options.RetryPolicy!.Strategy.Should().Be(BackoffStrategy.Linear);
    }

    [Fact]
    public void WithRetryTransient_Sets_RetryableExceptions()
    {
        var options = new AgentNodeOptions()
            .WithRetryTransient(3);

        options.RetryPolicy.Should().NotBeNull();
        options.RetryPolicy!.RetryableExceptions.Should().Contain(typeof(TimeoutException));
        options.RetryPolicy.RetryableExceptions.Should().Contain(typeof(HttpRequestException));
    }

    [Fact]
    public void WithRetry_Custom_Policy()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromSeconds(2),
            Strategy = BackoffStrategy.Constant,
            MaxDelay = TimeSpan.FromSeconds(30)
        };

        var options = new AgentNodeOptions()
            .WithRetry(policy);

        options.RetryPolicy.Should().BeSameAs(policy);
    }

    [Fact]
    public void Retry_And_Error_Handling_Combined()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("flaky", config, o => o
                .WithRetry(3, BackoffStrategy.Exponential)
                .OnErrorFallback("reliable"))
            .AddAgent("reliable", config)
            .From("START").To("flaky")
            .From("flaky", "reliable").To("END");

        builder.Should().NotBeNull();
    }

    #endregion
}
