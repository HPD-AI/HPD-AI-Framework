using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class ExecuteCommandPermissionPolicyTests
{
    [Fact]
    public async Task Policy_emits_only_server_owned_rule_choices_for_analyzed_command()
    {
        var (input, context) = CreateInput("git status --short");

        var evaluation = await new ExecuteCommandPermissionPolicy().EvaluateAsync(
            input,
            context,
            CancellationToken.None);

        evaluation.RequestFingerprint.Should().NotBeNullOrWhiteSpace();
        evaluation.Presentation.Should().BeOfType<ExecuteCommandPermissionPresentation>();
        evaluation.Choices.Items.Select(choice => choice.Id).Should().Contain("allow_exact");
        evaluation.Choices.Items.Single(choice => choice.Id == "allow_exact").Persistence.Should()
            .Match<PermissionPersistenceProposal>(proposal =>
                proposal.Kind == PermissionPersistenceKind.ValidatedRule &&
                proposal.RuleTypeId == ExecuteCommandPermissionPolicy.RuleTypeId &&
                proposal.CanonicalRule.HasValue);
    }

    [Fact]
    public async Task Validated_rule_matches_only_the_policy_analyzed_authority()
    {
        var policy = new ExecuteCommandPermissionPolicy();
        var (approvedInput, approvedContext) = CreateInput("git status --short");
        var evaluation = await policy.EvaluateAsync(
            approvedInput,
            approvedContext,
            CancellationToken.None);
        var proposal = evaluation.Choices.Items.Single(choice => choice.Id == "allow_exact").Persistence!;

        policy.MatchesValidatedRule(
            approvedInput,
            approvedContext,
            proposal.RuleTypeId!,
            proposal.CanonicalRule!.Value,
            PermissionDecisionKind.Allow).Should().BeTrue();

        var (differentInput, differentContext) = CreateInput("git reset --hard");
        policy.MatchesValidatedRule(
            differentInput,
            differentContext,
            proposal.RuleTypeId!,
            proposal.CanonicalRule.Value,
            PermissionDecisionKind.Allow).Should().BeFalse();
    }

    private static (ValidatedPermissionInput Input, PermissionEvaluationContext Context) CreateInput(
        string command)
    {
        var request = JsonSerializer.SerializeToElement(new
        {
            action = "run",
            command,
            executionMode = "Synchronous"
        });
        var arguments = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement>
        {
            ["request"] = request
        });
        var invocation = new ResolvedFunctionInvocation
        {
            Action = "run",
            Mode = AgentInvocationMode.Synchronous,
            Policy = AgentInvocationModePolicy.SynchronousOnly,
            Handling = AgentInvocationModeHandling.Runtime,
            ValidatedAction = new ValidatedFunctionAction
            {
                Action = "run",
                CanonicalJson = request
            },
            IngressProvenance = FunctionArgumentIngressProvenance.Canonicalized
        };
        var input = new ValidatedPermissionInput(arguments, invocation);
        var context = new PermissionEvaluationContext
        {
            FunctionName = nameof(CodingToolHarness.ExecuteCommand),
            Action = "run",
            FunctionCallId = "call-1",
            Scope = "coding/execute-command/run",
            Input = input,
            RunConfig = CreateRunConfig(),
            Services = EmptyServiceProvider.Instance
        };
        return (input, context);
    }

    private static AgentRunConfig CreateRunConfig()
    {
        var root = Path.GetFullPath(Directory.GetCurrentDirectory());
        return new AgentRunConfig
        {
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object>
                {
                    [AgentWorkspace.ContextKey] = new AgentWorkspace(
                        "default",
                        root,
                        [new AgentWorkspaceRoot("default", root)])
                }
            }
        };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();
        public object? GetService(Type serviceType) => null;
    }
}
