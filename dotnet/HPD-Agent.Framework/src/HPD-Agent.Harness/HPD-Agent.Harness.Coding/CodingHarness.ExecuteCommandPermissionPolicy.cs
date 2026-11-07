using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Permissions;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding;

/// <summary>Client-safe command security evidence without a policy-owned UI contract.</summary>
public sealed record ExecuteCommandPermissionEvidence(
    ExecuteCommandPermissionPlan Plan,
    ExecuteCommandPermissionRuleDiagnostics RuleDiagnostics);

/// <summary>
/// Evaluates ExecuteCommand's semantic command plan while the framework owns mediation and grants.
/// </summary>
[PermissionPolicyDefinition("hpd.coding.execute-command.v2")]
public sealed class ExecuteCommandPermissionPolicy :
    IPermissionPolicy,
    IValidatedPermissionRulePolicy
{
    /// <summary>The stable canonical rule payload type.</summary>
    public const string RuleTypeId = "hpd.coding.execute-command.rule-set.v2";

    private static readonly ExecuteCommandOptions DefaultOptions = new();

    /// <inheritdoc />
    public ValueTask<PermissionPolicyResult> EvaluateAsync(
        ValidatedPermissionInput input,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Analyze(input, context.RunConfig);
        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, []);
        var legal = ExecuteCommandPermissionChoiceBuilder.Build(plan, match.MatchingRules);
        var choices = legal.Select(choice => new PermissionChoice(choice.Id, choice.Label,
            choice is FeedbackChoice ? PermissionDecisionKind.Feedback : choice is DenyChoice ? PermissionDecisionKind.Deny : PermissionDecisionKind.Allow,
            choice is FeedbackChoice ? PermissionDeniedBehavior.ReturnToModel : null)).ToArray();
        var mappings = legal.OfType<PersistRuleChoice>().ToDictionary(choice => choice.Id,
            choice => new PermissionPersistenceProposal
            {
                Kind = PermissionPersistenceKind.ValidatedRule, RuleTypeId = RuleTypeId,
                CanonicalRule = CreateCanonicalRule(choice)
            }, StringComparer.Ordinal);
        return ValueTask.FromResult(new PermissionPolicyResult
        {
            Outcome = new PermissionAsk("Review the command and its security effects.", ToFrameworkRisk(plan.Risk),
                new PermissionResource(plan.Command.Value, plan.Command.Value), choices),
            Evidence = new PermissionEvidence("hpd.coding.execute-command", "1",
                JsonSerializer.SerializeToElement(new ExecuteCommandPermissionEvidence(plan, match.Diagnostics),
                    CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionEvidence)),
            PersistenceByChoice = mappings
        });
    }

    public bool MatchesStoredDecision(ValidatedPermissionInput input,
        PermissionEvaluationContext context, PermissionDecisionRecord record)
    {
        if (record.RuleTypeId != RuleTypeId || record.CanonicalRule is not { } rule) return false;
        ExecuteCommandPermissionRule[]? rules;
        try { rules = JsonSerializer.Deserialize(rule, CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionRuleArray); }
        catch (JsonException) { return false; }
        if (rules is null || rules.Length == 0 || rules.Any(candidate =>
            !ExecuteCommandPermissionRuleValidator.ValidatePersistedRuleForCurrentWorkspace(candidate, context.RunConfig).Valid)) return false;
        return ExecuteCommandPermissionRuleMatcher.Match(Analyze(input, context.RunConfig), rules).Decision is
            { Behavior: ExecuteCommandPermissionBehavior.Allow };
    }

    private static ExecuteCommandPermissionPlan Analyze(
        ValidatedPermissionInput input,
        AgentRunConfig runConfig) =>
        ExecuteCommandPermissionAnalyzer.Analyze(
            new Dictionary<string, object?>
            {
                ["request"] = input.GetRequiredValue("request")
            },
            runConfig,
            DefaultOptions);

    private static JsonElement CreateCanonicalRule(PersistRuleChoice choice)
    {
        IReadOnlyList<ExecuteCommandPermissionRule> rules =
            choice.Proposal is SegmentRuleBundleProposal bundle
                ? bundle.SegmentRules
                : [choice.Proposal.Rule];
        return JsonSerializer.SerializeToElement(
            rules,
            CodingToolHarnessJsonContext.Default.ExecuteCommandPermissionRuleReadOnlyList);
    }

    private static PermissionRisk ToFrameworkRisk(ExecuteCommandPermissionRisk risk)
    {
        if ((risk & (ExecuteCommandPermissionRisk.Destructive |
                     ExecuteCommandPermissionRisk.PrivilegeEscalation |
                     ExecuteCommandPermissionRisk.Unsandboxed)) != 0)
            return PermissionRisk.Critical;
        if ((risk & (ExecuteCommandPermissionRisk.PathSensitiveWrite |
                     ExecuteCommandPermissionRisk.UnsafeRedirectionTarget |
                     ExecuteCommandPermissionRisk.OutsideWorkspaceReference)) != 0)
            return PermissionRisk.High;
        return risk == ExecuteCommandPermissionRisk.None ? PermissionRisk.Low : PermissionRisk.Medium;
    }
}
