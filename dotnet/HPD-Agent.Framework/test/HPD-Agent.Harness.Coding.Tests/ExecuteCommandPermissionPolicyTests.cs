using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Tests;

[Collection(CurrentDirectoryCollection.Name)]
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

        var ask = evaluation.Outcome.Should().BeOfType<PermissionAsk>().Subject;
        evaluation.Evidence.Should().NotBeNull();
        evaluation.Evidence!.Kind.Should().Be("hpd.coding.execute-command");
        ask.Choices.Select(choice => choice.Id).Should().Contain("allow_exact");
        evaluation.PersistenceByChoice!["allow_exact"].Should()
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
        var proposal = evaluation.PersistenceByChoice!["allow_exact"];
        var record = new PermissionDecisionRecord
        {
            RecordId = "record-1",
            PermissionId = "permission-1",
            Key = new PermissionDecisionKey(
                "coding", approvedContext.Authority, "run", "hpd.coding.execute-command.v2"),
            Persistence = PermissionPersistenceKind.ValidatedRule,
            RuleTypeId = proposal.RuleTypeId,
            CanonicalRule = proposal.CanonicalRule,
            CreatedAt = DateTimeOffset.UtcNow
        };

        policy.MatchesStoredDecision(
            approvedInput,
            approvedContext,
            record).Should().BeTrue();

        var (differentInput, differentContext) = CreateInput("git reset --hard");
        policy.MatchesStoredDecision(
            differentInput,
            differentContext,
            record).Should().BeFalse();
    }

    [Theory]
    [InlineData("git status --short 2>&1 | head -40")]
    [InlineData("git diff --stat; echo '---'; git diff Program.cs | head -120; echo '---'; git diff ../../global.json")]
    public async Task Safe_compound_read_commands_offer_a_session_rule(string command)
    {
        var (input, context) = CreateInput(command);

        var evaluation = await new ExecuteCommandPermissionPolicy().EvaluateAsync(
            input,
            context,
            CancellationToken.None);

        var ask = evaluation.Outcome.Should().BeOfType<PermissionAsk>().Subject;
        ask.Choices.Should().Contain(choice =>
            choice.Id == "allow_similar" &&
            choice.Label == "Allow similar commands this session");
        evaluation.PersistenceByChoice.Should().ContainKey("allow_similar");
        evaluation.Evidence!.Payload.GetProperty("plan").GetProperty("planKind").GetString()
            .Should().Be("segmented");
        evaluation.Evidence.Payload.GetProperty("plan").GetProperty("unsupportedShellFeatures")
            .EnumerateArray()
            .Select(static value => value.GetInt32())
            .Should().NotContain((int)ExecuteCommandUnsupportedShellFeature.OutputRedirection);
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
            Authority = "coding/execute-command/run",
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
                        [new AgentWorkspaceRoot("default", root, AgentWorkspaceAccess.ReadWrite)])
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
