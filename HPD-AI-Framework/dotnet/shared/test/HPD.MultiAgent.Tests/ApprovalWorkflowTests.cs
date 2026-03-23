using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for Phase 4: Approval Workflow features.
/// </summary>
public class ApprovalWorkflowTests
{
    private static AgentConfig CreateTestConfig() => new() { Name = "Test", SystemInstructions = "Test" };

    #region ApprovalConfig Tests

    [Fact]
    public void ApprovalConfig_Default_Values_Are_Correct()
    {
        var config = new ApprovalConfig();

        config.Condition.Should().BeNull();
        config.Message.Should().BeNull();
        config.Timeout.Should().Be(TimeSpan.FromMinutes(5));
        config.TimeoutBehavior.Should().Be(ApprovalTimeoutBehavior.Deny);
    }

    [Fact]
    public void ApprovalConfig_With_Condition()
    {
        var config = new ApprovalConfig
        {
            Condition = ctx => ctx.HasOutput("dangerous"),
            Message = ctx => $"Approve action for {ctx.NodeId}?",
            Timeout = TimeSpan.FromMinutes(10)
        };

        config.Condition.Should().NotBeNull();
        config.Message.Should().NotBeNull();
        config.Timeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void ApprovalTimeoutBehavior_All_Values_Exist()
    {
        var values = Enum.GetValues<ApprovalTimeoutBehavior>();

        values.Should().Contain(ApprovalTimeoutBehavior.Deny);
        values.Should().Contain(ApprovalTimeoutBehavior.AutoApprove);
        values.Should().Contain(ApprovalTimeoutBehavior.SuspendIndefinitely);
    }

    #endregion

    #region ApprovalContext Tests

    [Fact]
    public void ApprovalContext_GetOutput_Returns_Typed_Value()
    {
        var outputs = new Dictionary<string, object>
        {
            ["score"] = 0.95,
            ["category"] = "high-risk"
        };

        var context = new ApprovalContext
        {
            NodeId = "test",
            Outputs = outputs
        };

        context.GetOutput<double>("score").Should().Be(0.95);
        context.GetOutput<string>("category").Should().Be("high-risk");
    }

    [Fact]
    public void ApprovalContext_GetOutput_Returns_Default_For_Missing()
    {
        var context = new ApprovalContext
        {
            NodeId = "test",
            Outputs = new Dictionary<string, object>()
        };

        context.GetOutput<string>("missing").Should().BeNull();
        context.GetOutput<int>("missing").Should().Be(0);
    }

    [Fact]
    public void ApprovalContext_HasOutput_Returns_True_When_Present()
    {
        var context = new ApprovalContext
        {
            NodeId = "test",
            Outputs = new Dictionary<string, object> { ["key"] = "value" }
        };

        context.HasOutput("key").Should().BeTrue();
        context.HasOutput("missing").Should().BeFalse();
    }

    #endregion

    #region RequiresApproval Fluent API Tests

    [Fact]
    public void RequiresApproval_Default_Sets_Always_True_Condition()
    {
        var options = new AgentNodeOptions()
            .RequiresApproval();

        options.Approval.Should().NotBeNull();
        options.Approval!.Condition.Should().NotBeNull();

        // Condition should always return true
        var context = new ApprovalContext
        {
            NodeId = "test",
            Outputs = new Dictionary<string, object>()
        };
        options.Approval.Condition!(context).Should().BeTrue();
    }

    [Fact]
    public void RequiresApproval_With_Message_Sets_Message()
    {
        var options = new AgentNodeOptions()
            .RequiresApproval("Please approve this action");

        options.Approval.Should().NotBeNull();

        var context = new ApprovalContext
        {
            NodeId = "test",
            Outputs = new Dictionary<string, object>()
        };
        options.Approval!.Message!(context).Should().Be("Please approve this action");
    }

    [Fact]
    public void RequiresApproval_With_Timeout_Sets_Timeout()
    {
        var options = new AgentNodeOptions()
            .RequiresApproval(timeout: TimeSpan.FromMinutes(10));

        options.Approval.Should().NotBeNull();
        options.Approval!.Timeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void RequiresApproval_With_Condition_Sets_Condition()
    {
        var options = new AgentNodeOptions()
            .RequiresApproval(
                when: ctx => ctx.GetOutput<double>("score") > 0.9,
                message: ctx => $"High score detected: {ctx.GetOutput<double>("score")}"
            );

        options.Approval.Should().NotBeNull();

        var lowScoreContext = new ApprovalContext
        {
            NodeId = "test",
            Outputs = new Dictionary<string, object> { ["score"] = 0.5 }
        };
        options.Approval!.Condition!(lowScoreContext).Should().BeFalse();

        var highScoreContext = new ApprovalContext
        {
            NodeId = "test",
            Outputs = new Dictionary<string, object> { ["score"] = 0.95 }
        };
        options.Approval.Condition!(highScoreContext).Should().BeTrue();
    }

    [Fact]
    public void RequiresApproval_With_Config_Uses_Config()
    {
        var config = new ApprovalConfig
        {
            Condition = _ => true,
            Timeout = TimeSpan.FromMinutes(15),
            TimeoutBehavior = ApprovalTimeoutBehavior.AutoApprove
        };

        var options = new AgentNodeOptions()
            .RequiresApproval(config);

        options.Approval.Should().BeSameAs(config);
    }

    [Fact]
    public void RequiresApprovalWhen_Sets_Field_Equals_Condition()
    {
        var options = new AgentNodeOptions()
            .RequiresApprovalWhen("action", "delete");

        options.Approval.Should().NotBeNull();

        var deleteContext = new ApprovalContext
        {
            NodeId = "test",
            Outputs = new Dictionary<string, object> { ["action"] = "delete" }
        };
        options.Approval!.Condition!(deleteContext).Should().BeTrue();

        var updateContext = new ApprovalContext
        {
            NodeId = "test",
            Outputs = new Dictionary<string, object> { ["action"] = "update" }
        };
        options.Approval.Condition!(updateContext).Should().BeFalse();
    }

    [Fact]
    public void RequiresApprovalWhenExists_Sets_Field_Exists_Condition()
    {
        var options = new AgentNodeOptions()
            .RequiresApprovalWhenExists("dangerous_flag");

        options.Approval.Should().NotBeNull();

        var withFlagContext = new ApprovalContext
        {
            NodeId = "test",
            Outputs = new Dictionary<string, object> { ["dangerous_flag"] = true }
        };
        options.Approval!.Condition!(withFlagContext).Should().BeTrue();

        var withoutFlagContext = new ApprovalContext
        {
            NodeId = "test",
            Outputs = new Dictionary<string, object> { ["safe"] = true }
        };
        options.Approval.Condition!(withoutFlagContext).Should().BeFalse();
    }

    #endregion

    #region Workflow Builder Integration Tests

    [Fact]
    public void AddAgent_With_RequiresApproval_Works()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("risky", config, o => o.RequiresApproval("Approve this risky action?"));

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Complete_Approval_Workflow_Builds()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .WithName("ApprovalWorkflow")
            .AddAgent("analyzer", config, o => o.StructuredOutput<AnalysisResult>())
            .AddAgent("executor", config, o => o
                .RequiresApproval(
                    when: ctx => ctx.GetOutput<double>("risk") > 0.7,
                    message: ctx => $"Risk level is {ctx.GetOutput<double>("risk"):P0}. Proceed?",
                    timeout: TimeSpan.FromMinutes(10)))
            .AddAgent("reporter", config)
            .From("START").To("analyzer")
            .From("analyzer").To("executor")
            .From("executor").To("reporter")
            .From("reporter").To("END");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Multiple_Approval_Nodes_Builds()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("planner", config, o => o.RequiresApproval("Approve plan?"))
            .AddAgent("executor", config, o => o.RequiresApproval("Approve execution?"))
            .AddAgent("verifier", config, o => o.RequiresApproval("Verify results?"))
            .From("START").To("planner")
            .From("planner").To("executor")
            .From("executor").To("verifier")
            .From("verifier").To("END");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Conditional_Approval_With_RouteByType_Builds()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("classifier", config, o => o.UnionOutput<LowRiskAction, HighRiskAction>())
            .AddAgent("lowRiskHandler", config) // No approval needed
            .AddAgent("highRiskHandler", config, o => o.RequiresApproval("High risk action detected!"))
            .From("START").To("classifier")
            .From("classifier").RouteByType()
                .When<LowRiskAction>("lowRiskHandler")
                .When<HighRiskAction>("highRiskHandler")
            .From("lowRiskHandler", "highRiskHandler").To("END");

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Approval_With_Error_Handling_Builds()
    {
        var config = CreateTestConfig();

        var builder = AgentWorkflow.Create()
            .AddAgent("dangerous", config, o => o
                .RequiresApproval("Approve dangerous action?")
                .WithRetry(3)
                .OnErrorFallback("safe"))
            .AddAgent("safe", config)
            .From("START").To("dangerous")
            .From("dangerous", "safe").To("END");

        builder.Should().NotBeNull();
    }

    #endregion

    #region ApprovalWorkflowExtensions Tests

    [Fact]
    public void CreateApprovalResponse_Creates_Approve_Response()
    {
        var response = ApprovalWorkflowExtensions.CreateApprovalResponse(
            requestId: "req-123",
            approved: true,
            reason: "Looks good");

        response.RequestId.Should().Be("req-123");
        response.Approved.Should().BeTrue();
        response.Reason.Should().Be("Looks good");
        response.SourceName.Should().Be("User");
    }

    [Fact]
    public void CreateApprovalResponse_Creates_Deny_Response()
    {
        var response = ApprovalWorkflowExtensions.CreateApprovalResponse(
            requestId: "req-456",
            approved: false,
            reason: "Too risky");

        response.RequestId.Should().Be("req-456");
        response.Approved.Should().BeFalse();
        response.Reason.Should().Be("Too risky");
    }

    [Fact]
    public void CreateApprovalResponse_With_ResumeData()
    {
        var resumeData = new Dictionary<string, object> { ["modified"] = true };

        var response = ApprovalWorkflowExtensions.CreateApprovalResponse(
            requestId: "req-789",
            approved: true,
            resumeData: resumeData);

        response.ResumeData.Should().Be(resumeData);
    }

    #endregion

    // Test types
    public record AnalysisResult(double Risk, string Category);
    public record LowRiskAction(string Description);
    public record HighRiskAction(string Description, double RiskLevel);
}
