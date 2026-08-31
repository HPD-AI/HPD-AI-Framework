using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Permissions;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding;

/// <summary>Safe typed presentation for an analyzed ExecuteCommand permission request.</summary>
/// <param name="Plan">The constructor-free semantic command plan.</param>
/// <param name="RuleDiagnostics">Diagnostics produced while matching reusable command rules.</param>
[PermissionPresentation("hpd.coding.execute-command")]
public sealed record ExecuteCommandPermissionPresentation(
    ExecuteCommandPermissionPlan Plan,
    ExecuteCommandPermissionRuleDiagnostics RuleDiagnostics,
    IReadOnlyList<ExecuteCommandPermissionChoice> Choices);

/// <summary>
/// Evaluates ExecuteCommand's semantic command plan while the framework owns mediation and grants.
/// </summary>
public sealed class ExecuteCommandPermissionPolicy :
    PermissionPolicy<ExecuteCommandPermissionPresentation>,
    IValidatedPermissionRulePolicy
{
    /// <summary>The stable canonical rule payload type.</summary>
    public const string RuleTypeId = "hpd.coding.execute-command.rule-set.v2";

    private static readonly ExecuteCommandOptions DefaultOptions = new();

    /// <inheritdoc />
    public override ValueTask<PermissionEvaluation> EvaluateAsync(
        ValidatedPermissionInput input,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Analyze(input, context.RunConfig);
        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, []);
        var choices = ExecuteCommandPermissionChoiceBuilder.Build(plan, match.MatchingRules)
            .Select(ToFrameworkChoice)
            .ToArray();

        return ValueTask.FromResult(new PermissionEvaluation
        {
            PolicyId = "hpd.coding.execute-command",
            PolicyRevision = "2",
            Scope = context.Scope,
            Title = plan.Action == ExecuteCommandAction.Run
                ? "Allow this command?"
                : $"Allow ExecuteCommand {plan.Action}?",
            Summary = plan.Command.Value,
            Risk = ToFrameworkRisk(plan.Risk),
            Choices = new PermissionChoiceSet { Items = choices },
            RequestFingerprint = plan.Fingerprint.Value,
            Presentation = new ExecuteCommandPermissionPresentation(
                plan,
                match.Diagnostics,
                ExecuteCommandPermissionChoiceBuilder.Build(plan, match.MatchingRules))
        });
    }

    /// <inheritdoc />
    public bool MatchesValidatedRule(
        ValidatedPermissionInput input,
        PermissionEvaluationContext context,
        string ruleTypeId,
        JsonElement canonicalRule,
        PermissionDecisionKind storedDecision)
    {
        if (storedDecision != PermissionDecisionKind.Allow ||
            !string.Equals(ruleTypeId, RuleTypeId, StringComparison.Ordinal))
            return false;

        ExecuteCommandPermissionRule[]? rules;
        try
        {
            rules = canonicalRule.Deserialize<ExecuteCommandPermissionRule[]>();
        }
        catch (JsonException)
        {
            return false;
        }

        if (rules is null || rules.Length == 0 ||
            rules.Any(rule => !ExecuteCommandPermissionRuleValidator.ValidatePersistedRuleForCurrentWorkspace(
                rule,
                context.RunConfig).Valid))
            return false;

        var match = ExecuteCommandPermissionRuleMatcher.Match(Analyze(input, context.RunConfig), rules);
        return match.Decision is { Behavior: ExecuteCommandPermissionBehavior.Allow };
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

    private static PermissionChoiceDescriptor ToFrameworkChoice(ExecuteCommandPermissionChoice choice) =>
        choice switch
        {
            PersistRuleChoice persisted => new PermissionChoiceDescriptor
            {
                Id = persisted.Id,
                Label = persisted.Label,
                Decision = PermissionDecisionKind.Allow,
                Persistence = new PermissionPersistenceProposal
                {
                    Kind = PermissionPersistenceKind.ValidatedRule,
                    RuleTypeId = RuleTypeId,
                    CanonicalRule = JsonSerializer.SerializeToElement(
                        persisted.Proposal is SegmentRuleBundleProposal bundle
                            ? bundle.SegmentRules
                            : [persisted.Proposal.Rule])
                }
            },
            FeedbackChoice => new PermissionChoiceDescriptor
            {
                Id = choice.Id,
                Label = choice.Label,
                Decision = PermissionDecisionKind.Feedback,
                DeniedBehavior = PermissionDeniedBehavior.ReturnToModel
            },
            DenyChoice => new PermissionChoiceDescriptor
            {
                Id = choice.Id,
                Label = choice.Label,
                Decision = PermissionDecisionKind.Deny
            },
            _ => new PermissionChoiceDescriptor
            {
                Id = choice.Id,
                Label = choice.Label,
                Decision = PermissionDecisionKind.Allow
            }
        };

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

/// <summary>Maps ExecuteCommand's rich typed request protocol to one normalized framework decision.</summary>
public sealed class ExecuteCommandPermissionInteraction : IPermissionInteraction
{
    /// <inheritdoc />
    public async ValueTask<PermissionDecision> RequestAsync(
        PermissionInteractionContext context,
        PermissionEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        var presentation = evaluation.Presentation as ExecuteCommandPermissionPresentation ??
            throw new InvalidOperationException("ExecuteCommand permission presentation is unavailable.");
        var response = await context.RequestAsync<
            ExecuteCommandPermissionRequestEvent,
            ExecuteCommandPermissionResponseEvent>(
            new ExecuteCommandPermissionRequestEvent(
                context.PermissionId,
                nameof(ExecuteCommandPermissionInteraction),
                context.FunctionCallId,
                presentation.Plan,
                [],
                presentation.RuleDiagnostics,
                presentation.Choices),
            cancellationToken).ConfigureAwait(false);
        var choice = evaluation.Choices.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, response.ChoiceId, StringComparison.Ordinal)) ??
            throw new InvalidOperationException(
                $"ExecuteCommand permission response selected unknown choice '{response.ChoiceId}'.");
        return new PermissionDecision
        {
            Kind = choice.Decision,
            ChoiceId = choice.Id,
            Feedback = response.FeedbackText,
            Reason = response.FeedbackText
        };
    }
}
