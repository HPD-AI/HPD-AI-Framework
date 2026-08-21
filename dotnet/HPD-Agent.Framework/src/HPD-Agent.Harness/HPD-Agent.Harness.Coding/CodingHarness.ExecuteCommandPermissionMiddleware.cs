using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Security;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.Security;
using HPD.Environment.Contracts;
using HPD.Events;
using Microsoft.Extensions.AI;

namespace HPDOS.ToolHarnesses.Middleware;

public sealed class ExecuteCommandPermissionMiddleware : IToolHarnessMiddleware
{
    private const string MiddlewareName = nameof(ExecuteCommandPermissionMiddleware);
    private const int MaxSegments = 50;
    private static readonly ExecuteCommandOptions DefaultOptions = new();

    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        context.UpdateMiddlewareState<ExecuteCommandBatchPermissionStateData>(_ => new ExecuteCommandBatchPermissionStateData());
        return Task.CompletedTask;
    }

    public async Task BeforeParallelBatchAsync(BeforeParallelBatchContext context, CancellationToken cancellationToken)
    {
        if (context.RunConfig.Security.Approval == AgentApprovalPolicy.AutoApprove)
            return;

        foreach (var call in context.ParallelFunctions)
        {
            if (!IsExecuteCommand(call.FunctionName))
                continue;

            var result = await CheckPermissionAsync(
                context,
                context.RunConfig,
                call.FunctionName,
                call.CallId,
                call.Arguments,
                cancellationToken).ConfigureAwait(false);

            context.UpdateMiddlewareState<ExecuteCommandBatchPermissionStateData>(state =>
                state.WithDecision(result.Fingerprint, result.Decision));
        }
    }

    public async Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
    {
        if (!IsExecuteCommand(context.Function?.Name))
            return;

        if (context.RunConfig.Security.Approval == AgentApprovalPolicy.AutoApprove)
            return;

        var plan = ExecuteCommandPermissionAnalyzer.Analyze(context.Arguments, context.RunConfig, DefaultOptions);
        if (plan is UntrustedCommandPermissionPlan { InvalidRequest: true } invalidPlan)
        {
            await ApplyDecisionAsync(
                    context,
                    ExecuteCommandPermissionDecision.InvalidArguments(plan.Fingerprint.Value, invalidPlan.FailureReason),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (plan.Action != ExecuteCommandAction.Run)
            return;

        var batchState = context.GetMiddlewareState<ExecuteCommandBatchPermissionStateData>() ?? new();
        if (batchState.DecisionsByFingerprint.TryGetValue(plan.Fingerprint.Value, out var batchDecision))
        {
            await ApplyDecisionAsync(context, batchDecision, cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = await CheckPermissionAsync(
            context,
            context.RunConfig,
            context.Function!.Name,
            context.FunctionCallId,
            context.Arguments,
            cancellationToken).ConfigureAwait(false);

        context.UpdateMiddlewareState<ExecuteCommandBatchPermissionStateData>(state =>
            state.WithDecision(result.Fingerprint, result.Decision));
        await ApplyDecisionAsync(context, result.Decision, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ExecuteCommandPermissionCheckResult> CheckPermissionAsync(
        HookContext context,
        AgentRunConfig runConfig,
        string functionName,
        string callId,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var plan = ExecuteCommandPermissionAnalyzer.Analyze(arguments, runConfig, DefaultOptions);
        if (plan is UntrustedCommandPermissionPlan { InvalidRequest: true } invalidPlan)
        {
            return new ExecuteCommandPermissionCheckResult(
                plan.Fingerprint.Value,
                ExecuteCommandPermissionDecision.InvalidArguments(plan.Fingerprint.Value, invalidPlan.FailureReason));
        }

        if (plan.Action != ExecuteCommandAction.Run)
        {
            return new ExecuteCommandPermissionCheckResult(
                plan.Fingerprint.Value,
                ExecuteCommandPermissionDecision.AllowOnce(plan.Fingerprint.Value));
        }

        var state = context.GetMiddlewareState<ExecuteCommandPermissionStateData>() ?? new();
        var match = ExecuteCommandPermissionRuleMatcher.Match(plan, state.Rules);

        if (match.Decision is { Behavior: ExecuteCommandPermissionBehavior.Deny })
        {
            var reason = $"ExecuteCommand denied by rule {match.Decision.Id}.";
            return new ExecuteCommandPermissionCheckResult(
                plan.Fingerprint.Value,
                ExecuteCommandPermissionDecision.Deny(plan.Fingerprint.Value, reason, "denied_by_rule"));
        }

        if (match.Decision is { Behavior: ExecuteCommandPermissionBehavior.Ask })
        {
            // Ask is a terminal rule decision that deliberately shadows Allow and forces the prompt path.
        }
        else if (match.Decision is { Behavior: ExecuteCommandPermissionBehavior.Allow })
        {
            return new ExecuteCommandPermissionCheckResult(
                plan.Fingerprint.Value,
                ExecuteCommandPermissionDecision.AllowOnce(plan.Fingerprint.Value));
        }

        var choices = ExecuteCommandPermissionChoiceBuilder.Build(plan, match.MatchingRules);
        var permissionId = Guid.NewGuid().ToString("N");
        ExecuteCommandPermissionResponseEvent response;
        try
        {
            response = await context.RequestAsync<ExecuteCommandPermissionRequestEvent, ExecuteCommandPermissionResponseEvent>(
                    new ExecuteCommandPermissionRequestEvent(
                        permissionId,
                        MiddlewareName,
                        callId,
                        plan,
                        match.MatchingRules,
                        match.Diagnostics,
                        choices))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new ExecuteCommandPermissionCheckResult(
                plan.Fingerprint.Value,
                ExecuteCommandPermissionDecision.Deny(plan.Fingerprint.Value, "ExecuteCommand permission request timed out."));
        }
        catch (OperationCanceledException)
        {
            return new ExecuteCommandPermissionCheckResult(
                plan.Fingerprint.Value,
                ExecuteCommandPermissionDecision.Deny(plan.Fingerprint.Value, "ExecuteCommand permission request was cancelled."));
        }

        var selected = choices.FirstOrDefault(choice => string.Equals(choice.Id, response.ChoiceId, StringComparison.Ordinal));
        if (selected is null)
        {
            return new ExecuteCommandPermissionCheckResult(
                plan.Fingerprint.Value,
                ExecuteCommandPermissionDecision.Deny(plan.Fingerprint.Value, "ExecuteCommand permission response selected an unavailable choice."));
        }

        switch (selected)
        {
            case AllowOnceChoice allow:
                return new ExecuteCommandPermissionCheckResult(
                    plan.Fingerprint.Value,
                    ExecuteCommandPermissionDecision.AllowOnce(plan.Fingerprint.Value));

            case PersistRuleChoice persist:
                var rulesToPersist = persist.Proposal switch
                {
                    SegmentRuleBundleProposal bundle => bundle.SegmentRules,
                    _ => [persist.Proposal.Rule]
                };
                context.UpdateMiddlewareState<ExecuteCommandPermissionStateData>(current =>
                {
                    foreach (var rule in rulesToPersist)
                        current = current.WithValidatedRule(rule with { CreatedByPromptId = permissionId });
                    return current;
                });
                foreach (var rule in rulesToPersist)
                {
                    await context.PublishAsync(new ExecuteCommandPermissionRulePersistedEvent(
                        permissionId,
                        MiddlewareName,
                        callId,
                        rule.Id,
                        rule.Pattern,
                        rule.Behavior,
                        rule.MatchKind,
                        BuildAuditDetails(
                            plan,
                            rule,
                            decision: "persisted",
                            persistedRuleIds: rulesToPersist.Select(item => item.Id).ToArray())), cancellationToken)
                        .ConfigureAwait(false);
                }
                return new ExecuteCommandPermissionCheckResult(
                    plan.Fingerprint.Value,
                    ExecuteCommandPermissionDecision.AllowOnce(plan.Fingerprint.Value));

            case FeedbackChoice:
                var feedback = string.IsNullOrWhiteSpace(response.FeedbackText)
                    ? "User denied ExecuteCommand and did not provide alternate instructions."
                    : response.FeedbackText.Trim();
                return new ExecuteCommandPermissionCheckResult(
                    plan.Fingerprint.Value,
                    ExecuteCommandPermissionDecision.Deny(
                        plan.Fingerprint.Value,
                        feedback,
                        deniedBehavior: PermissionDeniedBehavior.ReturnToModel));

            default:
                return new ExecuteCommandPermissionCheckResult(
                    plan.Fingerprint.Value,
                    ExecuteCommandPermissionDecision.Deny(plan.Fingerprint.Value, "User denied ExecuteCommand."));
        }
    }

    private static ExecuteCommandPermissionAuditDetails BuildAuditDetails(
        ExecuteCommandPermissionPlan plan,
        ExecuteCommandPermissionRule? matchedRule,
        string decision,
        IReadOnlyList<string> persistedRuleIds)
        => new()
        {
            AnalyzerVersion = plan.AnalyzerVersion,
            NormalizationVersion = plan.NormalizationVersion,
            RuleSchemaVersion = ExecuteCommandPermissionAnalyzerVersions.RuleSchema,
            Shell = plan.Shell,
            Workspace = plan.Workspace,
            MatchedRuleId = matchedRule?.Id,
            MatchedRuleSchemaVersion = matchedRule?.RuleSchemaVersion,
            Decision = decision,
            TrustLevel = plan.TrustLevel,
            Risk = plan.Risk,
            UnsupportedShellFeatures = plan.UnsupportedShellFeatures,
            PersistedRuleIds = persistedRuleIds
        };

    private static bool IsExecuteCommand(string? functionName)
        => string.Equals(functionName, nameof(CodingToolHarness.ExecuteCommand), StringComparison.Ordinal);

    private static async Task ApplyDecisionAsync(
        BeforeFunctionContext context,
        ExecuteCommandPermissionDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.Approved)
            return;

        context.BlockExecution = true;
        var reason = SecurityElementEscape(decision.Reason ?? "Permission denied.");
        context.OverrideResult = string.Equals(decision.ReasonCode, "invalid_arguments", StringComparison.Ordinal)
            ? $"""
                <execute_command_error kind="invalid_arguments">
                  {reason}
                </execute_command_error>
                """
            : $"""
                <execute_command_permission_denied reason="{SecurityElementEscape(decision.ReasonCode)}">
                  {reason}
                </execute_command_permission_denied>
                """;
        if (decision.DeniedBehavior == PermissionDeniedBehavior.InterruptTurn)
        {
            await InterruptDeniedPermissionAsync(
                    context,
                    decision.Reason ?? "ExecuteCommand permission denied.",
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task InterruptDeniedPermissionAsync(
        BeforeFunctionContext context,
        string reason,
        CancellationToken cancellationToken)
    {
        context.EventFlows?.InterruptFlow(context.FunctionCallId);
        await context.PublishAsync(
                new InterruptionHandledEvent(
                    context.FunctionCallId,
                    reason,
                    InterruptionSource.Middleware),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string SecurityElementEscape(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private sealed record ExecuteCommandPermissionCheckResult(
        string Fingerprint,
        ExecuteCommandPermissionDecision Decision);

    internal static class ExecuteCommandPermissionAnalyzer
    {
        public static ExecuteCommandPermissionPlan Analyze(
            IReadOnlyDictionary<string, object?> arguments,
            AgentRunConfig runConfig,
            ExecuteCommandOptions options,
            ExecuteCommandShellScope? shellOverride = null)
        {
            var requestArguments = GetRequestArguments(arguments);
            var action = GetString(requestArguments, "action") switch
            {
                "run" => ExecuteCommandAction.Run,
                "listBackground" => ExecuteCommandAction.ListBackground,
                "readOutput" => ExecuteCommandAction.ReadOutput,
                "stop" => ExecuteCommandAction.Stop,
                _ => ExecuteCommandAction.Run
            };
            var command = GetString(requestArguments, "command");
            var operationId = GetString(requestArguments, "operationId");
            var workingDirectory = GetString(requestArguments, "workingDirectory");
            var timeoutMilliseconds = GetInt(requestArguments, "timeoutMilliseconds", 120_000);
            var startsInBackground = GetEnum(
                requestArguments,
                "executionMode",
                CommandExecutionMode.Synchronous) == CommandExecutionMode.Background;
            var tailLines = GetInt(requestArguments, "tailLines", 200);
            var delayMilliseconds = GetInt(requestArguments, "delayMilliseconds", 0);
            var environment = GetStringDictionary(requestArguments, "environment");

            var normalized = CodingToolHarness.NormalizeExecuteCommandRequest(
                action,
                command,
                operationId,
                workingDirectory,
                timeoutMilliseconds,
                startsInBackground,
                tailLines,
                delayMilliseconds,
                environment,
                runConfig,
                options);

            if (normalized.Error is { } error)
            {
                var requestedSandbox = TryGetRequestedSandbox(runConfig);
                return new UntrustedCommandPermissionPlan
                {
                    AnalyzerVersion = ExecuteCommandPermissionAnalyzerVersions.Analyzer,
                    NormalizationVersion = ExecuteCommandPermissionAnalyzerVersions.Normalization,
                    Fingerprint = Fingerprint("invalid", action.ToString(), command ?? string.Empty, error.Message),
                    Action = action,
                    Command = new RawCommandText(command ?? string.Empty),
                    NormalizedCommand = new NormalizedCommandText(command?.Trim() ?? string.Empty),
                    Shell = shellOverride ?? GetShellScope(),
                    WorkingDirectory = error.WorkingDirectory ?? string.Empty,
                    Workspace = ExecuteCommandPermissionWorkspaceScope.From(runConfig, error.WorkingDirectory),
                    RequestedSandbox = requestedSandbox,
                    FilesystemEffects = [],
                    NetworkEffects = [],
                    StartsInBackground = startsInBackground,
                    Risk = ExecuteCommandPermissionRisk.UnknownOrUnparseable,
                    FailureReason = error.Message,
                    InvalidRequest = true
                };
            }

            var request = normalized.Request!;
            var shell = shellOverride ?? GetShellScope();
            var sandbox = CodingSandboxRuntime.Capture(runConfig);
            var workspace = ExecuteCommandPermissionWorkspaceScope.From(runConfig, request.WorkingDirectory);
            if (request.Action != ExecuteCommandAction.Run)
            {
                var nonRunFingerprint = Fingerprint(
                    request.Action.ToString(),
                    shell.Family.ToString(),
                    request.WorkingDirectory,
                    request.OperationId ?? string.Empty,
                    sandbox.Canonicalize(request.WorkingDirectory));
                return new ExecuteCommandPlanBase
                {
                    AnalyzerVersion = ExecuteCommandPermissionAnalyzerVersions.Analyzer,
                    NormalizationVersion = ExecuteCommandPermissionAnalyzerVersions.Normalization,
                    Fingerprint = nonRunFingerprint,
                    Action = request.Action,
                    Command = new RawCommandText(request.Command),
                    NormalizedCommand = new NormalizedCommandText(request.Command.Trim()),
                    Shell = shell,
                    WorkingDirectory = request.WorkingDirectory,
                    Workspace = workspace,
                    RequestedSandbox = sandbox,
                    FilesystemEffects = [],
                    NetworkEffects = [],
                    StartsInBackground = false,
                    Risk = ExecuteCommandPermissionRisk.None
                }.ToNonRun($"{request.Action} is governed by same-session background command ownership, not shell command permission.");
            }

            var analysis = ExecuteCommandShellAnalyzer.Analyze(new RawCommandText(request.Command), shell);
            var risk = analysis.Risk;
            if (request.StartsInBackground)
                risk |= ExecuteCommandPermissionRisk.BackgroundProcess;
            if (!sandbox.IsEnforced)
                risk |= ExecuteCommandPermissionRisk.Unsandboxed;
            if (sandbox.Filesystem.Count > 0 ||
                sandbox.Network.Mode != NetworkEgressMode.Blocked ||
                sandbox.Interactive.AllowPty ||
                sandbox.Interactive.AllowLocalBinding ||
                sandbox.Interactive.AllowedMachLookups.Count > 0)
            {
                risk |= ExecuteCommandPermissionRisk.AdditionalSandboxPermissions;
            }

            var filesystemEffects = ExecuteCommandPathAnalyzer.GetEffects(analysis, request.WorkingDirectory, workspace.RootPath, sandbox);
            var networkEffects = ExecuteCommandPathAnalyzer.GetNetworkEffects(analysis, sandbox);
            if (filesystemEffects.Any(effect => !effect.WithinWorkspace))
                risk |= ExecuteCommandPermissionRisk.OutsideWorkspaceReference;

            var fingerprint = Fingerprint(
                action.ToString(),
                shell.Family.ToString(),
                request.WorkingDirectory,
                request.Command,
                sandbox.Canonicalize(request.WorkingDirectory),
                risk.ToString());

            var basePlan = new ExecuteCommandPlanBase
            {
                AnalyzerVersion = ExecuteCommandPermissionAnalyzerVersions.Analyzer,
                NormalizationVersion = ExecuteCommandPermissionAnalyzerVersions.Normalization,
                Fingerprint = fingerprint,
                Action = action,
                Command = new RawCommandText(request.Command),
                NormalizedCommand = new NormalizedCommandText(request.Command.Trim()),
                Shell = shell,
                WorkingDirectory = request.WorkingDirectory,
                Workspace = workspace,
                RequestedSandbox = sandbox,
                FilesystemEffects = filesystemEffects,
                NetworkEffects = networkEffects,
                StartsInBackground = request.StartsInBackground,
                Risk = risk,
                UnsupportedShellFeatures = analysis.UnsupportedFeatures,
                ShellAnalyzerName = analysis.AnalyzerName,
                ShellUnsupportedFeatureReason = analysis.UnsupportedFeatureReason
            };

            if (analysis.TrustLevel == ExecuteCommandAnalysisTrustLevel.Untrusted)
                return basePlan.ToUntrusted("The command uses shell syntax HPD cannot safely model for remembered permission.");

            if (analysis.TrustLevel == ExecuteCommandAnalysisTrustLevel.ReviewOnly)
                return basePlan.ToReviewOnly(analysis.Segments);

            if (analysis.Segments.Count > 1)
            {
                var segmentRules = analysis.Segments
                    .Where(segment => segment.Readiness >= ExecuteCommandPolicyReadiness.PrefixAllowAllowed && segment.SafePrefix is not null)
                    .Select(segment => CreateRule(
                        ExecuteCommandPermissionBehavior.Allow,
                        ExecuteCommandPermissionMatchKind.Prefix,
                        segment.SafePrefix!,
                        shell,
                        sandbox,
                        request.WorkingDirectory,
                        workspace,
                        ExecuteCommandAnalysisTrustLevel.Simple,
                        risk))
                    .ToArray();

                if (segmentRules.Length == analysis.Segments.Count)
                {
                    return basePlan.ToSegmented(
                        analysis.Segments,
                        new SegmentRuleBundleProposal
                        {
                            Rule = CreateRule(
                                ExecuteCommandPermissionBehavior.Allow,
                                ExecuteCommandPermissionMatchKind.Exact,
                                request.Command,
                                shell,
                                sandbox,
                                request.WorkingDirectory,
                                workspace,
                                ExecuteCommandAnalysisTrustLevel.Segmented,
                                risk),
                            UserLabel = "Always allow similar commands",
                            SegmentRules = segmentRules
                        });
                }

                return basePlan.ToReviewOnly(analysis.Segments);
            }

            var commandPlan = analysis.Segments[0];
            var readiness = commandPlan.Readiness;
            var exact = readiness >= ExecuteCommandPolicyReadiness.ExactAllowOnly
                ? new ExactAllowRuleProposal
                {
                    Rule = CreateRule(
                        ExecuteCommandPermissionBehavior.Allow,
                        ExecuteCommandPermissionMatchKind.Exact,
                        request.Command,
                        shell,
                        sandbox,
                        request.WorkingDirectory,
                        workspace,
                        ExecuteCommandAnalysisTrustLevel.Simple,
                        risk),
                    UserLabel = "Always allow this exact command"
                }
                : null;
            var prefix = readiness >= ExecuteCommandPolicyReadiness.PrefixAllowAllowed && commandPlan.SafePrefix is not null
                ? new PrefixAllowRuleProposal
                {
                    Rule = CreateRule(
                        ExecuteCommandPermissionBehavior.Allow,
                        ExecuteCommandPermissionMatchKind.Prefix,
                        commandPlan.SafePrefix,
                        shell,
                        sandbox,
                        request.WorkingDirectory,
                        workspace,
                        ExecuteCommandAnalysisTrustLevel.Simple,
                        risk),
                    UserLabel = "Always allow similar commands",
                    Prefix = new SafeCommandPrefix(commandPlan.SafePrefix)
                }
                : null;

            if (exact is null)
                return basePlan.ToReviewOnly(analysis.Segments);

            return basePlan.ToSimple(commandPlan, exact, prefix);
        }

        private static ExecuteCommandPermissionRule CreateRule(
            ExecuteCommandPermissionBehavior behavior,
            ExecuteCommandPermissionMatchKind matchKind,
            string pattern,
            ExecuteCommandShellScope shell,
            AgentSandboxRuntime sandbox,
            string workingDirectory,
            ExecuteCommandPermissionWorkspaceScope workspace,
            ExecuteCommandAnalysisTrustLevel trustLevel,
            ExecuteCommandPermissionRisk risk)
            => new()
            {
                Id = $"ecpr_{Guid.NewGuid():N}",
                RuleSchemaVersion = ExecuteCommandPermissionAnalyzerVersions.RuleSchema,
                AnalyzerVersion = ExecuteCommandPermissionAnalyzerVersions.Analyzer,
                NormalizationVersion = ExecuteCommandPermissionAnalyzerVersions.Normalization,
                Behavior = behavior,
                MatchKind = matchKind,
                Pattern = pattern,
                Shell = shell,
                RequestedSandboxFingerprint = sandbox.Canonicalize(workingDirectory),
                Workspace = workspace,
                Risk = risk,
                MinimumTrustLevel = trustLevel,
                CreatedAt = DateTimeOffset.UtcNow
            };

        private static PermissionFingerprint Fingerprint(params string[] parts)
        {
            var input = string.Join("\u001f", parts);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return new PermissionFingerprint(Convert.ToHexString(bytes).ToLowerInvariant());
        }

        private static ExecuteCommandShellScope GetShellScope()
        {
            var env = EnvironmentContext.CreateCurrent();
            var shell = Path.GetFileName(env.ShellExecutable).ToLowerInvariant();
            var family = shell switch
            {
                "bash" => ExecuteCommandShellFamily.Bash,
                "zsh" => ExecuteCommandShellFamily.Zsh,
                "sh" or "dash" => ExecuteCommandShellFamily.Sh,
                "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe" => ExecuteCommandShellFamily.PowerShell,
                "cmd" or "cmd.exe" => ExecuteCommandShellFamily.Cmd,
                _ => ExecuteCommandShellFamily.Unknown
            };
            return new ExecuteCommandShellScope
            {
                Executable = env.ShellExecutable,
                Family = family
            };
        }

        private static AgentSandboxRuntime TryGetRequestedSandbox(AgentRunConfig runConfig)
        {
            try
            {
                return CodingSandboxRuntime.Capture(runConfig);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
            {
                return AgentSandboxRuntime.Capture(new AgentRunConfig());
            }
        }

        private static string? GetString(IReadOnlyDictionary<string, object?> arguments, string key)
            => arguments.TryGetValue(key, out var value) ? ConvertToString(value) : null;

        private static IReadOnlyDictionary<string, object?> GetRequestArguments(
            IReadOnlyDictionary<string, object?> arguments)
        {
            if (!arguments.TryGetValue("request", out var value) || value is null)
                return EmptyArguments;

            if (value is IReadOnlyDictionary<string, object?> readOnly)
                return readOnly;

            if (value is IDictionary<string, object?> dictionary)
                return new Dictionary<string, object?>(dictionary, StringComparer.Ordinal);

            if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
                return element.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => (object?)property.Value.Clone(),
                    StringComparer.Ordinal);

            return EmptyArguments;
        }

        private static readonly IReadOnlyDictionary<string, object?> EmptyArguments =
            new Dictionary<string, object?>(StringComparer.Ordinal);

        private static string? ConvertToString(object? value)
            => value switch
            {
                null => null,
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                JsonElement { ValueKind: JsonValueKind.Null } => null,
                _ => value.ToString()
            };

        private static int GetInt(IReadOnlyDictionary<string, object?> arguments, string key, int fallback)
            => arguments.TryGetValue(key, out var value)
                ? value switch
                {
                    int i => i,
                    long l => checked((int)l),
                    JsonElement { ValueKind: JsonValueKind.Number } element => element.GetInt32(),
                    string text when int.TryParse(text, out var parsed) => parsed,
                    _ => fallback
                }
                : fallback;

        private static bool GetBool(IReadOnlyDictionary<string, object?> arguments, string key, bool fallback)
            => arguments.TryGetValue(key, out var value)
                ? value switch
                {
                    bool b => b,
                    JsonElement { ValueKind: JsonValueKind.True } => true,
                    JsonElement { ValueKind: JsonValueKind.False } => false,
                    string text when bool.TryParse(text, out var parsed) => parsed,
                    _ => fallback
                }
                : fallback;

        private static TEnum GetEnum<TEnum>(IReadOnlyDictionary<string, object?> arguments, string key, TEnum fallback)
            where TEnum : struct, Enum
        {
            if (!arguments.TryGetValue(key, out var value))
                return fallback;
            var text = ConvertToString(value);
            return Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed) ? parsed : fallback;
        }

        private static IReadOnlyDictionary<string, string>? GetStringDictionary(
            IReadOnlyDictionary<string, object?> arguments,
            string key)
        {
            if (!arguments.TryGetValue(key, out var value) || value is null)
                return null;

            if (value is IReadOnlyDictionary<string, string> typed)
                return typed;

            if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                    values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                return values;
            }

            return null;
        }
    }
}

[MiddlewareState(Persistent = true, Scope = StateScope.Session)]
public sealed record ExecuteCommandPermissionStateData
{
    public IReadOnlyList<ExecuteCommandPermissionRule> Rules { get; init; } = [];
    public IReadOnlyList<ExecuteCommandInactivePermissionRule> InactiveRules { get; init; } = [];

    public ExecuteCommandPermissionStateData WithValidatedRule(ExecuteCommandPermissionRule rule)
    {
        var validation = ExecuteCommandPermissionRuleLifecycle.ValidatePersistedRule(rule);
        if (!validation.Valid)
            throw new InvalidOperationException($"ExecuteCommand permission rule '{rule.Id}' is invalid: {validation.Reason}");

        return WithRuleUnchecked(rule);
    }

    public ExecuteCommandPermissionRuleLifecycleOperationResult ImportRules(
        IEnumerable<ExecuteCommandPermissionRule> rules,
        AgentRunConfig runConfig)
    {
        var state = this;
        var issues = new List<ExecuteCommandPermissionRuleLifecycleIssue>();
        var auditRecords = new List<ExecuteCommandPermissionRuleLifecycleAuditRecord>();

        foreach (var rule in rules)
        {
            var validation = ExecuteCommandPermissionRuleLifecycle.ValidatePersistedRuleForCurrentWorkspace(rule, runConfig);
            if (!validation.Valid)
            {
                issues.Add(ExecuteCommandPermissionRuleLifecycleIssue.From(rule, validation.Reason!));
                state = state.WithInactiveRuleUnchecked(rule, validation.Reason!);
                auditRecords.Add(ExecuteCommandPermissionRuleLifecycleAuditRecord.Inactivated(
                    ExecuteCommandPermissionRuleLifecycleOperation.Import,
                    rule,
                    validation.Reason!));
                continue;
            }

            state = state.WithRuleUnchecked(rule);
            auditRecords.Add(ExecuteCommandPermissionRuleLifecycleAuditRecord.Activated(
                ExecuteCommandPermissionRuleLifecycleOperation.Import,
                rule));
        }

        return new ExecuteCommandPermissionRuleLifecycleOperationResult(state, issues, auditRecords);
    }

    public ExecuteCommandPermissionRuleLifecycleOperationResult ReplaceRule(
        string ruleId,
        ExecuteCommandPermissionRule replacement,
        AgentRunConfig runConfig)
    {
        var issues = new List<ExecuteCommandPermissionRuleLifecycleIssue>();
        var auditRecords = new List<ExecuteCommandPermissionRuleLifecycleAuditRecord>();

        if (string.IsNullOrWhiteSpace(ruleId))
        {
            issues.Add(ExecuteCommandPermissionRuleLifecycleIssue.From(replacement, "missing_rule_id"));
            auditRecords.Add(ExecuteCommandPermissionRuleLifecycleAuditRecord.Rejected(
                ExecuteCommandPermissionRuleLifecycleOperation.Replace,
                replacement,
                "missing_rule_id"));
            return new ExecuteCommandPermissionRuleLifecycleOperationResult(this, issues, auditRecords);
        }

        if (!string.Equals(ruleId, replacement.Id, StringComparison.Ordinal))
        {
            issues.Add(ExecuteCommandPermissionRuleLifecycleIssue.From(replacement, "rule_id_mismatch"));
            auditRecords.Add(ExecuteCommandPermissionRuleLifecycleAuditRecord.Rejected(
                ExecuteCommandPermissionRuleLifecycleOperation.Replace,
                replacement,
                "rule_id_mismatch"));
            return new ExecuteCommandPermissionRuleLifecycleOperationResult(this, issues, auditRecords);
        }

        if (!Rules.Any(rule => string.Equals(rule.Id, ruleId, StringComparison.Ordinal)))
        {
            issues.Add(ExecuteCommandPermissionRuleLifecycleIssue.From(replacement, "rule_not_found"));
            auditRecords.Add(ExecuteCommandPermissionRuleLifecycleAuditRecord.Rejected(
                ExecuteCommandPermissionRuleLifecycleOperation.Replace,
                replacement,
                "rule_not_found"));
            return new ExecuteCommandPermissionRuleLifecycleOperationResult(this, issues, auditRecords);
        }

        var validation = ExecuteCommandPermissionRuleLifecycle.ValidatePersistedRuleForCurrentWorkspace(replacement, runConfig);
        if (!validation.Valid)
        {
            issues.Add(ExecuteCommandPermissionRuleLifecycleIssue.From(replacement, validation.Reason!));
            auditRecords.Add(ExecuteCommandPermissionRuleLifecycleAuditRecord.Rejected(
                ExecuteCommandPermissionRuleLifecycleOperation.Replace,
                replacement,
                validation.Reason!));
            return new ExecuteCommandPermissionRuleLifecycleOperationResult(this, issues, auditRecords);
        }

        auditRecords.Add(ExecuteCommandPermissionRuleLifecycleAuditRecord.Activated(
            ExecuteCommandPermissionRuleLifecycleOperation.Replace,
            replacement));
        return new ExecuteCommandPermissionRuleLifecycleOperationResult(WithRuleUnchecked(replacement), issues, auditRecords);
    }

    public ExecuteCommandPermissionRuleLifecycleOperationResult RevalidateRules(AgentRunConfig runConfig)
    {
        var state = this with { Rules = [] };
        var issues = new List<ExecuteCommandPermissionRuleLifecycleIssue>();
        var auditRecords = new List<ExecuteCommandPermissionRuleLifecycleAuditRecord>();

        foreach (var rule in Rules)
        {
            var validation = ExecuteCommandPermissionRuleLifecycle.ValidatePersistedRuleForCurrentWorkspace(rule, runConfig);
            if (!validation.Valid)
            {
                issues.Add(ExecuteCommandPermissionRuleLifecycleIssue.From(rule, validation.Reason!));
                state = state.WithInactiveRuleUnchecked(rule, validation.Reason!);
                auditRecords.Add(ExecuteCommandPermissionRuleLifecycleAuditRecord.Inactivated(
                    ExecuteCommandPermissionRuleLifecycleOperation.Revalidate,
                    rule,
                    validation.Reason!));
                continue;
            }

            state = state.WithRuleUnchecked(rule);
        }

        return new ExecuteCommandPermissionRuleLifecycleOperationResult(state, issues, auditRecords);
    }

    public ExecuteCommandPermissionStateData WithoutRule(string ruleId)
        => this with
        {
            Rules = Rules.Where(rule => !string.Equals(rule.Id, ruleId, StringComparison.Ordinal)).ToArray(),
            InactiveRules = InactiveRules.Where(rule => !string.Equals(rule.Rule.Id, ruleId, StringComparison.Ordinal)).ToArray()
        };

    public ExecuteCommandPermissionStateData Clear()
        => this with { Rules = [], InactiveRules = [] };

    private ExecuteCommandPermissionStateData WithRuleUnchecked(ExecuteCommandPermissionRule rule)
    {
        var rules = Rules.Where(existing => !string.Equals(existing.Id, rule.Id, StringComparison.Ordinal)).Append(rule).ToArray();
        var inactive = InactiveRules.Where(existing => !string.Equals(existing.Rule.Id, rule.Id, StringComparison.Ordinal)).ToArray();
        return this with { Rules = rules, InactiveRules = inactive };
    }

    private ExecuteCommandPermissionStateData WithInactiveRuleUnchecked(
        ExecuteCommandPermissionRule rule,
        string reason)
    {
        var inactiveRule = new ExecuteCommandInactivePermissionRule
        {
            Rule = rule,
            Reason = reason,
            InactivatedAt = DateTimeOffset.UtcNow
        };
        var inactive = InactiveRules
            .Where(existing => !string.Equals(existing.Rule.Id, rule.Id, StringComparison.Ordinal))
            .Append(inactiveRule)
            .ToArray();
        var active = Rules.Where(existing => !string.Equals(existing.Id, rule.Id, StringComparison.Ordinal)).ToArray();
        return this with { Rules = active, InactiveRules = inactive };
    }
}

[MiddlewareState]
public sealed record ExecuteCommandBatchPermissionStateData
{
    public IReadOnlyDictionary<string, ExecuteCommandPermissionDecision> DecisionsByFingerprint { get; init; }
        = new Dictionary<string, ExecuteCommandPermissionDecision>(StringComparer.Ordinal);

    public ExecuteCommandBatchPermissionStateData WithDecision(string fingerprint, ExecuteCommandPermissionDecision decision)
    {
        var decisions = new Dictionary<string, ExecuteCommandPermissionDecision>(DecisionsByFingerprint, StringComparer.Ordinal)
        {
            [fingerprint] = decision
        };
        return this with { DecisionsByFingerprint = decisions };
    }
}

public sealed record ExecuteCommandPermissionDecision
{
    public required string Fingerprint { get; init; }
    public required bool Approved { get; init; }
    public string? Reason { get; init; }
    public string ReasonCode { get; init; } = "user_denied";
    public PermissionDeniedBehavior DeniedBehavior { get; init; } = PermissionDeniedBehavior.InterruptTurn;

    public static ExecuteCommandPermissionDecision AllowOnce(string fingerprint)
        => new() { Fingerprint = fingerprint, Approved = true };

    public static ExecuteCommandPermissionDecision Deny(
        string fingerprint,
        string reason,
        string reasonCode = "user_denied",
        PermissionDeniedBehavior deniedBehavior = PermissionDeniedBehavior.InterruptTurn)
        => new()
        {
            Fingerprint = fingerprint,
            Approved = false,
            Reason = reason,
            ReasonCode = reasonCode,
            DeniedBehavior = deniedBehavior
        };

    public static ExecuteCommandPermissionDecision InvalidArguments(string fingerprint, string reason)
        => Deny(fingerprint, reason, "invalid_arguments");
}

public sealed record ExecuteCommandPermissionRule
{
    public required string Id { get; init; }
    public required int RuleSchemaVersion { get; init; }
    public required int AnalyzerVersion { get; init; }
    public required int NormalizationVersion { get; init; }
    public required ExecuteCommandPermissionBehavior Behavior { get; init; }
    public required ExecuteCommandPermissionMatchKind MatchKind { get; init; }
    public required string Pattern { get; init; }
    public required ExecuteCommandShellScope Shell { get; init; }
    public required string RequestedSandboxFingerprint { get; init; }
    public required ExecuteCommandPermissionWorkspaceScope Workspace { get; init; }
    public ExecuteCommandPermissionRisk Risk { get; init; }
    public ExecuteCommandAnalysisTrustLevel MinimumTrustLevel { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? CreatedByPromptId { get; init; }
    public string? Description { get; init; }
}

public sealed record ExecuteCommandInactivePermissionRule
{
    public required ExecuteCommandPermissionRule Rule { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset InactivatedAt { get; init; }
}

public sealed record ExecuteCommandPermissionRuleValidationResult(
    bool Valid,
    string? Reason)
{
    public static ExecuteCommandPermissionRuleValidationResult Success { get; } = new(true, null);

    public static ExecuteCommandPermissionRuleValidationResult Failure(string reason)
        => new(false, reason);
}

public sealed record ExecuteCommandPermissionRuleLifecycleIssue(
    string RuleId,
    string Pattern,
    string Reason)
{
    public static ExecuteCommandPermissionRuleLifecycleIssue From(
        ExecuteCommandPermissionRule rule,
        string reason)
        => new(rule.Id, rule.Pattern, reason);
}

public sealed record ExecuteCommandPermissionRuleLifecycleOperationResult(
    ExecuteCommandPermissionStateData State,
    IReadOnlyList<ExecuteCommandPermissionRuleLifecycleIssue> Issues,
    IReadOnlyList<ExecuteCommandPermissionRuleLifecycleAuditRecord> AuditRecords)
{
    public bool Success => Issues.Count == 0;
}

public sealed record ExecuteCommandPermissionRuleLifecycleAuditRecord
{
    public required ExecuteCommandPermissionRuleLifecycleOperation Operation { get; init; }
    public required ExecuteCommandPermissionRuleLifecycleAction Action { get; init; }
    public required string RuleId { get; init; }
    public required string Pattern { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;

    public static ExecuteCommandPermissionRuleLifecycleAuditRecord Activated(
        ExecuteCommandPermissionRuleLifecycleOperation operation,
        ExecuteCommandPermissionRule rule)
        => new()
        {
            Operation = operation,
            Action = ExecuteCommandPermissionRuleLifecycleAction.Activated,
            RuleId = rule.Id,
            Pattern = rule.Pattern
        };

    public static ExecuteCommandPermissionRuleLifecycleAuditRecord Inactivated(
        ExecuteCommandPermissionRuleLifecycleOperation operation,
        ExecuteCommandPermissionRule rule,
        string reason)
        => new()
        {
            Operation = operation,
            Action = ExecuteCommandPermissionRuleLifecycleAction.Inactivated,
            RuleId = rule.Id,
            Pattern = rule.Pattern,
            Reason = reason
        };

    public static ExecuteCommandPermissionRuleLifecycleAuditRecord Rejected(
        ExecuteCommandPermissionRuleLifecycleOperation operation,
        ExecuteCommandPermissionRule rule,
        string reason)
        => new()
        {
            Operation = operation,
            Action = ExecuteCommandPermissionRuleLifecycleAction.Rejected,
            RuleId = rule.Id,
            Pattern = rule.Pattern,
            Reason = reason
        };
}

public enum ExecuteCommandPermissionRuleLifecycleOperation
{
    Import,
    Replace,
    Revalidate
}

public enum ExecuteCommandPermissionRuleLifecycleAction
{
    Activated,
    Inactivated,
    Rejected
}

internal static class ExecuteCommandPermissionRuleLifecycle
{
    public static ExecuteCommandPermissionRuleValidationResult ValidatePersistedRule(
        ExecuteCommandPermissionRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Id))
            return ExecuteCommandPermissionRuleValidationResult.Failure("missing_rule_id");
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return ExecuteCommandPermissionRuleValidationResult.Failure("missing_pattern");
        if (string.IsNullOrWhiteSpace(rule.RequestedSandboxFingerprint))
            return ExecuteCommandPermissionRuleValidationResult.Failure("missing_requested_sandbox_fingerprint");
        if (rule.RuleSchemaVersion != ExecuteCommandPermissionAnalyzerVersions.RuleSchema)
            return ExecuteCommandPermissionRuleValidationResult.Failure("rule_schema_version_mismatch");
        if (rule.AnalyzerVersion != ExecuteCommandPermissionAnalyzerVersions.Analyzer)
            return ExecuteCommandPermissionRuleValidationResult.Failure("analyzer_version_mismatch");
        if (rule.NormalizationVersion != ExecuteCommandPermissionAnalyzerVersions.Normalization)
            return ExecuteCommandPermissionRuleValidationResult.Failure("normalization_version_mismatch");

        if (rule.Behavior is ExecuteCommandPermissionBehavior.Ask or ExecuteCommandPermissionBehavior.Deny)
            return ExecuteCommandPermissionRuleValidationResult.Success;

        if (rule.MatchKind == ExecuteCommandPermissionMatchKind.Wildcard)
            return ExecuteCommandPermissionRuleValidationResult.Failure("allow_wildcard_not_persistable");

        var plan = AnalyzeRulePattern(rule);
        if (plan.Workspace.RootId != rule.Workspace.RootId)
            return ExecuteCommandPermissionRuleValidationResult.Failure("workspace_scope_mismatch");

        return rule.MatchKind switch
        {
            ExecuteCommandPermissionMatchKind.Exact => ValidateExactAllow(rule, plan),
            ExecuteCommandPermissionMatchKind.Prefix => ValidatePrefixAllow(rule, plan),
            _ => ExecuteCommandPermissionRuleValidationResult.Failure("unsupported_match_kind")
        };
    }

    public static ExecuteCommandPermissionRuleValidationResult ValidatePersistedRuleForCurrentWorkspace(
        ExecuteCommandPermissionRule rule,
        AgentRunConfig runConfig)
    {
        var workspaceValidation = ValidateWorkspaceScope(rule, runConfig);
        return workspaceValidation.Valid ? ValidatePersistedRule(rule) : workspaceValidation;
    }

    private static ExecuteCommandPermissionRuleValidationResult ValidateWorkspaceScope(
        ExecuteCommandPermissionRule rule,
        AgentRunConfig runConfig)
    {
        var workspace = AgentWorkspace.From(runConfig);
        var root = workspace.Roots.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, rule.Workspace.RootId, StringComparison.Ordinal));
        if (root is null)
            return ExecuteCommandPermissionRuleValidationResult.Failure("workspace_root_unavailable");

        var currentRootPath = Path.GetFullPath(root.Path);
        var ruleRootPath = Path.GetFullPath(rule.Workspace.RootPath);
        if (!string.Equals(currentRootPath, ruleRootPath, StringComparison.Ordinal))
            return ExecuteCommandPermissionRuleValidationResult.Failure("workspace_root_path_changed");

        return ExecuteCommandPermissionRuleValidationResult.Success;
    }

    private static ExecuteCommandPermissionPlan AnalyzeRulePattern(ExecuteCommandPermissionRule rule)
    {
        var runConfig = new AgentRunConfig
        {
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object>
                {
                    [AgentWorkspace.ContextKey] = new AgentWorkspace(
                        rule.Workspace.RootId,
                        rule.Workspace.RootPath,
                        [new AgentWorkspaceRoot(rule.Workspace.RootId, rule.Workspace.RootPath)]),
                }
            }
        };

        return ExecuteCommandPermissionMiddleware.ExecuteCommandPermissionAnalyzer.Analyze(
            new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["action"] = "run",
                    ["command"] = rule.Pattern
                }
            },
            runConfig,
            new ExecuteCommandOptions(),
            rule.Shell);
    }

    private static ExecuteCommandPermissionRuleValidationResult ValidateExactAllow(
        ExecuteCommandPermissionRule rule,
        ExecuteCommandPermissionPlan plan)
    {
        if (plan is not SimpleCommandPermissionPlan simple)
            return ExecuteCommandPermissionRuleValidationResult.Failure("exact_allow_not_reproducible");
        if (!string.Equals(simple.ExactAllowRule.Rule.Pattern, rule.Pattern, StringComparison.Ordinal))
            return ExecuteCommandPermissionRuleValidationResult.Failure("exact_allow_pattern_mismatch");
        return ExecuteCommandPermissionRuleValidationResult.Success;
    }

    private static ExecuteCommandPermissionRuleValidationResult ValidatePrefixAllow(
        ExecuteCommandPermissionRule rule,
        ExecuteCommandPermissionPlan plan)
    {
        if (plan is not SimpleCommandPermissionPlan simple ||
            simple.PrefixAllowRule is null ||
            !string.Equals(simple.PrefixAllowRule.Rule.Pattern, rule.Pattern, StringComparison.Ordinal))
        {
            return ExecuteCommandPermissionRuleValidationResult.Failure("prefix_allow_not_reproducible");
        }

        return ExecuteCommandPermissionRuleValidationResult.Success;
    }
}

public enum ExecuteCommandPermissionBehavior
{
    Allow,
    Deny,
    Ask
}

public enum ExecuteCommandPermissionMatchKind
{
    Exact,
    Prefix,
    Wildcard
}

public readonly record struct RawCommandText(string Value);
public readonly record struct NormalizedCommandText(string Value);
public readonly record struct SafeCommandPrefix(string Value);
public readonly record struct PermissionFingerprint(string Value);
public readonly record struct WorkspaceRootId(string Value);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "planKind")]
[JsonDerivedType(typeof(SimpleCommandPermissionPlan), "simple")]
[JsonDerivedType(typeof(SegmentedCommandPermissionPlan), "segmented")]
[JsonDerivedType(typeof(NonRunCommandPermissionPlan), "nonRun")]
[JsonDerivedType(typeof(ReviewOnlyCommandPermissionPlan), "reviewOnly")]
[JsonDerivedType(typeof(UntrustedCommandPermissionPlan), "untrusted")]
public abstract record ExecuteCommandPermissionPlan
{
    public required int AnalyzerVersion { get; init; }
    public required int NormalizationVersion { get; init; }
    public required PermissionFingerprint Fingerprint { get; init; }
    public required ExecuteCommandAction Action { get; init; }
    public required RawCommandText Command { get; init; }
    public required NormalizedCommandText NormalizedCommand { get; init; }
    public required ExecuteCommandShellScope Shell { get; init; }
    public required string WorkingDirectory { get; init; }
    public required ExecuteCommandPermissionWorkspaceScope Workspace { get; init; }
    public required AgentSandboxRuntime RequestedSandbox { get; init; }
    public required IReadOnlyList<ExecuteCommandFilesystemEffect> FilesystemEffects { get; init; }
    public required IReadOnlyList<ExecuteCommandNetworkEffect> NetworkEffects { get; init; }
    public required bool StartsInBackground { get; init; }
    public required ExecuteCommandPermissionRisk Risk { get; init; }
    public IReadOnlyList<ExecuteCommandUnsupportedShellFeature> UnsupportedShellFeatures { get; init; } = [];
    public abstract ExecuteCommandAnalysisTrustLevel TrustLevel { get; }
    public string? AnalysisWarning { get; init; }
    public string? ShellAnalyzerName { get; init; }
    public string? ShellUnsupportedFeatureReason { get; init; }
}

public sealed record SimpleCommandPermissionPlan : ExecuteCommandPermissionPlan
{
    public override ExecuteCommandAnalysisTrustLevel TrustLevel => ExecuteCommandAnalysisTrustLevel.Simple;
    public required ExecuteCommandSubcommandPlan CommandPlan { get; init; }
    public required ExactAllowRuleProposal ExactAllowRule { get; init; }
    public PrefixAllowRuleProposal? PrefixAllowRule { get; init; }
    public required IReadOnlyList<ExecuteCommandPermissionRuleProposal> SuggestedRules { get; init; }
}

public sealed record SegmentedCommandPermissionPlan : ExecuteCommandPermissionPlan
{
    public override ExecuteCommandAnalysisTrustLevel TrustLevel => ExecuteCommandAnalysisTrustLevel.Segmented;
    public required IReadOnlyList<ExecuteCommandSubcommandPlan> Segments { get; init; }
    public required SegmentRuleBundleProposal SegmentRuleBundle { get; init; }
}

public sealed record NonRunCommandPermissionPlan : ExecuteCommandPermissionPlan
{
    public override ExecuteCommandAnalysisTrustLevel TrustLevel => ExecuteCommandAnalysisTrustLevel.Simple;
    public required string PolicyReason { get; init; }
}

public sealed record ReviewOnlyCommandPermissionPlan : ExecuteCommandPermissionPlan
{
    public override ExecuteCommandAnalysisTrustLevel TrustLevel => ExecuteCommandAnalysisTrustLevel.ReviewOnly;
    public required IReadOnlyList<ExecuteCommandSubcommandPlan> VisibleSegments { get; init; }
    public required IReadOnlyList<ExecuteCommandPermissionRuleProposal> NonAllowRuleSuggestions { get; init; }
}

public sealed record UntrustedCommandPermissionPlan : ExecuteCommandPermissionPlan
{
    public override ExecuteCommandAnalysisTrustLevel TrustLevel => ExecuteCommandAnalysisTrustLevel.Untrusted;
    public required string FailureReason { get; init; }
    public bool InvalidRequest { get; init; }
}

internal sealed record ExecuteCommandPlanBase
{
    public required int AnalyzerVersion { get; init; }
    public required int NormalizationVersion { get; init; }
    public required PermissionFingerprint Fingerprint { get; init; }
    public required ExecuteCommandAction Action { get; init; }
    public required RawCommandText Command { get; init; }
    public required NormalizedCommandText NormalizedCommand { get; init; }
    public required ExecuteCommandShellScope Shell { get; init; }
    public required string WorkingDirectory { get; init; }
    public required ExecuteCommandPermissionWorkspaceScope Workspace { get; init; }
    public required AgentSandboxRuntime RequestedSandbox { get; init; }
    public required IReadOnlyList<ExecuteCommandFilesystemEffect> FilesystemEffects { get; init; }
    public required IReadOnlyList<ExecuteCommandNetworkEffect> NetworkEffects { get; init; }
    public required bool StartsInBackground { get; init; }
    public required ExecuteCommandPermissionRisk Risk { get; init; }
    public IReadOnlyList<ExecuteCommandUnsupportedShellFeature> UnsupportedShellFeatures { get; init; } = [];
    public string? ShellAnalyzerName { get; init; }
    public string? ShellUnsupportedFeatureReason { get; init; }

    public SimpleCommandPermissionPlan ToSimple(
        ExecuteCommandSubcommandPlan command,
        ExactAllowRuleProposal exact,
        PrefixAllowRuleProposal? prefix)
        => new()
        {
            AnalyzerVersion = AnalyzerVersion,
            NormalizationVersion = NormalizationVersion,
            Fingerprint = Fingerprint,
            Action = Action,
            Command = Command,
            NormalizedCommand = NormalizedCommand,
            Shell = Shell,
            WorkingDirectory = WorkingDirectory,
            Workspace = Workspace,
            RequestedSandbox = RequestedSandbox,
            FilesystemEffects = FilesystemEffects,
            NetworkEffects = NetworkEffects,
            StartsInBackground = StartsInBackground,
            Risk = Risk,
            UnsupportedShellFeatures = UnsupportedShellFeatures,
            ShellAnalyzerName = ShellAnalyzerName,
            ShellUnsupportedFeatureReason = ShellUnsupportedFeatureReason,
            CommandPlan = command,
            ExactAllowRule = exact,
            PrefixAllowRule = prefix,
            SuggestedRules = prefix is null ? [exact] : [exact, prefix]
        };

    public SegmentedCommandPermissionPlan ToSegmented(
        IReadOnlyList<ExecuteCommandSubcommandPlan> segments,
        SegmentRuleBundleProposal proposal)
        => new()
        {
            AnalyzerVersion = AnalyzerVersion,
            NormalizationVersion = NormalizationVersion,
            Fingerprint = Fingerprint,
            Action = Action,
            Command = Command,
            NormalizedCommand = NormalizedCommand,
            Shell = Shell,
            WorkingDirectory = WorkingDirectory,
            Workspace = Workspace,
            RequestedSandbox = RequestedSandbox,
            FilesystemEffects = FilesystemEffects,
            NetworkEffects = NetworkEffects,
            StartsInBackground = StartsInBackground,
            Risk = Risk,
            UnsupportedShellFeatures = UnsupportedShellFeatures,
            ShellAnalyzerName = ShellAnalyzerName,
            ShellUnsupportedFeatureReason = ShellUnsupportedFeatureReason,
            Segments = segments,
            SegmentRuleBundle = proposal
        };

    public NonRunCommandPermissionPlan ToNonRun(string policyReason)
        => new()
        {
            AnalyzerVersion = AnalyzerVersion,
            NormalizationVersion = NormalizationVersion,
            Fingerprint = Fingerprint,
            Action = Action,
            Command = Command,
            NormalizedCommand = NormalizedCommand,
            Shell = Shell,
            WorkingDirectory = WorkingDirectory,
            Workspace = Workspace,
            RequestedSandbox = RequestedSandbox,
            FilesystemEffects = FilesystemEffects,
            NetworkEffects = NetworkEffects,
            StartsInBackground = StartsInBackground,
            Risk = Risk,
            UnsupportedShellFeatures = UnsupportedShellFeatures,
            ShellAnalyzerName = ShellAnalyzerName,
            ShellUnsupportedFeatureReason = ShellUnsupportedFeatureReason,
            PolicyReason = policyReason
        };

    public ReviewOnlyCommandPermissionPlan ToReviewOnly(IReadOnlyList<ExecuteCommandSubcommandPlan> segments)
        => new()
        {
            AnalyzerVersion = AnalyzerVersion,
            NormalizationVersion = NormalizationVersion,
            Fingerprint = Fingerprint,
            Action = Action,
            Command = Command,
            NormalizedCommand = NormalizedCommand,
            Shell = Shell,
            WorkingDirectory = WorkingDirectory,
            Workspace = Workspace,
            RequestedSandbox = RequestedSandbox,
            FilesystemEffects = FilesystemEffects,
            NetworkEffects = NetworkEffects,
            StartsInBackground = StartsInBackground,
            Risk = Risk,
            UnsupportedShellFeatures = UnsupportedShellFeatures,
            ShellAnalyzerName = ShellAnalyzerName,
            ShellUnsupportedFeatureReason = ShellUnsupportedFeatureReason,
            VisibleSegments = segments,
            NonAllowRuleSuggestions = []
        };

    public UntrustedCommandPermissionPlan ToUntrusted(string reason)
        => new()
        {
            AnalyzerVersion = AnalyzerVersion,
            NormalizationVersion = NormalizationVersion,
            Fingerprint = Fingerprint,
            Action = Action,
            Command = Command,
            NormalizedCommand = NormalizedCommand,
            Shell = Shell,
            WorkingDirectory = WorkingDirectory,
            Workspace = Workspace,
            RequestedSandbox = RequestedSandbox,
            FilesystemEffects = FilesystemEffects,
            NetworkEffects = NetworkEffects,
            StartsInBackground = StartsInBackground,
            Risk = Risk,
            UnsupportedShellFeatures = UnsupportedShellFeatures,
            ShellAnalyzerName = ShellAnalyzerName,
            ShellUnsupportedFeatureReason = ShellUnsupportedFeatureReason,
            FailureReason = reason
        };
}

public sealed record ExecuteCommandSubcommandPlan
{
    public required string Text { get; init; }
    public IReadOnlyList<string> Argv { get; init; } = [];
    public IReadOnlyList<string> DefensiveArgv { get; init; } = [];
    public IReadOnlyDictionary<string, string?> EnvironmentAssignments { get; init; } = new Dictionary<string, string?>(StringComparer.Ordinal);
    public IReadOnlyList<ExecuteCommandRedirectionPlan> Redirections { get; init; } = [];
    public IReadOnlyList<string> NormalizedWrappers { get; init; } = [];
    public required string BaseCommand { get; init; }
    public string? SafePrefix { get; init; }
    public required ExecuteCommandPermissionRisk Risk { get; init; }
    public required ExecuteCommandAnalysisTrustLevel TrustLevel { get; init; }
    public ExecuteCommandPolicyReadiness Readiness { get; init; } = ExecuteCommandPolicyReadiness.OneTimeOnly;
}

internal sealed record ExecuteCommandShellParseResult
{
    public required RawCommandText Command { get; init; }
    public required ExecuteCommandShellFamily Family { get; init; }
    public required IReadOnlyList<ExecuteCommandShellSegmentParse> Segments { get; init; }
    public IReadOnlyList<ExecuteCommandShellOperatorParse> Operators { get; init; } = [];
    public IReadOnlyList<ExecuteCommandShellExpansionParse> Expansions { get; init; } = [];
    public IReadOnlyList<ExecuteCommandShellHeredocParse> Heredocs { get; init; } = [];
    public IReadOnlyList<ExecuteCommandShellSubshellParse> Subshells { get; init; } = [];
    public ExecuteCommandPermissionRisk Risk { get; init; }
    public IReadOnlyList<ExecuteCommandUnsupportedShellFeature> UnsupportedFeatures { get; init; } = [];
}

internal sealed record ExecuteCommandShellSegmentParse
{
    public required string Text { get; init; }
    public required ExecuteCommandShellSourceSpan Span { get; init; }
    public required IReadOnlyList<ExecuteCommandShellToken> Tokens { get; init; }
    public IReadOnlyList<ExecuteCommandRedirectionPlan> Redirections { get; init; } = [];
    public IReadOnlyList<ExecuteCommandShellExpansionParse> Expansions { get; init; } = [];
    public IReadOnlyList<ExecuteCommandShellHeredocParse> Heredocs { get; init; } = [];
    public IReadOnlyList<ExecuteCommandShellSubshellParse> Subshells { get; init; } = [];
    public ExecuteCommandPermissionRisk Risk { get; init; }
    public IReadOnlyList<ExecuteCommandUnsupportedShellFeature> UnsupportedFeatures { get; init; } = [];
}

internal sealed record ExecuteCommandShellToken
{
    public required string Text { get; init; }
    public required ExecuteCommandShellTokenKind Kind { get; init; }
    public required ExecuteCommandShellSourceSpan Span { get; init; }
}

internal enum ExecuteCommandShellTokenKind
{
    Word,
    SingleQuoted,
    DoubleQuoted
}

internal sealed record ExecuteCommandShellOperatorParse
{
    public required ExecuteCommandShellOperatorKind Kind { get; init; }
    public required string Text { get; init; }
    public required ExecuteCommandShellSourceSpan Span { get; init; }
}

internal enum ExecuteCommandShellOperatorKind
{
    Pipe,
    And,
    Or,
    Separator,
    Newline
}

internal readonly record struct ExecuteCommandShellSourceSpan(int Start, int Length);

internal sealed record ExecuteCommandShellExpansionParse
{
    public required ExecuteCommandShellExpansionKind Kind { get; init; }
    public required string Text { get; init; }
    public required ExecuteCommandShellSourceSpan Span { get; init; }
}

internal enum ExecuteCommandShellExpansionKind
{
    CommandSubstitution,
    BacktickCommandSubstitution,
    BareVariable
}

internal sealed record ExecuteCommandShellHeredocParse
{
    public required string Operator { get; init; }
    public required string Delimiter { get; init; }
    public required bool DelimiterQuoted { get; init; }
    public string? Body { get; init; }
    public required ExecuteCommandShellSourceSpan Span { get; init; }
}

internal sealed record ExecuteCommandShellSubshellParse
{
    public required string Text { get; init; }
    public required ExecuteCommandShellSourceSpan Span { get; init; }
}

public sealed record ExecuteCommandShellScope
{
    public required string Executable { get; init; }
    public required ExecuteCommandShellFamily Family { get; init; }
    public string? Version { get; init; }
}

public enum ExecuteCommandShellFamily
{
    Unknown,
    Bash,
    Zsh,
    Sh,
    PowerShell,
    Cmd
}

public sealed record ExecuteCommandRedirectionPlan
{
    public required ExecuteCommandRedirectionKind Kind { get; init; }
    public required string Target { get; init; }
    public required ExecuteCommandFilesystemOperation Operation { get; init; }
    public required bool TargetStaticallyResolved { get; init; }
}

public enum ExecuteCommandRedirectionKind
{
    Input,
    Output,
    Append,
    ErrorOutput,
    ErrorAppend,
    OutputAndError
}

public sealed record ExecuteCommandFilesystemEffect
{
    public required ExecuteCommandFilesystemOperation Operation { get; init; }
    public required string Path { get; init; }
    public required bool WithinWorkspace { get; init; }
    public required bool CoveredBySandbox { get; init; }
}

public enum ExecuteCommandFilesystemOperation
{
    Read,
    Create,
    Write,
    Delete
}

public sealed record ExecuteCommandNetworkEffect
{
    public required ExecuteCommandNetworkOperation Operation { get; init; }
    public string? Host { get; init; }
    public string? Port { get; init; }
    public required bool CoveredBySandbox { get; init; }
}

public enum ExecuteCommandNetworkOperation
{
    LikelyEgress
}

public enum ExecuteCommandAnalysisTrustLevel
{
    Simple,
    Segmented,
    ReviewOnly,
    Untrusted
}

[Flags]
public enum ExecuteCommandPermissionRisk
{
    None = 0,
    UnknownOrUnparseable = 1 << 0,
    ParserDifferentialRisk = 1 << 1,
    CompoundCommand = 1 << 2,
    ShellInvocation = 1 << 3,
    PrivilegeEscalation = 1 << 4,
    FilesystemMutation = 1 << 5,
    PathSensitiveWrite = 1 << 6,
    Destructive = 1 << 7,
    NetworkLikely = 1 << 8,
    OutputRedirection = 1 << 9,
    UnsafeRedirectionTarget = 1 << 10,
    CommandSubstitution = 1 << 11,
    BareVariableExpansion = 1 << 12,
    Heredoc = 1 << 13,
    UnknownWrapper = 1 << 14,
    DangerousShellBuiltin = 1 << 15,
    CompoundWithDirectoryChange = 1 << 16,
    BackgroundProcess = 1 << 17,
    Unsandboxed = 1 << 18,
    AdditionalSandboxPermissions = 1 << 19,
    OutsideWorkspaceReference = 1 << 20,
    Subshell = 1 << 21
}

public enum ExecuteCommandUnsupportedShellFeature
{
    ParserDifferential,
    ControlCharacter,
    CarriageReturn,
    UnicodeWhitespace,
    EscapedOperator,
    MidWordComment,
    QuotedNewlineComment,
    BraceExpansion,
    CommandSubstitution,
    BareVariableExpansion,
    Heredoc,
    OutputRedirection,
    UnsafeRedirectionTarget,
    ShellInvocation,
    EncodedCommand,
    ScriptBlockInvocation,
    Pipeline,
    PowerShellAlias,
    PowerShellSubexpression,
    PowerShellInvocationOperator,
    PowerShellFileWritingCommand,
    CmdPercentExpansion,
    CmdDelayedExpansion,
    CmdBatchDispatch,
    CmdFor,
    CmdCall,
    CmdSet,
    CmdCommandSwitch,
    ExcessiveSegments,
    DirectoryChangeCompound,
    UnknownWrapper,
    Subshell
}

public enum ExecuteCommandPolicyReadiness
{
    OneTimeOnly,
    ExactAllowOnly,
    PrefixAllowAllowed,
    SegmentBundleAllowed
}

public sealed record ExecuteCommandPermissionWorkspaceScope
{
    public required string RootId { get; init; }
    public required string RootPath { get; init; }
    public string? RelativeWorkingDirectory { get; init; }

    public static ExecuteCommandPermissionWorkspaceScope From(AgentRunConfig runConfig, string? workingDirectory)
    {
        var workspace = AgentWorkspace.From(runConfig);
        var root = workspace.Roots.FirstOrDefault();
        var rootPath = root?.Path ?? Directory.GetCurrentDirectory();
        var relative = workingDirectory is null ? null : Path.GetRelativePath(rootPath, workingDirectory);
        return new ExecuteCommandPermissionWorkspaceScope
        {
            RootId = root?.Id ?? "default",
            RootPath = rootPath,
            RelativeWorkingDirectory = relative
        };
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "proposalKind")]
[JsonDerivedType(typeof(ExactAllowRuleProposal), "exactAllow")]
[JsonDerivedType(typeof(PrefixAllowRuleProposal), "prefixAllow")]
[JsonDerivedType(typeof(SegmentRuleBundleProposal), "segmentBundle")]
[JsonDerivedType(typeof(AskRuleProposal), "ask")]
[JsonDerivedType(typeof(DenyRuleProposal), "deny")]
public abstract record ExecuteCommandPermissionRuleProposal
{
    public required ExecuteCommandPermissionRule Rule { get; init; }
    public required string UserLabel { get; init; }
}

public sealed record ExactAllowRuleProposal : ExecuteCommandPermissionRuleProposal;
public sealed record PrefixAllowRuleProposal : ExecuteCommandPermissionRuleProposal
{
    public required SafeCommandPrefix Prefix { get; init; }
}
public sealed record SegmentRuleBundleProposal : ExecuteCommandPermissionRuleProposal
{
    public required IReadOnlyList<ExecuteCommandPermissionRule> SegmentRules { get; init; }
}
public sealed record AskRuleProposal : ExecuteCommandPermissionRuleProposal;
public sealed record DenyRuleProposal : ExecuteCommandPermissionRuleProposal;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "choiceKind")]
[JsonDerivedType(typeof(AllowOnceChoice), "allowOnce")]
[JsonDerivedType(typeof(PersistRuleChoice), "persistRule")]
[JsonDerivedType(typeof(DenyChoice), "deny")]
[JsonDerivedType(typeof(FeedbackChoice), "feedback")]
public abstract record ExecuteCommandPermissionChoice
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
}

public sealed record AllowOnceChoice : ExecuteCommandPermissionChoice;

public sealed record PersistRuleChoice : ExecuteCommandPermissionChoice
{
    public required ExecuteCommandPermissionRuleProposal Proposal { get; init; }
}

public sealed record DenyChoice : ExecuteCommandPermissionChoice;
public sealed record FeedbackChoice : ExecuteCommandPermissionChoice;

public sealed record ExecuteCommandPermissionRequestEvent(
    string PermissionId,
    string SourceName,
    string CallId,
    ExecuteCommandPermissionPlan Plan,
    IReadOnlyList<ExecuteCommandPermissionRule> MatchingRules,
    ExecuteCommandPermissionRuleDiagnostics RuleDiagnostics,
    IReadOnlyList<ExecuteCommandPermissionChoice> AvailableChoices) : AgentEvent, IAgentRequestEvent<ExecuteCommandPermissionResponseEvent>
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Control;
    public string RequestId => PermissionId;
}

public sealed record ExecuteCommandPermissionResponseEvent(
    string PermissionId,
    string SourceName,
    string ChoiceId,
    string? FeedbackText = null) : AgentEvent, IAgentResponseEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Control;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;
    public string RequestId => PermissionId;
}

public sealed record ExecuteCommandPermissionRulePersistedEvent(
    string PermissionId,
    string SourceName,
    string CallId,
    string RuleId,
    string Pattern,
    ExecuteCommandPermissionBehavior Behavior,
    ExecuteCommandPermissionMatchKind MatchKind,
    ExecuteCommandPermissionAuditDetails Details) : AgentEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;
}

public sealed record ExecuteCommandPermissionAuditDetails
{
    public required int AnalyzerVersion { get; init; }
    public required int NormalizationVersion { get; init; }
    public required int RuleSchemaVersion { get; init; }
    public required ExecuteCommandShellScope Shell { get; init; }
    public required ExecuteCommandPermissionWorkspaceScope Workspace { get; init; }
    public string? MatchedRuleId { get; init; }
    public int? MatchedRuleSchemaVersion { get; init; }
    public required string Decision { get; init; }
    public required ExecuteCommandAnalysisTrustLevel TrustLevel { get; init; }
    public required ExecuteCommandPermissionRisk Risk { get; init; }
    public required IReadOnlyList<ExecuteCommandUnsupportedShellFeature> UnsupportedShellFeatures { get; init; }
    public required IReadOnlyList<string> PersistedRuleIds { get; init; }
}

internal static class ExecuteCommandPermissionAnalyzerVersions
{
    public const int RuleSchema = 2;
    public const int Analyzer = 2;
    public const int Normalization = 2;
    public const int SemanticPolicy = 2;
}

internal static class ExecuteCommandPermissionParityChecklist
{
    public static IReadOnlyList<ExecuteCommandPermissionParityGate> Entries { get; } =
    [
        new("safe-env-allow", "Environment normalization", ExecuteCommandPermissionParityStatus.CorpusCovered, "Safe env allowlist; unsafe env rejection", ["safe-env-allow", "unsafe-env-prefix"]),
        new("broad-env-deny-ask", "Environment normalization", ExecuteCommandPermissionParityStatus.CorpusCovered, "FOO=bar denied command still denied", ["deny-env-bypass"]),
        new("wrapper-normalization", "Wrapper normalization", ExecuteCommandPermissionParityStatus.CorpusCovered, "timeout/time/nice/nohup/stdbuf accepted and rejected forms", ["safe-wrapper-timeout", "unsafe-wrapper-env"]),
        new("banned-prefixes", "Wrapper and builtin policy", ExecuteCommandPermissionParityStatus.CorpusCovered, "shells, wrappers, privilege commands never produce prefix allow", ["shell-c", "sudo-prefix", "interpreter-python"]),
        new("prefix-compound-rejection", "Structural segmentation", ExecuteCommandPermissionParityStatus.CorpusCovered, "prefix rules do not match raw compound commands", ["compound-git-curl"]),
        new("too-complex-parser-handling", "Parser differential", ExecuteCommandPermissionParityStatus.CorpusCovered, "complex parser differentials return review or untrusted", ["escaped-operator", "control-carriage-return", "unicode-whitespace", "midword-comment", "brace-expansion"]),
        new("parser-differential-corpus", "Parser differential", ExecuteCommandPermissionParityStatus.CorpusCovered, "quote, newline, control character, escaped operator corpus", ["escaped-operator", "control-carriage-return", "unicode-whitespace", "midword-comment", "quoted-newline-comment", "brace-expansion"]),
        new("original-redirection-validation", "Redirection", ExecuteCommandPermissionParityStatus.CorpusCovered, "redirections validated from original command", ["safe-output-redirection", "unsafe-redirection-variable", "unsafe-redirection-glob"]),
        new("path-sensitive-extraction", "Path-sensitive command", ExecuteCommandPermissionParityStatus.CorpusCovered, "command-specific argv examples", ["rm-delete-effect", "copy-effects", "grep-path-effect"]),
        new("sed-dangerous-forms", "Sed special gate", ExecuteCommandPermissionParityStatus.CorpusCovered, "in-place, write, execute, script-file cases", ["sed-in-place", "sed-script-file"]),
        new("jq-dangerous-forms", "Jq special gate", ExecuteCommandPermissionParityStatus.CorpusCovered, "file reads, module loading, filesystem-affecting forms", ["jq-rawfile", "jq-library-path"]),
        new("directory-change-risk", "Directory change", ExecuteCommandPermissionParityStatus.CorpusCovered, "cd/pushd/popd with git, writes, redirects", ["cd-plus-write"]),
        new("sandbox-deny-ask-precedence", "Sandbox auto-allow", ExecuteCommandPermissionParityStatus.CorpusCovered, "explicit deny and ask win before allow", ["deny-env-bypass"]),
        new("segment-fanout-cap", "Structural segmentation", ExecuteCommandPermissionParityStatus.CorpusCovered, "excessive segments fail closed", ["segment-fanout-cap"]),
        new("powershell-cmd-family-adapters", "Shell family adapters", ExecuteCommandPermissionParityStatus.CorpusCovered, "PowerShell and cmd risky constructs avoid POSIX heuristics", ["powershell-encoded", "powershell-pipeline", "cmd-redirection", "cmd-for-loop"]),
        new("suggestion-arity", "Suggestion arity policy", ExecuteCommandPermissionParityStatus.CorpusCovered, "arity traps reject broad interpreter, wrapper, and multi-meaning prefixes", ["arity-gh-pr-check", "arity-git-remote-set-url", "interpreter-python"]),
        new("external-workspace-overlays", "Sandbox overlay policy", ExecuteCommandPermissionParityStatus.CorpusCovered, "static external paths produce narrow overlays; unresolved/sensitive paths do not", ["external-write-overlay", "unsafe-redirection-variable"])
    ];
}

public sealed record ExecuteCommandPermissionParityGate(
    string Id,
    string Gate,
    ExecuteCommandPermissionParityStatus Status,
    string RequiredTests,
    IReadOnlyList<string> RequiredCorpusIds);

public enum ExecuteCommandPermissionParityStatus
{
    Required,
    Implemented,
    CorpusCovered,
    PersistenceEnabled
}

internal static class ExecuteCommandPermissionChoiceBuilder
{
    public static IReadOnlyList<ExecuteCommandPermissionChoice> Build(
        ExecuteCommandPermissionPlan plan,
        IReadOnlyList<ExecuteCommandPermissionRule> matchingRules)
    {
        var choices = new List<ExecuteCommandPermissionChoice>
        {
            new AllowOnceChoice
            {
                Id = "allow_once",
                Label = "Allow once"
            }
        };

        if (plan is SimpleCommandPermissionPlan simple)
        {
            choices.Add(new PersistRuleChoice
            {
                Id = "allow_exact",
                Label = "Always allow this exact command",
                Proposal = simple.ExactAllowRule
            });
            if (simple.PrefixAllowRule is not null)
            {
                choices.Add(new PersistRuleChoice
                {
                    Id = "allow_similar",
                    Label = "Always allow similar commands",
                    Proposal = simple.PrefixAllowRule
                });
            }
        }
        else if (plan is SegmentedCommandPermissionPlan segmented)
        {
            choices.Add(new PersistRuleChoice
            {
                Id = "allow_similar",
                Label = "Always allow similar commands",
                Proposal = segmented.SegmentRuleBundle
            });
        }

        choices.Add(new DenyChoice
        {
            Id = "deny",
            Label = "Deny"
        });
        choices.Add(new FeedbackChoice
        {
            Id = "feedback",
            Label = "Tell agent what to do instead"
        });
        return choices;
    }
}

internal static class ExecuteCommandPermissionRuleMatcher
{
    public static ExecuteCommandPermissionRuleMatch Match(
        ExecuteCommandPermissionPlan plan,
        IReadOnlyList<ExecuteCommandPermissionRule> rules)
    {
        var evaluated = rules
            .Select(rule => EvaluateRule(plan, rule))
            .ToArray();

        var scoped = evaluated
            .Where(result => result.InactiveReason is null)
            .Select(result => result.Rule)
            .ToArray();

        var active = evaluated
            .Where(result => result.InactiveReason is null && result.Matches)
            .Select(result => result.Rule)
            .ToArray();

        var decision = active.FirstOrDefault(rule => rule.Behavior == ExecuteCommandPermissionBehavior.Deny)
            ?? active.FirstOrDefault(rule => rule.Behavior == ExecuteCommandPermissionBehavior.Ask);

        if (decision is null)
        {
            decision = plan switch
            {
                SimpleCommandPermissionPlan => active.FirstOrDefault(rule => rule.Behavior == ExecuteCommandPermissionBehavior.Allow),
                SegmentedCommandPermissionPlan segmented when AllSegmentsAllowed(segmented, scoped) =>
                    active.FirstOrDefault(rule => rule.Behavior == ExecuteCommandPermissionBehavior.Allow),
                _ => null
            };
        }

        var shadowed = decision is null
            ? []
            : active.Where(rule => !string.Equals(rule.Id, decision.Id, StringComparison.Ordinal) && IsShadowedBy(decision, rule)).ToArray();
        var inactive = evaluated
            .Where(result => result.InactiveReason is not null)
            .Select(result => new ExecuteCommandInactiveRule(result.Rule.Id, result.Rule.Pattern, result.InactiveReason!))
            .ToArray();
        var diagnostics = new ExecuteCommandPermissionRuleDiagnostics(
            decision?.Id,
            decision?.Behavior,
            active,
            shadowed,
            inactive);

        return new ExecuteCommandPermissionRuleMatch(active, decision, diagnostics);
    }

    private static ExecuteCommandRuleEvaluation EvaluateRule(
        ExecuteCommandPermissionPlan plan,
        ExecuteCommandPermissionRule rule)
    {
        var inactiveReason = GetInactiveReason(plan, rule);
        if (inactiveReason is not null)
            return new ExecuteCommandRuleEvaluation(rule, false, inactiveReason);

        return new ExecuteCommandRuleEvaluation(rule, MatchesAny(plan, rule), null);
    }

    private static string? GetInactiveReason(
        ExecuteCommandPermissionPlan plan,
        ExecuteCommandPermissionRule rule)
    {
        if (rule.RuleSchemaVersion != ExecuteCommandPermissionAnalyzerVersions.RuleSchema)
            return "rule_schema_version_mismatch";
        if (rule.AnalyzerVersion != ExecuteCommandPermissionAnalyzerVersions.Analyzer)
            return "analyzer_version_mismatch";
        if (rule.NormalizationVersion != ExecuteCommandPermissionAnalyzerVersions.Normalization)
            return "normalization_version_mismatch";
        if (rule.Shell.Family != plan.Shell.Family)
            return "shell_family_mismatch";
        if (!string.Equals(rule.Workspace.RootId, plan.Workspace.RootId, StringComparison.Ordinal))
            return "workspace_root_mismatch";
        if (!string.Equals(rule.RequestedSandboxFingerprint, plan.RequestedSandbox.Canonicalize(plan.WorkingDirectory), StringComparison.Ordinal))
            return "sandbox_scope_mismatch";
        return null;
    }

    private static bool IsShadowedBy(
        ExecuteCommandPermissionRule decision,
        ExecuteCommandPermissionRule candidate)
        => decision.Behavior switch
        {
            ExecuteCommandPermissionBehavior.Deny => candidate.Behavior is ExecuteCommandPermissionBehavior.Ask or ExecuteCommandPermissionBehavior.Allow,
            ExecuteCommandPermissionBehavior.Ask => candidate.Behavior == ExecuteCommandPermissionBehavior.Allow,
            ExecuteCommandPermissionBehavior.Allow => decision.MatchKind == ExecuteCommandPermissionMatchKind.Exact &&
                candidate.Behavior == ExecuteCommandPermissionBehavior.Allow &&
                candidate.MatchKind == ExecuteCommandPermissionMatchKind.Prefix,
            _ => false
        };

    private static bool AllSegmentsAllowed(
        SegmentedCommandPermissionPlan plan,
        IReadOnlyList<ExecuteCommandPermissionRule> scopedRules)
        => plan.Segments.All(segment => scopedRules.Any(rule =>
            rule.Behavior == ExecuteCommandPermissionBehavior.Allow &&
            MatchesSegment(segment, rule)));

    private static bool MatchesAny(ExecuteCommandPermissionPlan plan, ExecuteCommandPermissionRule rule)
    {
        if (MatchesExactCommand(plan, rule))
            return true;

        return GetSegments(plan).Any(segment => MatchesSegment(segment, rule));
    }

    private static IReadOnlyList<ExecuteCommandSubcommandPlan> GetSegments(ExecuteCommandPermissionPlan plan)
        => plan switch
        {
            SimpleCommandPermissionPlan simple => [simple.CommandPlan],
            SegmentedCommandPermissionPlan segmented => segmented.Segments,
            ReviewOnlyCommandPermissionPlan reviewOnly => reviewOnly.VisibleSegments,
            _ => []
        };

    private static bool MatchesExactCommand(
        ExecuteCommandPermissionPlan plan,
        ExecuteCommandPermissionRule rule)
        => rule.MatchKind == ExecuteCommandPermissionMatchKind.Exact &&
           string.Equals(plan.NormalizedCommand.Value, rule.Pattern, StringComparison.Ordinal);

    private static bool MatchesSegment(
        ExecuteCommandSubcommandPlan segment,
        ExecuteCommandPermissionRule rule)
    {
        var normalized = GetMatchText(segment, rule.Behavior);
        return MatchesText(normalized, rule);
    }

    private static string GetMatchText(
        ExecuteCommandSubcommandPlan segment,
        ExecuteCommandPermissionBehavior behavior)
        => string.Join(' ', behavior == ExecuteCommandPermissionBehavior.Allow
            ? segment.Argv
            : segment.DefensiveArgv);

    private static bool MatchesText(string text, ExecuteCommandPermissionRule rule)
        => rule.MatchKind switch
        {
            ExecuteCommandPermissionMatchKind.Exact => string.Equals(text, rule.Pattern, StringComparison.Ordinal),
            ExecuteCommandPermissionMatchKind.Prefix =>
                string.Equals(text, rule.Pattern, StringComparison.Ordinal) ||
                text.StartsWith(rule.Pattern + " ", StringComparison.Ordinal),
            ExecuteCommandPermissionMatchKind.Wildcard => false,
            _ => false
        };
}

internal sealed record ExecuteCommandPermissionRuleMatch(
    IReadOnlyList<ExecuteCommandPermissionRule> MatchingRules,
    ExecuteCommandPermissionRule? Decision,
    ExecuteCommandPermissionRuleDiagnostics Diagnostics);

internal sealed record ExecuteCommandRuleEvaluation(
    ExecuteCommandPermissionRule Rule,
    bool Matches,
    string? InactiveReason);

public sealed record ExecuteCommandPermissionRuleDiagnostics(
    string? DecisionRuleId,
    ExecuteCommandPermissionBehavior? DecisionBehavior,
    IReadOnlyList<ExecuteCommandPermissionRule> MatchingRules,
    IReadOnlyList<ExecuteCommandPermissionRule> ShadowedRules,
    IReadOnlyList<ExecuteCommandInactiveRule> InactiveRules);

public sealed record ExecuteCommandInactiveRule(
    string RuleId,
    string Pattern,
    string Reason);

internal sealed record ExecuteCommandShellAnalysis(
    IReadOnlyList<ExecuteCommandSubcommandPlan> Segments,
    ExecuteCommandPermissionRisk Risk,
    ExecuteCommandAnalysisTrustLevel TrustLevel)
{
    public ExecuteCommandShellFamily Family { get; init; } = ExecuteCommandShellFamily.Unknown;
    public string AnalyzerName { get; init; } = string.Empty;
    public IReadOnlyList<ExecuteCommandUnsupportedShellFeature> UnsupportedFeatures { get; init; } = [];
    public string? UnsupportedFeatureReason { get; init; }
}

internal interface IExecuteCommandShellFamilyAnalyzer
{
    ExecuteCommandShellFamily Family { get; }

    ExecuteCommandShellAnalysis Analyze(
        RawCommandText command,
        ExecuteCommandShellScope shell,
        ExecuteCommandSemanticPolicy policy);
}

internal sealed record ExecuteCommandSuggestionArity
{
    public required int MinimumTokens { get; init; }
    public required int MaximumTokens { get; init; }
    public bool AllowExactWhenPrefixRejected { get; init; }
}

internal delegate ExecuteCommandPermissionRisk ExecuteCommandCommandRiskClassifier(IReadOnlyList<string> argv);
internal delegate IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> ExecuteCommandFilesystemEffectExtractor(IReadOnlyList<string> argv);
internal delegate bool ExecuteCommandNetworkEffectClassifier(IReadOnlyList<string> argv);

internal sealed record ExecuteCommandCommandFamilyPolicy
{
    public required int SemanticPolicyVersion { get; init; }
    public required string Pattern { get; init; }
    public required ExecuteCommandPolicyReadiness Readiness { get; init; }
    public required IReadOnlyList<int> SuggestionArities { get; init; }
    public IReadOnlyList<string> RequiredParityGateIds { get; init; } = [];
    public ExecuteCommandPermissionParityStatus MinimumParityStatus { get; init; } =
        ExecuteCommandPermissionParityStatus.CorpusCovered;
    public ExecuteCommandPermissionRisk BaseRisk { get; init; }
    public ExecuteCommandCommandRiskClassifier? RiskClassifier { get; init; }
    public ExecuteCommandFilesystemEffectExtractor? FilesystemEffectExtractor { get; init; }
    public ExecuteCommandNetworkEffectClassifier? NetworkEffectClassifier { get; init; }
    public IReadOnlyList<ExecuteCommandShellFamily> SupportedShellFamilies { get; init; } =
    [
        ExecuteCommandShellFamily.Bash,
        ExecuteCommandShellFamily.Zsh,
        ExecuteCommandShellFamily.Sh,
        ExecuteCommandShellFamily.Unknown
    ];
}

internal sealed class ExecuteCommandSemanticPolicy
{
    private static readonly System.Text.RegularExpressions.Regex SafeSuggestionTokenPattern =
        new("^[a-z][a-z0-9]*(-[a-z0-9]+)*$|^-v$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<string> ExactPersistenceGateIds =
    [
        "safe-env-allow",
        "banned-prefixes",
        "too-complex-parser-handling",
        "parser-differential-corpus",
        "original-redirection-validation"
    ];

    private static readonly IReadOnlyList<string> PrefixPersistenceGateIds =
    [
        "safe-env-allow",
        "wrapper-normalization",
        "banned-prefixes",
        "prefix-compound-rejection",
        "too-complex-parser-handling",
        "parser-differential-corpus",
        "original-redirection-validation",
        "suggestion-arity"
    ];

    private readonly Dictionary<string, ExecuteCommandCommandFamilyPolicy> _familiesByPattern;
    private readonly Dictionary<string, ExecuteCommandPermissionParityGate> _parityGatesById;

    private ExecuteCommandSemanticPolicy(
        IReadOnlySet<string> safeEnvironmentVariables,
        IReadOnlySet<string> unsafeEnvironmentVariables,
        IReadOnlySet<string> blockedPrefixCommands,
        IReadOnlyList<ExecuteCommandCommandFamilyPolicy> commandFamilies)
    {
        SafeEnvironmentVariables = safeEnvironmentVariables;
        UnsafeEnvironmentVariables = unsafeEnvironmentVariables;
        BlockedPrefixCommands = blockedPrefixCommands;
        CommandFamilies = commandFamilies;
        _familiesByPattern = commandFamilies.ToDictionary(policy => policy.Pattern, StringComparer.Ordinal);
        _parityGatesById = ExecuteCommandPermissionParityChecklist.Entries.ToDictionary(gate => gate.Id, StringComparer.Ordinal);
    }

    public static ExecuteCommandSemanticPolicy Default { get; } = new(
        new HashSet<string>(StringComparer.Ordinal)
        {
            "GOOS", "GOARCH", "CGO_ENABLED", "RUST_BACKTRACE", "RUST_LOG", "NODE_ENV",
            "PYTHONUNBUFFERED", "PYTHONDONTWRITEBYTECODE", "NO_COLOR", "FORCE_COLOR",
            "TERM", "LANG", "LC_ALL", "TZ"
        },
        new HashSet<string>(StringComparer.Ordinal)
        {
            "PATH", "LD_PRELOAD", "LD_LIBRARY_PATH", "PYTHONPATH", "NODE_PATH", "NODE_OPTIONS",
            "RUSTFLAGS", "GOFLAGS", "HOME", "TMPDIR", "SHELL", "BASH_ENV", "DOCKER_HOST", "KUBECONFIG"
        },
        new HashSet<string>(StringComparer.Ordinal)
        {
            "sh", "bash", "zsh", "fish", "csh", "tcsh", "ksh", "dash",
            "cmd", "powershell", "pwsh", "env", "xargs", "sudo", "doas", "pkexec",
            "timeout", "time", "nice", "nohup", "stdbuf", "python", "python3", "node", "ruby", "perl"
        },
        [
            PrefixFamily("git status", [2]),
            PrefixFamily("git diff", [2], riskClassifier: GitDiffRisk, filesystemEffectExtractor: GitDiffEffects),
            PrefixFamily("git remote -v", [3]),
            PrefixFamily("dotnet test", [2]),
            PrefixFamily("dotnet build", [2]),
            PrefixFamily("npm run build", [3], ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely),
            PrefixFamily("npm run", [3, 2], ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely),
            PrefixFamily("npm test", [2], ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely),
            PrefixFamily("pnpm run", [2]),
            PrefixFamily("bun test", [2]),
            PrefixFamily("cargo test", [2]),
            PrefixFamily("gh pr check", [3], ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely),
            PrefixFamily("rg", [1], filesystemEffectExtractor: SearchEffects),
            PrefixFamily("grep", [1], filesystemEffectExtractor: SearchEffects),
            ExactFamily("cat", [1], filesystemEffectExtractor: ReadAllPathArgs),
            ExactFamily("head", [1], filesystemEffectExtractor: ReadAllPathArgs),
            ExactFamily("tail", [1], filesystemEffectExtractor: ReadAllPathArgs),
            OneTimeFamily("sudo", ExecuteCommandPermissionRisk.PrivilegeEscalation),
            OneTimeFamily("doas", ExecuteCommandPermissionRisk.PrivilegeEscalation),
            OneTimeFamily("pkexec", ExecuteCommandPermissionRisk.PrivilegeEscalation),
            OneTimeFamily("bash", ExecuteCommandPermissionRisk.None, ShellInvocationRisk),
            OneTimeFamily("sh", ExecuteCommandPermissionRisk.None, ShellInvocationRisk),
            OneTimeFamily("zsh", ExecuteCommandPermissionRisk.None, ShellInvocationRisk),
            OneTimeFamily("fish", ExecuteCommandPermissionRisk.None, ShellInvocationRisk),
            OneTimeFamily("cmd", ExecuteCommandPermissionRisk.None, ShellInvocationRisk),
            OneTimeFamily("powershell", ExecuteCommandPermissionRisk.None, ShellInvocationRisk),
            OneTimeFamily("pwsh", ExecuteCommandPermissionRisk.None, ShellInvocationRisk),
            OneTimeFamily("eval", ExecuteCommandPermissionRisk.DangerousShellBuiltin),
            OneTimeFamily("source", ExecuteCommandPermissionRisk.DangerousShellBuiltin),
            OneTimeFamily(".", ExecuteCommandPermissionRisk.DangerousShellBuiltin),
            OneTimeFamily("exec", ExecuteCommandPermissionRisk.DangerousShellBuiltin),
            OneTimeFamily("trap", ExecuteCommandPermissionRisk.DangerousShellBuiltin),
            OneTimeFamily("enable", ExecuteCommandPermissionRisk.DangerousShellBuiltin),
            OneTimeFamily("hash", ExecuteCommandPermissionRisk.DangerousShellBuiltin),
            OneTimeFamily("alias", ExecuteCommandPermissionRisk.DangerousShellBuiltin),
            OneTimeFamily("let", ExecuteCommandPermissionRisk.DangerousShellBuiltin),
            OneTimeFamily("rm", ExecuteCommandPermissionRisk.FilesystemMutation | ExecuteCommandPermissionRisk.Destructive, filesystemEffectExtractor: DeleteAllPathArgs),
            OneTimeFamily("mv", ExecuteCommandPermissionRisk.FilesystemMutation, filesystemEffectExtractor: MoveEffects),
            OneTimeFamily("cp", ExecuteCommandPermissionRisk.FilesystemMutation, filesystemEffectExtractor: CopyEffects),
            OneTimeFamily("mkdir", ExecuteCommandPermissionRisk.FilesystemMutation, filesystemEffectExtractor: CreateAllPathArgs),
            OneTimeFamily("touch", ExecuteCommandPermissionRisk.FilesystemMutation, filesystemEffectExtractor: CreateAllPathArgs),
            OneTimeFamily("chmod", ExecuteCommandPermissionRisk.FilesystemMutation, filesystemEffectExtractor: ChmodEffects),
            OneTimeFamily("find", ExecuteCommandPermissionRisk.None, FindRisk, FindEffects),
            OneTimeFamily("sed", ExecuteCommandPermissionRisk.None, SedRisk, SedEffects),
            OneTimeFamily("jq", ExecuteCommandPermissionRisk.None, JqRisk, JqEffects),
            OneTimeFamily("curl", ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely),
            OneTimeFamily("wget", ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely),
            OneTimeFamily("pip", ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely),
            OneTimeFamily("docker", ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely),
            OneTimeFamily("gh", ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely),
            OneTimeFamily("git", ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely),
            OneTimeFamily("npm", ExecuteCommandPermissionRisk.NetworkLikely, networkEffectClassifier: NetworkLikely)
        ]);

    public IReadOnlySet<string> SafeEnvironmentVariables { get; }

    public IReadOnlySet<string> UnsafeEnvironmentVariables { get; }

    public IReadOnlySet<string> BlockedPrefixCommands { get; }

    public IReadOnlyList<ExecuteCommandCommandFamilyPolicy> CommandFamilies { get; }

    public string? GetSafePrefix(IReadOnlyList<string> argv)
        => GetCommandFamilyPolicy(argv, safePrefix: null)?.Pattern;

    public ExecuteCommandCommandFamilyPolicy? GetCommandFamilyPolicy(
        IReadOnlyList<string> argv,
        string? safePrefix)
    {
        if (safePrefix is not null && _familiesByPattern.TryGetValue(safePrefix, out var safePrefixPolicy))
            return safePrefixPolicy;

        if (argv.Count == 0 || BlockedPrefixCommands.Contains(argv[0]))
            return null;

        foreach (var policy in CommandFamilies)
        {
            foreach (var arity in policy.SuggestionArities.OrderByDescending(static value => value))
            {
                if (argv.Count < arity)
                    continue;

                var candidateTokens = argv.Take(arity).ToArray();
                if (!CandidateTokensAreSafe(candidateTokens))
                    continue;

                var candidate = string.Join(' ', candidateTokens);
                if (string.Equals(candidate, policy.Pattern, StringComparison.Ordinal))
                    return policy;
            }
        }

        return null;
    }

    public ExecuteCommandPermissionRisk ClassifyCommandRisk(IReadOnlyList<string> argv)
        => CommandFamilies
            .Where(policy => MatchesPolicy(argv, policy))
            .Aggregate(ExecuteCommandPermissionRisk.None, (risk, policy) =>
                risk | policy.BaseRisk | (policy.RiskClassifier?.Invoke(argv) ?? ExecuteCommandPermissionRisk.None));

    public IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> ExtractFilesystemEffects(IReadOnlyList<string> argv)
        => CommandFamilies
            .Where(policy => MatchesPolicy(argv, policy) && policy.FilesystemEffectExtractor is not null)
            .SelectMany(policy => policy.FilesystemEffectExtractor!(argv));

    public bool HasNetworkEffect(IReadOnlyList<string> argv)
        => CommandFamilies.Any(policy =>
            MatchesPolicy(argv, policy) &&
            (policy.NetworkEffectClassifier?.Invoke(argv) == true ||
             policy.BaseRisk.HasFlag(ExecuteCommandPermissionRisk.NetworkLikely) &&
             policy.NetworkEffectClassifier is null));

    public ExecuteCommandPolicyReadiness ResolveReadiness(ExecuteCommandCommandFamilyPolicy? policy)
    {
        if (policy is null || policy.Readiness == ExecuteCommandPolicyReadiness.OneTimeOnly)
            return ExecuteCommandPolicyReadiness.OneTimeOnly;

        if (policy.RequiredParityGateIds.Count == 0)
            return ExecuteCommandPolicyReadiness.OneTimeOnly;

        foreach (var gateId in policy.RequiredParityGateIds)
        {
            if (!_parityGatesById.TryGetValue(gateId, out var gate) ||
                gate.Status < policy.MinimumParityStatus ||
                gate.RequiredCorpusIds.Count == 0)
            {
                return ExecuteCommandPolicyReadiness.OneTimeOnly;
            }
        }

        return policy.Readiness;
    }

    private static ExecuteCommandCommandFamilyPolicy PrefixFamily(
        string pattern,
        IReadOnlyList<int> suggestionArities,
        ExecuteCommandPermissionRisk baseRisk = ExecuteCommandPermissionRisk.None,
        ExecuteCommandCommandRiskClassifier? riskClassifier = null,
        ExecuteCommandFilesystemEffectExtractor? filesystemEffectExtractor = null,
        ExecuteCommandNetworkEffectClassifier? networkEffectClassifier = null,
        IReadOnlyList<string>? requiredParityGateIds = null)
        => Family(pattern, ExecuteCommandPolicyReadiness.PrefixAllowAllowed, suggestionArities, baseRisk, riskClassifier, filesystemEffectExtractor, networkEffectClassifier, requiredParityGateIds ?? PrefixPersistenceGateIds);

    private static ExecuteCommandCommandFamilyPolicy ExactFamily(
        string pattern,
        IReadOnlyList<int> suggestionArities,
        ExecuteCommandPermissionRisk baseRisk = ExecuteCommandPermissionRisk.None,
        ExecuteCommandCommandRiskClassifier? riskClassifier = null,
        ExecuteCommandFilesystemEffectExtractor? filesystemEffectExtractor = null,
        ExecuteCommandNetworkEffectClassifier? networkEffectClassifier = null,
        IReadOnlyList<string>? requiredParityGateIds = null)
        => Family(pattern, ExecuteCommandPolicyReadiness.ExactAllowOnly, suggestionArities, baseRisk, riskClassifier, filesystemEffectExtractor, networkEffectClassifier, requiredParityGateIds ?? ExactPersistenceGateIds);

    private static ExecuteCommandCommandFamilyPolicy OneTimeFamily(
        string pattern,
        ExecuteCommandPermissionRisk baseRisk,
        ExecuteCommandCommandRiskClassifier? riskClassifier = null,
        ExecuteCommandFilesystemEffectExtractor? filesystemEffectExtractor = null,
        ExecuteCommandNetworkEffectClassifier? networkEffectClassifier = null)
        => Family(pattern, ExecuteCommandPolicyReadiness.OneTimeOnly, [1], baseRisk, riskClassifier, filesystemEffectExtractor, networkEffectClassifier, requiredParityGateIds: []);

    private static ExecuteCommandCommandFamilyPolicy Family(
        string pattern,
        ExecuteCommandPolicyReadiness readiness,
        IReadOnlyList<int> suggestionArities,
        ExecuteCommandPermissionRisk baseRisk,
        ExecuteCommandCommandRiskClassifier? riskClassifier,
        ExecuteCommandFilesystemEffectExtractor? filesystemEffectExtractor,
        ExecuteCommandNetworkEffectClassifier? networkEffectClassifier,
        IReadOnlyList<string> requiredParityGateIds)
        => new()
        {
            SemanticPolicyVersion = ExecuteCommandPermissionAnalyzerVersions.SemanticPolicy,
            Pattern = pattern,
            Readiness = readiness,
            SuggestionArities = suggestionArities,
            RequiredParityGateIds = requiredParityGateIds,
            BaseRisk = baseRisk,
            RiskClassifier = riskClassifier,
            FilesystemEffectExtractor = filesystemEffectExtractor,
            NetworkEffectClassifier = networkEffectClassifier
        };

    private static bool MatchesPolicy(IReadOnlyList<string> argv, ExecuteCommandCommandFamilyPolicy policy)
    {
        var patternTokens = policy.Pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return argv.Count >= patternTokens.Length &&
               patternTokens.SequenceEqual(argv.Take(patternTokens.Length), StringComparer.Ordinal);
    }

    private static ExecuteCommandPermissionRisk ShellInvocationRisk(IReadOnlyList<string> argv)
        => argv.Any(token => token is "-c" or "/c" or "-Command" or "-EncodedCommand")
            ? ExecuteCommandPermissionRisk.ShellInvocation
            : ExecuteCommandPermissionRisk.None;

    private static ExecuteCommandPermissionRisk FindRisk(IReadOnlyList<string> argv)
        => argv.Any(token => token is "-exec" or "-execdir" or "-delete")
            ? ExecuteCommandPermissionRisk.DangerousShellBuiltin | ExecuteCommandPermissionRisk.FilesystemMutation
            : ExecuteCommandPermissionRisk.None;

    private static ExecuteCommandPermissionRisk SedRisk(IReadOnlyList<string> argv)
        => argv.Skip(1).Any(IsSedWriteOrExecuteToken)
            ? ExecuteCommandPermissionRisk.PathSensitiveWrite | ExecuteCommandPermissionRisk.FilesystemMutation
            : ExecuteCommandPermissionRisk.None;

    private static ExecuteCommandPermissionRisk JqRisk(IReadOnlyList<string> argv)
        => argv.Skip(1).Any(token => token is "--slurpfile" or "--rawfile" or "-L" or "--from-file" or "-f")
            ? ExecuteCommandPermissionRisk.OutsideWorkspaceReference
            : ExecuteCommandPermissionRisk.None;

    private static ExecuteCommandPermissionRisk GitDiffRisk(IReadOnlyList<string> argv)
        => argv.Count >= 3 && argv.Contains("--no-index", StringComparer.Ordinal)
            ? ExecuteCommandPermissionRisk.OutsideWorkspaceReference
            : ExecuteCommandPermissionRisk.None;

    private static bool NetworkLikely(IReadOnlyList<string> argv)
        => true;

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> ReadAllPathArgs(IReadOnlyList<string> argv)
        => GetPathCandidateArguments(argv.Skip(1))
            .Where(IsPathLikeArgument)
            .Select(path => (ExecuteCommandFilesystemOperation.Read, path));

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> SearchEffects(IReadOnlyList<string> argv)
        => GetPathCandidateArguments(argv.Skip(1))
            .Skip(1)
            .Where(IsPathLikeArgument)
            .Select(path => (ExecuteCommandFilesystemOperation.Read, path));

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> DeleteAllPathArgs(IReadOnlyList<string> argv)
        => GetPathCandidateArguments(argv.Skip(1))
            .Where(IsPathLikeArgument)
            .Select(path => (ExecuteCommandFilesystemOperation.Delete, path));

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> CreateAllPathArgs(IReadOnlyList<string> argv)
        => GetPathCandidateArguments(argv.Skip(1))
            .Where(IsPathLikeArgument)
            .Select(path => (ExecuteCommandFilesystemOperation.Create, path));

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> ChmodEffects(IReadOnlyList<string> argv)
        => GetPathCandidateArguments(argv.Skip(1))
            .Skip(1)
            .Where(IsPathLikeArgument)
            .Select(path => (ExecuteCommandFilesystemOperation.Write, path));

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> CopyEffects(IReadOnlyList<string> argv)
        => CopyMoveEffects(GetPathCandidateArguments(argv.Skip(1)), move: false);

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> MoveEffects(IReadOnlyList<string> argv)
        => CopyMoveEffects(GetPathCandidateArguments(argv.Skip(1)), move: true);

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> CopyMoveEffects(
        IReadOnlyList<string> args,
        bool move)
    {
        var paths = args.Where(IsPathLikeArgument).ToArray();
        if (paths.Length < 2)
            yield break;

        foreach (var source in paths[..^1])
            yield return (move ? ExecuteCommandFilesystemOperation.Delete : ExecuteCommandFilesystemOperation.Read, source);

        yield return (ExecuteCommandFilesystemOperation.Write, paths[^1]);
    }

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> FindEffects(IReadOnlyList<string> argv)
    {
        var args = argv.Skip(1).ToArray();
        foreach (var path in args.TakeWhile(arg => !IsFindExpressionToken(arg)).Where(IsPathLikeArgument))
            yield return (ExecuteCommandFilesystemOperation.Read, path);

        if (args.Contains("-delete", StringComparer.Ordinal))
            yield return (ExecuteCommandFilesystemOperation.Delete, ".");
    }

    private static bool IsFindExpressionToken(string arg)
        => arg.StartsWith("-", StringComparison.Ordinal) ||
           arg is "(" or ")" or "!" or "-o" or "-a";

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> SedEffects(IReadOnlyList<string> argv)
    {
        var args = argv.Skip(1).ToArray();
        var inPlace = false;
        var scriptConsumed = false;
        var fileArgs = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg == "--")
            {
                fileArgs.AddRange(args.Skip(index + 1).Where(IsPathLikeArgument));
                break;
            }
            if (arg == "-i" || arg.StartsWith("-i", StringComparison.Ordinal) || arg == "--in-place" || arg.StartsWith("--in-place=", StringComparison.Ordinal))
            {
                inPlace = true;
                continue;
            }
            if (arg is "-e" or "-f")
            {
                if (arg == "-f" && index + 1 < args.Length && IsPathLikeArgument(args[index + 1]))
                    yield return (ExecuteCommandFilesystemOperation.Read, args[index + 1]);
                scriptConsumed = true;
                index++;
                continue;
            }
            if (arg.StartsWith("-", StringComparison.Ordinal))
                continue;

            if (!scriptConsumed)
            {
                scriptConsumed = true;
                continue;
            }

            fileArgs.Add(arg);
        }

        foreach (var path in fileArgs.Where(IsPathLikeArgument))
            yield return (inPlace ? ExecuteCommandFilesystemOperation.Write : ExecuteCommandFilesystemOperation.Read, path);
    }

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> JqEffects(IReadOnlyList<string> argv)
    {
        var args = argv.Skip(1).ToArray();
        var filterConsumed = false;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--slurpfile" or "--rawfile")
            {
                if (index + 2 < args.Length && IsPathLikeArgument(args[index + 2]))
                    yield return (ExecuteCommandFilesystemOperation.Read, args[index + 2]);
                index += 2;
                continue;
            }
            if (arg is "-L" or "--from-file" or "-f")
            {
                if (index + 1 < args.Length && IsPathLikeArgument(args[index + 1]))
                    yield return (ExecuteCommandFilesystemOperation.Read, args[index + 1]);
                index++;
                continue;
            }
            if (arg.StartsWith("-", StringComparison.Ordinal))
                continue;
            if (!filterConsumed)
            {
                filterConsumed = true;
                continue;
            }
            if (IsPathLikeArgument(arg))
                yield return (ExecuteCommandFilesystemOperation.Read, arg);
        }
    }

    private static IEnumerable<(ExecuteCommandFilesystemOperation Operation, string Path)> GitDiffEffects(IReadOnlyList<string> argv)
    {
        if (argv.Count < 3 || !argv.Contains("--no-index", StringComparer.Ordinal))
            yield break;

        foreach (var arg in argv.SkipWhile(arg => arg != "--no-index").Skip(1).Where(IsPathLikeArgument))
            yield return (ExecuteCommandFilesystemOperation.Read, arg);
    }

    private static IReadOnlyList<string> GetPathCandidateArguments(IEnumerable<string> args)
    {
        var candidates = new List<string>();
        var forcePath = false;
        foreach (var arg in args)
        {
            if (!forcePath && arg == "--")
            {
                forcePath = true;
                continue;
            }

            if (!forcePath && arg.StartsWith("-", StringComparison.Ordinal))
                continue;

            candidates.Add(arg);
        }
        return candidates;
    }

    private static bool IsPathLikeArgument(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg) || arg == "--")
            return false;
        if (arg.StartsWith("-", StringComparison.Ordinal))
            return false;
        if (arg.Contains('$', StringComparison.Ordinal) ||
            arg.Contains('`', StringComparison.Ordinal) ||
            arg.Contains('*', StringComparison.Ordinal) ||
            arg.Contains('?', StringComparison.Ordinal) ||
            arg.Contains('{', StringComparison.Ordinal) ||
            arg.Contains('}', StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }

    private static bool IsSedWriteOrExecuteToken(string token)
        => token == "-i" ||
           token.StartsWith("-i", StringComparison.Ordinal) ||
           token == "--in-place" ||
           token.StartsWith("--in-place=", StringComparison.Ordinal) ||
           token.Contains(";w", StringComparison.Ordinal) ||
           token.Contains(";e", StringComparison.Ordinal) ||
           token.StartsWith("w ", StringComparison.Ordinal) ||
           token.StartsWith("e ", StringComparison.Ordinal);

    private bool CandidateTokensAreSafe(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0 || BlockedPrefixCommands.Contains(tokens[0]))
            return false;
        foreach (var token in tokens.Skip(1))
        {
            if (token.StartsWith("-", StringComparison.Ordinal) && token != "-v")
                return false;
            if (token.Contains('/', StringComparison.Ordinal) ||
                token.Contains('\\', StringComparison.Ordinal) ||
                Uri.TryCreate(token, UriKind.Absolute, out _))
            {
                return false;
            }
            if (!SafeSuggestionTokenPattern.IsMatch(token))
                return false;
        }
        return true;
    }
}

internal static class ExecuteCommandShellAnalyzer
{
    private const int MaxSegments = 50;
    private static readonly ExecuteCommandSemanticPolicy Policy = ExecuteCommandSemanticPolicy.Default;
    private static readonly IExecuteCommandShellFamilyAnalyzer Posix = new PosixShellFamilyAnalyzer();
    private static readonly IExecuteCommandShellFamilyAnalyzer PowerShell = new PowerShellShellFamilyAnalyzer();
    private static readonly IExecuteCommandShellFamilyAnalyzer Cmd = new CmdShellFamilyAnalyzer();

    public static ExecuteCommandShellAnalysis Analyze(
        RawCommandText command,
        ExecuteCommandShellScope shell,
        ExecuteCommandSemanticPolicy? policy = null)
        => GetAnalyzer(shell.Family).Analyze(command, shell, policy ?? Policy);

    private static IExecuteCommandShellFamilyAnalyzer GetAnalyzer(ExecuteCommandShellFamily family)
        => family switch
        {
            ExecuteCommandShellFamily.PowerShell => PowerShell,
            ExecuteCommandShellFamily.Cmd => Cmd,
            _ => Posix
        };

    private sealed class PowerShellShellFamilyAnalyzer : IExecuteCommandShellFamilyAnalyzer
    {
        public ExecuteCommandShellFamily Family => ExecuteCommandShellFamily.PowerShell;

        public ExecuteCommandShellAnalysis Analyze(
            RawCommandText command,
            ExecuteCommandShellScope shell,
            ExecuteCommandSemanticPolicy policy)
            => AnalyzePowerShell(command.Value);
    }

    private sealed class CmdShellFamilyAnalyzer : IExecuteCommandShellFamilyAnalyzer
    {
        public ExecuteCommandShellFamily Family => ExecuteCommandShellFamily.Cmd;

        public ExecuteCommandShellAnalysis Analyze(
            RawCommandText command,
            ExecuteCommandShellScope shell,
            ExecuteCommandSemanticPolicy policy)
            => AnalyzeCmd(command.Value);
    }

    private sealed class PosixShellFamilyAnalyzer : IExecuteCommandShellFamilyAnalyzer
    {
        public ExecuteCommandShellFamily Family => ExecuteCommandShellFamily.Sh;

        public ExecuteCommandShellAnalysis Analyze(
            RawCommandText command,
            ExecuteCommandShellScope shell,
            ExecuteCommandSemanticPolicy policy)
            => AnalyzePosix(command.Value, shell.Family);
    }

    private static ExecuteCommandShellAnalysis AnalyzePowerShell(string command)
    {
        var risk = ExecuteCommandPermissionRisk.UnknownOrUnparseable;
        var tokens = Tokenize(command);
        var features = new List<ExecuteCommandUnsupportedShellFeature>();
        if (tokens.Any(token => token.Equals("-Command", StringComparison.OrdinalIgnoreCase) ||
                                token.Equals("-EncodedCommand", StringComparison.OrdinalIgnoreCase)))
        {
            risk |= ExecuteCommandPermissionRisk.ShellInvocation;
            features.Add(ExecuteCommandUnsupportedShellFeature.ShellInvocation);
        }
        if (tokens.Any(token => token.Equals("-EncodedCommand", StringComparison.OrdinalIgnoreCase)))
        {
            features.Add(ExecuteCommandUnsupportedShellFeature.EncodedCommand);
        }
        if (tokens.Any(token => token.Equals("Invoke-Expression", StringComparison.OrdinalIgnoreCase) ||
                                token.Equals("iex", StringComparison.OrdinalIgnoreCase) ||
                                token.Equals("Start-Process", StringComparison.OrdinalIgnoreCase)))
        {
            risk |= ExecuteCommandPermissionRisk.DangerousShellBuiltin;
            features.Add(ExecuteCommandUnsupportedShellFeature.PowerShellInvocationOperator);
        }
        if (tokens.Any(token => token.Equals("Invoke-WebRequest", StringComparison.OrdinalIgnoreCase) ||
                                token.Equals("iwr", StringComparison.OrdinalIgnoreCase) ||
                                token.Equals("curl", StringComparison.OrdinalIgnoreCase)))
        {
            risk |= ExecuteCommandPermissionRisk.NetworkLikely;
            features.Add(ExecuteCommandUnsupportedShellFeature.PowerShellAlias);
        }
        if (tokens.Any(token => token.Equals("Out-File", StringComparison.OrdinalIgnoreCase) ||
                                token.Equals("Set-Content", StringComparison.OrdinalIgnoreCase) ||
                                token.Equals("Add-Content", StringComparison.OrdinalIgnoreCase)))
        {
            features.Add(ExecuteCommandUnsupportedShellFeature.PowerShellFileWritingCommand);
        }
        if (command.Contains('|', StringComparison.Ordinal) ||
            command.Contains('>', StringComparison.Ordinal) ||
            command.Contains('<', StringComparison.Ordinal))
        {
            risk |= ExecuteCommandPermissionRisk.CompoundCommand | ExecuteCommandPermissionRisk.OutputRedirection;
            if (command.Contains('|', StringComparison.Ordinal))
                features.Add(ExecuteCommandUnsupportedShellFeature.Pipeline);
            if (command.Contains('>', StringComparison.Ordinal) || command.Contains('<', StringComparison.Ordinal))
                features.Add(ExecuteCommandUnsupportedShellFeature.OutputRedirection);
        }
        if (command.Contains("$(", StringComparison.Ordinal) ||
            command.Contains('&', StringComparison.Ordinal))
        {
            risk |= ExecuteCommandPermissionRisk.CommandSubstitution;
            if (command.Contains("$(", StringComparison.Ordinal))
                features.Add(ExecuteCommandUnsupportedShellFeature.PowerShellSubexpression);
            if (command.Contains('&', StringComparison.Ordinal))
                features.Add(ExecuteCommandUnsupportedShellFeature.PowerShellInvocationOperator);
        }
        if (command.Contains('{', StringComparison.Ordinal) && command.Contains('}', StringComparison.Ordinal))
        {
            risk |= ExecuteCommandPermissionRisk.ParserDifferentialRisk;
            features.Add(ExecuteCommandUnsupportedShellFeature.ScriptBlockInvocation);
        }

        return StampAnalysis(new ExecuteCommandShellAnalysis(
            [CreateSegment(command, tokens, risk, ExecuteCommandAnalysisTrustLevel.ReviewOnly, allowPrefix: false)],
            risk,
            ExecuteCommandAnalysisTrustLevel.ReviewOnly),
            ExecuteCommandShellFamily.PowerShell,
            nameof(PowerShellShellFamilyAnalyzer),
            "PowerShell support is conservative until command-specific parsing is modeled.",
            features.Distinct().ToArray());
    }

    private static ExecuteCommandShellAnalysis AnalyzeCmd(string command)
    {
        var risk = ExecuteCommandPermissionRisk.UnknownOrUnparseable;
        var tokens = Tokenize(command);
        var features = new List<ExecuteCommandUnsupportedShellFeature>();
        if (tokens.Any(token => token.Equals("/c", StringComparison.OrdinalIgnoreCase) ||
                                token.Equals("/k", StringComparison.OrdinalIgnoreCase)))
        {
            risk |= ExecuteCommandPermissionRisk.ShellInvocation;
            features.Add(ExecuteCommandUnsupportedShellFeature.CmdCommandSwitch);
            features.Add(ExecuteCommandUnsupportedShellFeature.ShellInvocation);
        }
        if (tokens.Any(token => token.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                                token.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)))
        {
            risk |= ExecuteCommandPermissionRisk.ParserDifferentialRisk;
            features.Add(ExecuteCommandUnsupportedShellFeature.CmdBatchDispatch);
        }
        if (tokens.Any(token => token.Equals("for", StringComparison.OrdinalIgnoreCase) ||
                                token.Equals("call", StringComparison.OrdinalIgnoreCase) ||
                                token.Equals("set", StringComparison.OrdinalIgnoreCase)) ||
            command.Contains('%', StringComparison.Ordinal) ||
            command.Contains('!', StringComparison.Ordinal))
        {
            risk |= ExecuteCommandPermissionRisk.ParserDifferentialRisk;
            if (tokens.Any(token => token.Equals("for", StringComparison.OrdinalIgnoreCase)))
                features.Add(ExecuteCommandUnsupportedShellFeature.CmdFor);
            if (tokens.Any(token => token.Equals("call", StringComparison.OrdinalIgnoreCase)))
                features.Add(ExecuteCommandUnsupportedShellFeature.CmdCall);
            if (tokens.Any(token => token.Equals("set", StringComparison.OrdinalIgnoreCase)))
                features.Add(ExecuteCommandUnsupportedShellFeature.CmdSet);
            if (command.Contains('%', StringComparison.Ordinal))
                features.Add(ExecuteCommandUnsupportedShellFeature.CmdPercentExpansion);
            if (command.Contains('!', StringComparison.Ordinal))
                features.Add(ExecuteCommandUnsupportedShellFeature.CmdDelayedExpansion);
        }
        if (command.Contains('&', StringComparison.Ordinal) ||
            command.Contains('|', StringComparison.Ordinal) ||
            command.Contains('>', StringComparison.Ordinal) ||
            command.Contains('<', StringComparison.Ordinal))
        {
            risk |= ExecuteCommandPermissionRisk.CompoundCommand | ExecuteCommandPermissionRisk.OutputRedirection;
            if (command.Contains('|', StringComparison.Ordinal))
                features.Add(ExecuteCommandUnsupportedShellFeature.Pipeline);
            if (command.Contains('>', StringComparison.Ordinal) || command.Contains('<', StringComparison.Ordinal))
                features.Add(ExecuteCommandUnsupportedShellFeature.OutputRedirection);
        }

        return StampAnalysis(new ExecuteCommandShellAnalysis(
            [CreateSegment(command, tokens, risk, ExecuteCommandAnalysisTrustLevel.ReviewOnly, allowPrefix: false)],
            risk,
            ExecuteCommandAnalysisTrustLevel.ReviewOnly),
            ExecuteCommandShellFamily.Cmd,
            nameof(CmdShellFamilyAnalyzer),
            "cmd.exe support is conservative until command-specific parsing is modeled.",
            features.Distinct().ToArray());
    }

    private static ExecuteCommandShellAnalysis AnalyzePosix(string command, ExecuteCommandShellFamily family)
    {
        var parse = ParsePosix(new RawCommandText(command), family);
        if (parse.Risk.HasFlag(ExecuteCommandPermissionRisk.ParserDifferentialRisk))
        {
            return StampAnalysis(new ExecuteCommandShellAnalysis(
                parse.Segments.Count == 0
                    ? [CreateSegment(command, [], parse.Risk, ExecuteCommandAnalysisTrustLevel.Untrusted, allowPrefix: false)]
                    : [CreateSegment(parse.Segments[0], parse.Risk, ExecuteCommandAnalysisTrustLevel.Untrusted, allowPrefix: false)],
                parse.Risk,
                ExecuteCommandAnalysisTrustLevel.Untrusted),
                family,
                nameof(PosixShellFamilyAnalyzer),
                "POSIX parser differential risk blocked remembered permission.",
                parse.UnsupportedFeatures);
        }

        var risk = parse.Risk;
        var unsupportedFeatures = parse.UnsupportedFeatures;
        if (risk.HasFlag(ExecuteCommandPermissionRisk.OutputRedirection))
        {
            var redirected = CreateSegment(parse.Segments[0], risk, ExecuteCommandAnalysisTrustLevel.ReviewOnly);
            unsupportedFeatures = [.. unsupportedFeatures, .. GetSegmentUnsupportedFeatures(redirected)];
            return StampAnalysis(new ExecuteCommandShellAnalysis(
                [redirected],
                redirected.Risk,
                ExecuteCommandAnalysisTrustLevel.ReviewOnly),
                family,
                nameof(PosixShellFamilyAnalyzer),
                "POSIX redirection requires review.",
                unsupportedFeatures.Distinct().ToArray());
        }

        if (risk.HasFlag(ExecuteCommandPermissionRisk.CommandSubstitution) ||
            risk.HasFlag(ExecuteCommandPermissionRisk.Heredoc) ||
            risk.HasFlag(ExecuteCommandPermissionRisk.ShellInvocation) ||
            risk.HasFlag(ExecuteCommandPermissionRisk.DangerousShellBuiltin) ||
            risk.HasFlag(ExecuteCommandPermissionRisk.BareVariableExpansion))
        {
            var visible = CreateSegment(parse.Segments[0], risk, ExecuteCommandAnalysisTrustLevel.ReviewOnly);
            unsupportedFeatures = [.. unsupportedFeatures, .. GetSegmentUnsupportedFeatures(visible)];
            return StampAnalysis(new ExecuteCommandShellAnalysis(
                [visible],
                visible.Risk,
                ExecuteCommandAnalysisTrustLevel.ReviewOnly),
                family,
                nameof(PosixShellFamilyAnalyzer),
                "POSIX command shape uses unsupported or high-risk shell features.",
                unsupportedFeatures.Distinct().ToArray());
        }

        if (parse.Segments.Count > MaxSegments)
        {
            return StampAnalysis(new ExecuteCommandShellAnalysis(
                [CreateSegment(command, [], risk | ExecuteCommandPermissionRisk.UnknownOrUnparseable, ExecuteCommandAnalysisTrustLevel.Untrusted)],
                risk | ExecuteCommandPermissionRisk.UnknownOrUnparseable,
                ExecuteCommandAnalysisTrustLevel.Untrusted),
                family,
                nameof(PosixShellFamilyAnalyzer),
                "POSIX command exceeded the maximum segment count.",
                [ExecuteCommandUnsupportedShellFeature.ExcessiveSegments]);
        }

        if (parse.Operators.Count > 0)
            risk |= ExecuteCommandPermissionRisk.CompoundCommand;

        var plans = parse.Segments
            .Select(segment => CreateSegment(segment, risk, parse.Operators.Count > 0 ? ExecuteCommandAnalysisTrustLevel.Segmented : ExecuteCommandAnalysisTrustLevel.Simple))
            .ToArray();
        risk |= plans.Aggregate(ExecuteCommandPermissionRisk.None, (current, segment) => current | segment.Risk);
        unsupportedFeatures = [.. unsupportedFeatures, .. plans.SelectMany(GetSegmentUnsupportedFeatures)];

        if (plans.Any(segment => segment.TrustLevel == ExecuteCommandAnalysisTrustLevel.ReviewOnly))
        {
            return StampAnalysis(new ExecuteCommandShellAnalysis(plans, risk, ExecuteCommandAnalysisTrustLevel.ReviewOnly),
                family,
                nameof(PosixShellFamilyAnalyzer),
                "At least one POSIX segment requires review.",
                unsupportedFeatures.Distinct().ToArray());
        }

        if (plans.Any(segment => string.Equals(segment.BaseCommand, "cd", StringComparison.Ordinal) ||
                                 string.Equals(segment.BaseCommand, "pushd", StringComparison.Ordinal) ||
                                 string.Equals(segment.BaseCommand, "popd", StringComparison.Ordinal)) &&
            plans.Any(segment => string.Equals(segment.BaseCommand, "git", StringComparison.Ordinal) ||
                                 segment.Risk.HasFlag(ExecuteCommandPermissionRisk.FilesystemMutation)))
        {
            risk |= ExecuteCommandPermissionRisk.CompoundWithDirectoryChange;
            return StampAnalysis(new ExecuteCommandShellAnalysis(plans, risk, ExecuteCommandAnalysisTrustLevel.ReviewOnly),
                family,
                nameof(PosixShellFamilyAnalyzer),
                "POSIX compound command changes directory before path-sensitive operations.",
                [.. unsupportedFeatures, ExecuteCommandUnsupportedShellFeature.DirectoryChangeCompound]);
        }

        return StampAnalysis(new ExecuteCommandShellAnalysis(
            plans,
            risk,
            parse.Operators.Count > 0 ? ExecuteCommandAnalysisTrustLevel.Segmented : ExecuteCommandAnalysisTrustLevel.Simple),
            family,
            nameof(PosixShellFamilyAnalyzer),
            unsupportedFeatureReason: null,
            unsupportedFeatures.Distinct().ToArray());
    }

    internal static ExecuteCommandShellParseResult ParsePosix(RawCommandText command, ExecuteCommandShellFamily family)
    {
        var risk = ExecuteCommandPermissionRisk.None;
        var features = new List<ExecuteCommandUnsupportedShellFeature>();
        features.AddRange(GetParserDifferentialFeatures(command.Value));
        if (features.Count > 0 && features.Contains(ExecuteCommandUnsupportedShellFeature.ParserDifferential))
            risk |= ExecuteCommandPermissionRisk.ParserDifferentialRisk;

        var operators = new List<ExecuteCommandShellOperatorParse>();
        var segments = new List<ExecuteCommandShellSegmentParse>();
        var current = new StringBuilder();
        var quote = '\0';
        var segmentStart = 0;
        var commandSubstitutionDepth = 0;
        var subshellDepth = 0;
        var inBacktickCommandSubstitution = false;

        for (var i = 0; i < command.Value.Length; i++)
        {
            var ch = command.Value[i];
            if (quote != '\0')
            {
                current.Append(ch);
                if (ch == quote)
                    quote = '\0';
                continue;
            }

            if (inBacktickCommandSubstitution)
            {
                current.Append(ch);
                if (ch == '`')
                    inBacktickCommandSubstitution = false;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                current.Append(ch);
                continue;
            }

            if (ch == '`')
            {
                inBacktickCommandSubstitution = true;
                current.Append(ch);
                continue;
            }

            if (ch == '$' && i + 1 < command.Value.Length && command.Value[i + 1] == '(')
            {
                commandSubstitutionDepth++;
                current.Append(ch);
                current.Append(command.Value[++i]);
                continue;
            }

            if (commandSubstitutionDepth > 0)
            {
                current.Append(ch);
                if (ch == '(')
                    commandSubstitutionDepth++;
                else if (ch == ')')
                    commandSubstitutionDepth--;
                continue;
            }

            if (ch == '(')
            {
                subshellDepth++;
                current.Append(ch);
                continue;
            }

            if (subshellDepth > 0)
            {
                current.Append(ch);
                if (ch == '(')
                    subshellDepth++;
                else if (ch == ')')
                    subshellDepth--;
                continue;
            }

            if (TryReadPosixOperator(command.Value, i, out var operatorParse, out var consumed))
            {
                AddParsedSegment(segments, current, segmentStart);
                current.Clear();
                operators.Add(operatorParse);
                i += consumed - 1;
                segmentStart = i + 1;
                continue;
            }

            current.Append(ch);
        }

        AddParsedSegment(segments, current, segmentStart);
        if (segments.Count == 0)
            AddParsedSegment(segments, new StringBuilder(command.Value), 0);
        if (operators.Count > 0)
            risk |= ExecuteCommandPermissionRisk.CompoundCommand;
        if (segments.Count > MaxSegments)
        {
            risk |= ExecuteCommandPermissionRisk.UnknownOrUnparseable;
            features.Add(ExecuteCommandUnsupportedShellFeature.ExcessiveSegments);
        }

        var expansions = FindPosixExpansions(command.Value, 0);
        var heredocs = FindPosixHeredocs(command.Value, 0);
        var subshells = FindPosixSubshells(command.Value, 0);
        risk |= GetPosixNodeRisk(expansions, heredocs, subshells, operators);
        risk |= segments.Aggregate(ExecuteCommandPermissionRisk.None, (current, segment) => current | segment.Risk);
        features.AddRange(GetPosixNodeUnsupportedFeatures(expansions, heredocs, subshells, operators));
        features.AddRange(segments.SelectMany(segment => segment.UnsupportedFeatures));

        return new ExecuteCommandShellParseResult
        {
            Command = command,
            Family = family,
            Segments = segments,
            Operators = operators,
            Expansions = expansions,
            Heredocs = heredocs,
            Subshells = subshells,
            Risk = risk,
            UnsupportedFeatures = features.Distinct().ToArray()
        };
    }

    private static bool TryReadPosixOperator(
        string command,
        int index,
        out ExecuteCommandShellOperatorParse operatorParse,
        out int consumed)
    {
        var ch = command[index];
        if (ch == '&' && index + 1 < command.Length && command[index + 1] == '&')
        {
            operatorParse = new ExecuteCommandShellOperatorParse { Kind = ExecuteCommandShellOperatorKind.And, Text = "&&", Span = new ExecuteCommandShellSourceSpan(index, 2) };
            consumed = 2;
            return true;
        }
        if (ch == '|' && index + 1 < command.Length && command[index + 1] == '|')
        {
            operatorParse = new ExecuteCommandShellOperatorParse { Kind = ExecuteCommandShellOperatorKind.Or, Text = "||", Span = new ExecuteCommandShellSourceSpan(index, 2) };
            consumed = 2;
            return true;
        }
        if (ch == '|')
        {
            operatorParse = new ExecuteCommandShellOperatorParse { Kind = ExecuteCommandShellOperatorKind.Pipe, Text = "|", Span = new ExecuteCommandShellSourceSpan(index, 1) };
            consumed = 1;
            return true;
        }
        if (ch == ';')
        {
            operatorParse = new ExecuteCommandShellOperatorParse { Kind = ExecuteCommandShellOperatorKind.Separator, Text = ";", Span = new ExecuteCommandShellSourceSpan(index, 1) };
            consumed = 1;
            return true;
        }
        if (ch == '\n')
        {
            operatorParse = new ExecuteCommandShellOperatorParse { Kind = ExecuteCommandShellOperatorKind.Newline, Text = "\n", Span = new ExecuteCommandShellSourceSpan(index, 1) };
            consumed = 1;
            return true;
        }

        operatorParse = null!;
        consumed = 0;
        return false;
    }

    private static void AddParsedSegment(
        List<ExecuteCommandShellSegmentParse> segments,
        StringBuilder current,
        int segmentStart)
    {
        var rawText = current.ToString();
        var leadingWhitespace = rawText.TakeWhile(char.IsWhiteSpace).Count();
        var text = rawText.Trim();
        if (text.Length == 0)
            return;

        var span = new ExecuteCommandShellSourceSpan(segmentStart + leadingWhitespace, text.Length);
        var rawTokens = ParsePosixTokens(text, span.Start);
        var redirectionResult = ExtractRedirections(rawTokens.Select(token => token.Text).ToArray());
        var risk = redirectionResult.Risk;
        var expansions = FindPosixExpansions(text, span.Start);
        var heredocs = FindPosixHeredocs(text, span.Start);
        var subshells = FindPosixSubshells(text, span.Start);
        risk |= GetPosixNodeRisk(expansions, heredocs, subshells, operators: []);
        var features = GetPosixNodeUnsupportedFeatures(expansions, heredocs, subshells, operators: [])
            .Concat(redirectionResult.Risk.HasFlag(ExecuteCommandPermissionRisk.UnsafeRedirectionTarget)
                ? [ExecuteCommandUnsupportedShellFeature.UnsafeRedirectionTarget]
                : Array.Empty<ExecuteCommandUnsupportedShellFeature>())
            .ToArray();

        segments.Add(new ExecuteCommandShellSegmentParse
        {
            Text = text,
            Span = span,
            Tokens = RemoveRedirectionTokens(rawTokens),
            Redirections = redirectionResult.Redirections,
            Expansions = expansions,
            Heredocs = heredocs,
            Subshells = subshells,
            Risk = risk,
            UnsupportedFeatures = features.Distinct().ToArray()
        });
    }

    private static IReadOnlyList<ExecuteCommandShellToken> RemoveRedirectionTokens(
        IReadOnlyList<ExecuteCommandShellToken> tokens)
    {
        var commandTokens = new List<ExecuteCommandShellToken>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var split = TrySplitAttachedRedirection(tokens[i].Text);
            if (TryParseRedirectionOperator(split.Operator, out _, out _))
            {
                if (split.Target is null && i + 1 < tokens.Count)
                    i++;
                continue;
            }

            commandTokens.Add(tokens[i]);
        }

        return commandTokens;
    }

    private static IReadOnlyList<ExecuteCommandShellToken> ParsePosixTokens(string command, int commandOffset)
    {
        var tokens = new List<ExecuteCommandShellToken>();
        var current = new StringBuilder();
        var quote = '\0';
        var kind = ExecuteCommandShellTokenKind.Word;
        var tokenStart = -1;

        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                    continue;
                }
                if (tokenStart < 0)
                    tokenStart = commandOffset + i;
                current.Append(ch);
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                if (tokenStart < 0)
                    tokenStart = commandOffset + i;
                if (current.Length == 0)
                    kind = ch == '\'' ? ExecuteCommandShellTokenKind.SingleQuoted : ExecuteCommandShellTokenKind.DoubleQuoted;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                AddToken(tokens, current, kind, tokenStart);
                kind = ExecuteCommandShellTokenKind.Word;
                tokenStart = -1;
                continue;
            }

            if (tokenStart < 0)
                tokenStart = commandOffset + i;
            current.Append(ch);
        }

        AddToken(tokens, current, kind, tokenStart);
        return tokens;
    }

    private static IReadOnlyList<ExecuteCommandShellExpansionParse> FindPosixExpansions(string command, int commandOffset)
    {
        var expansions = new List<ExecuteCommandShellExpansionParse>();
        var inSingleQuote = false;
        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (inSingleQuote)
            {
                if (ch == '\'')
                    inSingleQuote = false;
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (ch == '"')
                continue;

            if (ch == '`')
            {
                var end = command.IndexOf('`', i + 1);
                if (end < 0)
                    end = command.Length - 1;
                expansions.Add(new ExecuteCommandShellExpansionParse
                {
                    Kind = ExecuteCommandShellExpansionKind.BacktickCommandSubstitution,
                    Text = command[i..(end + 1)],
                    Span = new ExecuteCommandShellSourceSpan(commandOffset + i, end - i + 1)
                });
                i = end;
                continue;
            }

            if (ch == '$' && i + 1 < command.Length && command[i + 1] == '(')
            {
                var end = FindMatchingParen(command, i + 1);
                if (end < 0)
                    end = command.Length - 1;
                expansions.Add(new ExecuteCommandShellExpansionParse
                {
                    Kind = ExecuteCommandShellExpansionKind.CommandSubstitution,
                    Text = command[i..(end + 1)],
                    Span = new ExecuteCommandShellSourceSpan(commandOffset + i, end - i + 1)
                });
                i = end;
                continue;
            }

            if (ch == '$' &&
                i + 1 < command.Length &&
                (char.IsLetter(command[i + 1]) || command[i + 1] == '_'))
            {
                var end = i + 2;
                while (end < command.Length && (char.IsLetterOrDigit(command[end]) || command[end] == '_'))
                    end++;
                expansions.Add(new ExecuteCommandShellExpansionParse
                {
                    Kind = ExecuteCommandShellExpansionKind.BareVariable,
                    Text = command[i..end],
                    Span = new ExecuteCommandShellSourceSpan(commandOffset + i, end - i)
                });
                i = end - 1;
            }
        }

        return expansions;
    }

    private static IReadOnlyList<ExecuteCommandShellSubshellParse> FindPosixSubshells(string command, int commandOffset)
    {
        var subshells = new List<ExecuteCommandShellSubshellParse>();
        var quote = '\0';
        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch != '(' || (i > 0 && command[i - 1] == '$'))
                continue;

            var end = FindMatchingParen(command, i);
            if (end < 0)
                continue;

            subshells.Add(new ExecuteCommandShellSubshellParse
            {
                Text = command[i..(end + 1)],
                Span = new ExecuteCommandShellSourceSpan(commandOffset + i, end - i + 1)
            });
            i = end;
        }

        return subshells;
    }

    private static int FindMatchingParen(string command, int openIndex)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = openIndex; i < command.Length; i++)
        {
            var ch = command[i];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch == '(')
                depth++;
            else if (ch == ')' && --depth == 0)
                return i;
        }

        return -1;
    }

    private static IReadOnlyList<ExecuteCommandShellHeredocParse> FindPosixHeredocs(string command, int commandOffset)
    {
        var heredocs = new List<ExecuteCommandShellHeredocParse>();
        var quote = '\0';
        for (var i = 0; i + 1 < command.Length; i++)
        {
            var ch = command[i];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (ch != '<' || command[i + 1] != '<')
                continue;

            var operatorEnd = i + 2;
            if (operatorEnd < command.Length && command[operatorEnd] == '-')
                operatorEnd++;
            var delimiterStart = operatorEnd;
            while (delimiterStart < command.Length && char.IsWhiteSpace(command[delimiterStart]) && command[delimiterStart] != '\n')
                delimiterStart++;
            if (delimiterStart >= command.Length)
                continue;

            var delimiterEnd = delimiterStart;
            var delimiterQuoted = command[delimiterStart] is '\'' or '"';
            if (delimiterQuoted)
            {
                var delimiterQuote = command[delimiterStart];
                delimiterEnd = command.IndexOf(delimiterQuote, delimiterStart + 1);
                if (delimiterEnd < 0)
                    delimiterEnd = command.Length - 1;
            }
            else
            {
                while (delimiterEnd < command.Length && !char.IsWhiteSpace(command[delimiterEnd]))
                    delimiterEnd++;
            }

            var rawDelimiterEnd = delimiterQuoted ? delimiterEnd + 1 : delimiterEnd;
            var rawDelimiter = command[delimiterStart..rawDelimiterEnd];
            var delimiter = rawDelimiter.Trim('\'', '"');
            var body = TryReadHeredocBody(command, rawDelimiterEnd, delimiter);
            heredocs.Add(new ExecuteCommandShellHeredocParse
            {
                Operator = command[i..operatorEnd],
                Delimiter = delimiter,
                DelimiterQuoted = delimiterQuoted,
                Body = body,
                Span = new ExecuteCommandShellSourceSpan(commandOffset + i, Math.Max(operatorEnd, rawDelimiterEnd) - i)
            });
            i = delimiterEnd;
        }

        return heredocs;
    }

    private static string? TryReadHeredocBody(string command, int searchStart, string delimiter)
    {
        if (searchStart >= command.Length)
            return null;

        var firstNewline = command.IndexOf('\n', searchStart);
        if (firstNewline < 0)
            return null;

        var bodyStart = firstNewline + 1;
        var currentLineStart = bodyStart;
        while (currentLineStart <= command.Length)
        {
            var lineEnd = command.IndexOf('\n', currentLineStart);
            if (lineEnd < 0)
                lineEnd = command.Length;
            var line = command[currentLineStart..lineEnd];
            if (string.Equals(line, delimiter, StringComparison.Ordinal))
                return command[bodyStart..currentLineStart].TrimEnd('\n');
            if (lineEnd == command.Length)
                break;
            currentLineStart = lineEnd + 1;
        }

        return null;
    }

    private static void AddToken(
        List<ExecuteCommandShellToken> tokens,
        StringBuilder current,
        ExecuteCommandShellTokenKind kind,
        int tokenStart)
    {
        if (current.Length == 0)
            return;
        tokens.Add(new ExecuteCommandShellToken
        {
            Text = current.ToString(),
            Kind = kind,
            Span = new ExecuteCommandShellSourceSpan(tokenStart, current.Length)
        });
        current.Clear();
    }

    private static ExecuteCommandShellAnalysis StampAnalysis(
        ExecuteCommandShellAnalysis analysis,
        ExecuteCommandShellFamily family,
        string analyzerName,
        string? unsupportedFeatureReason,
        IReadOnlyList<ExecuteCommandUnsupportedShellFeature>? unsupportedFeatures = null)
        => analysis with
        {
            Family = family,
            AnalyzerName = analyzerName,
            UnsupportedFeatures = unsupportedFeatures ?? [],
            UnsupportedFeatureReason = unsupportedFeatureReason
        };

    private static IReadOnlyList<ExecuteCommandUnsupportedShellFeature> GetParserDifferentialFeatures(string command)
    {
        var features = new List<ExecuteCommandUnsupportedShellFeature>();
        if (command.Any(ch => char.IsControl(ch) && ch is not '\n' and not '\t'))
        {
            features.Add(ExecuteCommandUnsupportedShellFeature.ParserDifferential);
            features.Add(ExecuteCommandUnsupportedShellFeature.ControlCharacter);
        }
        if (command.Contains('\r', StringComparison.Ordinal))
        {
            features.Add(ExecuteCommandUnsupportedShellFeature.ParserDifferential);
            features.Add(ExecuteCommandUnsupportedShellFeature.CarriageReturn);
        }
        if (ContainsUnicodeWhitespace(command))
        {
            features.Add(ExecuteCommandUnsupportedShellFeature.ParserDifferential);
            features.Add(ExecuteCommandUnsupportedShellFeature.UnicodeWhitespace);
        }
        if (ContainsEscapedShellOperator(command))
        {
            features.Add(ExecuteCommandUnsupportedShellFeature.ParserDifferential);
            features.Add(ExecuteCommandUnsupportedShellFeature.EscapedOperator);
        }
        if (ContainsMidWordCommentMarker(command))
        {
            features.Add(ExecuteCommandUnsupportedShellFeature.ParserDifferential);
            features.Add(ExecuteCommandUnsupportedShellFeature.MidWordComment);
        }
        if (ContainsQuotedNewlineComment(command))
        {
            features.Add(ExecuteCommandUnsupportedShellFeature.ParserDifferential);
            features.Add(ExecuteCommandUnsupportedShellFeature.QuotedNewlineComment);
        }
        if (ContainsBraceExpansion(command))
        {
            features.Add(ExecuteCommandUnsupportedShellFeature.ParserDifferential);
            features.Add(ExecuteCommandUnsupportedShellFeature.BraceExpansion);
        }
        return features.Distinct().ToArray();
    }

    private static ExecuteCommandPermissionRisk GetPosixNodeRisk(
        IReadOnlyList<ExecuteCommandShellExpansionParse> expansions,
        IReadOnlyList<ExecuteCommandShellHeredocParse> heredocs,
        IReadOnlyList<ExecuteCommandShellSubshellParse> subshells,
        IReadOnlyList<ExecuteCommandShellOperatorParse> operators)
    {
        var risk = ExecuteCommandPermissionRisk.None;
        if (operators.Count > 0)
            risk |= ExecuteCommandPermissionRisk.CompoundCommand;
        if (operators.Any(op => op.Kind == ExecuteCommandShellOperatorKind.Pipe))
            risk |= ExecuteCommandPermissionRisk.CompoundCommand;
        if (expansions.Any(expansion => expansion.Kind is ExecuteCommandShellExpansionKind.CommandSubstitution or ExecuteCommandShellExpansionKind.BacktickCommandSubstitution))
            risk |= ExecuteCommandPermissionRisk.CommandSubstitution;
        if (expansions.Any(expansion => expansion.Kind == ExecuteCommandShellExpansionKind.BareVariable))
            risk |= ExecuteCommandPermissionRisk.BareVariableExpansion;
        if (heredocs.Count > 0)
            risk |= ExecuteCommandPermissionRisk.Heredoc;
        if (subshells.Count > 0)
            risk |= ExecuteCommandPermissionRisk.CompoundCommand | ExecuteCommandPermissionRisk.Subshell;
        return risk;
    }

    private static IReadOnlyList<ExecuteCommandUnsupportedShellFeature> GetPosixNodeUnsupportedFeatures(
        IReadOnlyList<ExecuteCommandShellExpansionParse> expansions,
        IReadOnlyList<ExecuteCommandShellHeredocParse> heredocs,
        IReadOnlyList<ExecuteCommandShellSubshellParse> subshells,
        IReadOnlyList<ExecuteCommandShellOperatorParse> operators)
    {
        var features = new List<ExecuteCommandUnsupportedShellFeature>();
        foreach (var expansion in expansions)
        {
            features.Add(expansion.Kind switch
            {
                ExecuteCommandShellExpansionKind.CommandSubstitution => ExecuteCommandUnsupportedShellFeature.CommandSubstitution,
                ExecuteCommandShellExpansionKind.BacktickCommandSubstitution => ExecuteCommandUnsupportedShellFeature.CommandSubstitution,
                ExecuteCommandShellExpansionKind.BareVariable => ExecuteCommandUnsupportedShellFeature.BareVariableExpansion,
                _ => throw new InvalidOperationException($"Unsupported POSIX expansion kind '{expansion.Kind}'.")
            });
        }
        if (heredocs.Count > 0)
            features.Add(ExecuteCommandUnsupportedShellFeature.Heredoc);
        if (subshells.Count > 0)
            features.Add(ExecuteCommandUnsupportedShellFeature.Subshell);
        if (operators.Any(op => op.Kind == ExecuteCommandShellOperatorKind.Pipe))
            features.Add(ExecuteCommandUnsupportedShellFeature.Pipeline);
        return features.Distinct().ToArray();
    }

    private static IReadOnlyList<ExecuteCommandUnsupportedShellFeature> GetSegmentUnsupportedFeatures(
        ExecuteCommandSubcommandPlan segment)
    {
        var features = new List<ExecuteCommandUnsupportedShellFeature>();
        if (segment.Risk.HasFlag(ExecuteCommandPermissionRisk.UnsafeRedirectionTarget))
            features.Add(ExecuteCommandUnsupportedShellFeature.UnsafeRedirectionTarget);
        if (segment.Risk.HasFlag(ExecuteCommandPermissionRisk.OutputRedirection))
            features.Add(ExecuteCommandUnsupportedShellFeature.OutputRedirection);
        if (segment.Risk.HasFlag(ExecuteCommandPermissionRisk.UnknownWrapper))
            features.Add(ExecuteCommandUnsupportedShellFeature.UnknownWrapper);
        if (segment.Risk.HasFlag(ExecuteCommandPermissionRisk.BareVariableExpansion))
            features.Add(ExecuteCommandUnsupportedShellFeature.BareVariableExpansion);
        if (segment.Risk.HasFlag(ExecuteCommandPermissionRisk.CommandSubstitution))
            features.Add(ExecuteCommandUnsupportedShellFeature.CommandSubstitution);
        if (segment.Risk.HasFlag(ExecuteCommandPermissionRisk.Heredoc))
            features.Add(ExecuteCommandUnsupportedShellFeature.Heredoc);
        if (segment.Risk.HasFlag(ExecuteCommandPermissionRisk.ShellInvocation))
            features.Add(ExecuteCommandUnsupportedShellFeature.ShellInvocation);
        if (segment.Risk.HasFlag(ExecuteCommandPermissionRisk.Subshell))
            features.Add(ExecuteCommandUnsupportedShellFeature.Subshell);
        return features.Distinct().ToArray();
    }

    private static bool ContainsUnicodeWhitespace(string command)
        => command.Any(ch => char.IsWhiteSpace(ch) && ch is not ' ' and not '\t' and not '\n');

    private static bool ContainsEscapedShellOperator(string command)
    {
        for (var i = 0; i + 1 < command.Length; i++)
        {
            if (command[i] == '\\' && command[i + 1] is ';' or '|' or '>' or '<')
                return true;
        }
        return false;
    }

    private static bool ContainsMidWordCommentMarker(string command)
    {
        for (var i = 1; i < command.Length; i++)
        {
            if (command[i] == '#' &&
                !char.IsWhiteSpace(command[i - 1]) &&
                command[i - 1] is not '\'' and not '"')
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsQuotedNewlineComment(string command)
        => command.Contains("\\\n#", StringComparison.Ordinal) ||
           command.Contains("'\n#", StringComparison.Ordinal) ||
           command.Contains("\"\n#", StringComparison.Ordinal);

    private static bool ContainsBraceExpansion(string command)
        => System.Text.RegularExpressions.Regex.IsMatch(command, @"(?<!\$)\{[^{}\s]*,[^{}]*\}");

    private static ExecuteCommandSubcommandPlan CreateSegment(
        ExecuteCommandShellSegmentParse segment,
        ExecuteCommandPermissionRisk risk,
        ExecuteCommandAnalysisTrustLevel trust,
        bool allowPrefix = true)
        => CreateSegmentCore(
            segment.Text,
            segment.Tokens.Select(token => token.Text).ToArray(),
            segment.Redirections,
            risk | segment.Risk,
            trust,
            allowPrefix);

    private static ExecuteCommandSubcommandPlan CreateSegment(
        string text,
        IReadOnlyList<string> argv,
        ExecuteCommandPermissionRisk risk,
        ExecuteCommandAnalysisTrustLevel trust,
        bool allowPrefix = true)
    {
        var redirectionResult = ExtractRedirections(argv);
        return CreateSegmentCore(
            text,
            redirectionResult.Argv,
            redirectionResult.Redirections,
            risk | redirectionResult.Risk,
            trust,
            allowPrefix);
    }

    private static ExecuteCommandSubcommandPlan CreateSegmentCore(
        string text,
        IReadOnlyList<string> argv,
        IReadOnlyList<ExecuteCommandRedirectionPlan> redirections,
        ExecuteCommandPermissionRisk risk,
        ExecuteCommandAnalysisTrustLevel trust,
        bool allowPrefix)
    {
        var (environment, commandArgv, envRisk) = SplitLeadingEnvironmentAssignments(argv);
        risk |= envRisk;
        if (envRisk != ExecuteCommandPermissionRisk.None && trust < ExecuteCommandAnalysisTrustLevel.ReviewOnly)
            trust = ExecuteCommandAnalysisTrustLevel.ReviewOnly;

        if (risk.HasFlag(ExecuteCommandPermissionRisk.UnsafeRedirectionTarget) &&
            trust < ExecuteCommandAnalysisTrustLevel.ReviewOnly)
        {
            trust = ExecuteCommandAnalysisTrustLevel.ReviewOnly;
        }

        var wrapperResult = NormalizeWrappers(commandArgv);
        commandArgv = wrapperResult.Argv;
        risk |= wrapperResult.Risk;
        if (wrapperResult.Risk.HasFlag(ExecuteCommandPermissionRisk.UnknownWrapper) &&
            trust < ExecuteCommandAnalysisTrustLevel.ReviewOnly)
        {
            trust = ExecuteCommandAnalysisTrustLevel.ReviewOnly;
        }

        var baseCommand = commandArgv.Count > 0 ? commandArgv[0] : string.Empty;
        risk |= Policy.ClassifyCommandRisk(commandArgv);
        if (baseCommand == "git" &&
            commandArgv.Count >= 3 &&
            commandArgv[1] == "diff" &&
            commandArgv.Contains("--no-index", StringComparer.Ordinal) &&
            trust < ExecuteCommandAnalysisTrustLevel.ReviewOnly)
        {
            trust = ExecuteCommandAnalysisTrustLevel.ReviewOnly;
        }

        var safePrefix = allowPrefix && envRisk == ExecuteCommandPermissionRisk.None
            ? Policy.GetSafePrefix(commandArgv)
            : null;
        var familyPolicy = Policy.GetCommandFamilyPolicy(commandArgv, safePrefix);
        return new ExecuteCommandSubcommandPlan
        {
            Text = text.Trim(),
            Argv = commandArgv,
            DefensiveArgv = StripAllLeadingEnvironmentAssignments(argv),
            EnvironmentAssignments = environment,
            Redirections = redirections,
            BaseCommand = baseCommand,
            SafePrefix = safePrefix,
            Risk = risk,
            TrustLevel = trust,
            NormalizedWrappers = wrapperResult.Wrappers,
            Readiness = Policy.ResolveReadiness(familyPolicy)
        };
    }

    private sealed record RedirectionExtractionResult(
        IReadOnlyList<ExecuteCommandRedirectionPlan> Redirections,
        IReadOnlyList<string> Argv,
        ExecuteCommandPermissionRisk Risk);

    private static RedirectionExtractionResult ExtractRedirections(IReadOnlyList<string> argv)
    {
        var redirections = new List<ExecuteCommandRedirectionPlan>();
        var commandArgv = new List<string>();
        var risk = ExecuteCommandPermissionRisk.None;

        for (var i = 0; i < argv.Count; i++)
        {
            var token = argv[i];
            var split = TrySplitAttachedRedirection(token);
            if (TryParseRedirectionOperator(split.Operator, out var kind, out var operation))
            {
                risk |= ExecuteCommandPermissionRisk.OutputRedirection;
                var target = split.Target;
                if (target is null)
                {
                    if (i + 1 >= argv.Count)
                    {
                        risk |= ExecuteCommandPermissionRisk.UnsafeRedirectionTarget;
                        continue;
                    }
                    target = argv[++i];
                }

                var safe = IsSafeStaticRedirectionTarget(target);
                if (!safe)
                    risk |= ExecuteCommandPermissionRisk.UnsafeRedirectionTarget;
                redirections.Add(new ExecuteCommandRedirectionPlan
                {
                    Kind = kind,
                    Target = target,
                    Operation = operation,
                    TargetStaticallyResolved = safe
                });
                continue;
            }

            commandArgv.Add(token);
        }

        return new RedirectionExtractionResult(redirections, commandArgv, risk);
    }

    private static (string Operator, string? Target) TrySplitAttachedRedirection(string token)
    {
        foreach (var op in new[] { "2>>", "2>", "&>", ">>", ">", "<" })
        {
            if (token.StartsWith(op, StringComparison.Ordinal))
                return (op, token.Length == op.Length ? null : token[op.Length..]);
        }
        return (token, null);
    }

    private static bool TryParseRedirectionOperator(
        string token,
        out ExecuteCommandRedirectionKind kind,
        out ExecuteCommandFilesystemOperation operation)
    {
        switch (token)
        {
            case "<":
                kind = ExecuteCommandRedirectionKind.Input;
                operation = ExecuteCommandFilesystemOperation.Read;
                return true;
            case ">":
                kind = ExecuteCommandRedirectionKind.Output;
                operation = ExecuteCommandFilesystemOperation.Write;
                return true;
            case ">>":
                kind = ExecuteCommandRedirectionKind.Append;
                operation = ExecuteCommandFilesystemOperation.Write;
                return true;
            case "2>":
                kind = ExecuteCommandRedirectionKind.ErrorOutput;
                operation = ExecuteCommandFilesystemOperation.Write;
                return true;
            case "2>>":
                kind = ExecuteCommandRedirectionKind.ErrorAppend;
                operation = ExecuteCommandFilesystemOperation.Write;
                return true;
            case "&>":
                kind = ExecuteCommandRedirectionKind.OutputAndError;
                operation = ExecuteCommandFilesystemOperation.Write;
                return true;
            default:
                kind = default;
                operation = default;
                return false;
        }
    }

    private static bool IsSafeStaticRedirectionTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;
        if (target.Contains('$', StringComparison.Ordinal) ||
            target.Contains('`', StringComparison.Ordinal) ||
            target.Contains('*', StringComparison.Ordinal) ||
            target.Contains('?', StringComparison.Ordinal) ||
            target.Contains('{', StringComparison.Ordinal) ||
            target.Contains('}', StringComparison.Ordinal) ||
            target.Contains('~', StringComparison.Ordinal) ||
            target.Contains('!', StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }

    private sealed record WrapperNormalizationResult(
        IReadOnlyList<string> Wrappers,
        IReadOnlyList<string> Argv,
        ExecuteCommandPermissionRisk Risk);

    private static WrapperNormalizationResult NormalizeWrappers(IReadOnlyList<string> argv)
    {
        var wrappers = new List<string>();
        var current = argv.ToArray();
        var risk = ExecuteCommandPermissionRisk.None;

        while (current.Length > 0)
        {
            var wrapper = current[0];
            var consumed = GetSafeWrapperTokenCount(current);
            if (consumed == 0)
                break;
            if (consumed < 0)
            {
                risk |= ExecuteCommandPermissionRisk.UnknownWrapper;
                break;
            }
            wrappers.Add(string.Join(' ', current.Take(consumed)));
            current = current.Skip(consumed).ToArray();
        }

        return new WrapperNormalizationResult(wrappers, current, risk);
    }

    private static int GetSafeWrapperTokenCount(IReadOnlyList<string> argv)
    {
        if (argv.Count == 0)
            return 0;
        return argv[0] switch
        {
            "time" => GetTimeWrapperTokenCount(argv),
            "nohup" => 1,
            "timeout" => GetTimeoutWrapperTokenCount(argv),
            "nice" => GetNiceWrapperTokenCount(argv),
            "stdbuf" => GetStdbufWrapperTokenCount(argv),
            "env" or "xargs" or "sudo" or "doas" or "pkexec" => -1,
            _ => 0
        };
    }

    private static int GetTimeoutWrapperTokenCount(IReadOnlyList<string> argv)
    {
        if (argv.Count < 3)
            return -1;
        var index = 1;
        while (index < argv.Count && argv[index].StartsWith("--", StringComparison.Ordinal))
        {
            if (!argv[index].StartsWith("--signal=", StringComparison.Ordinal) &&
                !argv[index].StartsWith("--kill-after=", StringComparison.Ordinal) &&
                argv[index] is not "--foreground" and not "--preserve-status")
            {
                return -1;
            }
            index++;
        }
        return index < argv.Count - 1 && IsDurationToken(argv[index]) ? index + 1 : -1;
    }

    private static int GetTimeWrapperTokenCount(IReadOnlyList<string> argv)
    {
        if (argv.Count < 2)
            return -1;
        if (argv.Count >= 3 && argv[1] == "-p")
            return 2;
        return argv[1].StartsWith("-", StringComparison.Ordinal) ? -1 : 1;
    }

    private static int GetNiceWrapperTokenCount(IReadOnlyList<string> argv)
    {
        if (argv.Count < 2)
            return -1;
        if (argv.Count >= 4 && argv[1] == "-n" && int.TryParse(argv[2], out _))
            return 3;
        if (argv.Count >= 3 && argv[1].StartsWith("-", StringComparison.Ordinal) && int.TryParse(argv[1], out _))
            return 2;
        return argv[1].StartsWith("-", StringComparison.Ordinal) ? -1 : 1;
    }

    private static int GetStdbufWrapperTokenCount(IReadOnlyList<string> argv)
    {
        if (argv.Count < 3)
            return -1;
        var index = 1;
        while (index < argv.Count && argv[index].StartsWith("-", StringComparison.Ordinal))
        {
            var token = argv[index];
            if (!System.Text.RegularExpressions.Regex.IsMatch(token, "^-[ioe](0|L|[1-9][0-9]*[KMG]?)$"))
                return -1;
            index++;
        }
        return index < argv.Count ? index : -1;
    }

    private static bool IsDurationToken(string token)
        => System.Text.RegularExpressions.Regex.IsMatch(token, "^[0-9]+(\\.[0-9]+)?[smhd]?$");

    private static IReadOnlyList<string> StripAllLeadingEnvironmentAssignments(IReadOnlyList<string> tokens)
    {
        var index = 0;
        while (index < tokens.Count && TryParseEnvironmentAssignment(tokens[index], out _, out _))
            index++;
        return tokens.Skip(index).ToArray();
    }

    private static (IReadOnlyDictionary<string, string?> Environment, IReadOnlyList<string> Argv, ExecuteCommandPermissionRisk Risk)
        SplitLeadingEnvironmentAssignments(IReadOnlyList<string> tokens)
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        var risk = ExecuteCommandPermissionRisk.None;
        var index = 0;
        while (index < tokens.Count && TryParseEnvironmentAssignment(tokens[index], out var name, out var value))
        {
            env[name] = value;
            if (!IsSafeEnvironmentVariable(name))
                risk |= ExecuteCommandPermissionRisk.ParserDifferentialRisk;
            index++;
        }

        return (env, tokens.Skip(index).ToArray(), risk);
    }

    private static bool IsSafeEnvironmentVariable(string name)
        => Policy.SafeEnvironmentVariables.Contains(name) &&
           !Policy.UnsafeEnvironmentVariables.Contains(name) &&
           !name.StartsWith("DYLD_", StringComparison.Ordinal);

    private static bool TryParseEnvironmentAssignment(string token, out string name, out string? value)
    {
        name = string.Empty;
        value = null;
        var equals = token.IndexOf('=');
        if (equals <= 0)
            return false;
        var candidate = token[..equals];
        if (!System.Text.RegularExpressions.Regex.IsMatch(candidate, "^[A-Za-z_][A-Za-z0-9_]*$"))
            return false;
        name = candidate;
        value = token[(equals + 1)..];
        return true;
    }

    private static IReadOnlyList<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                    continue;
                }
                current.Append(ch);
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }
}

internal static class ExecuteCommandPathAnalyzer
{
    private static readonly ExecuteCommandSemanticPolicy Policy = ExecuteCommandSemanticPolicy.Default;

    public static IReadOnlyList<ExecuteCommandFilesystemEffect> GetEffects(
        ExecuteCommandShellAnalysis analysis,
        string workingDirectory,
        string workspaceRoot,
        AgentSandboxRuntime sandbox)
    {
        var effects = new List<ExecuteCommandFilesystemEffect>();
        foreach (var segment in analysis.Segments)
        {
            foreach (var redirect in segment.Redirections)
            {
                if (!redirect.TargetStaticallyResolved)
                    continue;
                var path = ResolvePath(redirect.Target, workingDirectory);
                effects.Add(new ExecuteCommandFilesystemEffect
                {
                    Operation = redirect.Operation,
                    Path = path,
                    WithinWorkspace = IsWithinWorkspace(path, workspaceRoot),
                    CoveredBySandbox = IsCoveredBySandbox(path, redirect.Operation, workspaceRoot, workingDirectory, sandbox)
                });
            }

            foreach (var effect in GetCommandPathEffects(segment, workingDirectory, workspaceRoot, sandbox))
                effects.Add(effect);
        }
        return effects;
    }

    private static IEnumerable<ExecuteCommandFilesystemEffect> GetCommandPathEffects(
        ExecuteCommandSubcommandPlan segment,
        string workingDirectory,
        string workspaceRoot,
        AgentSandboxRuntime sandbox)
    {
        if (segment.Argv.Count < 2)
            yield break;

        foreach (var (operation, pathToken) in Policy.ExtractFilesystemEffects(segment.Argv))
        {
            var path = ResolvePath(pathToken, workingDirectory);
            yield return new ExecuteCommandFilesystemEffect
            {
                Operation = operation,
                Path = path,
                WithinWorkspace = IsWithinWorkspace(path, workspaceRoot),
                CoveredBySandbox = IsCoveredBySandbox(path, operation, workspaceRoot, workingDirectory, sandbox)
            };
        }
    }

    private static string ResolvePath(string path, string workingDirectory)
        => Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workingDirectory, path));

    private static bool IsWithinWorkspace(string path, string workspaceRoot)
        => AgentWorkspace.IsPathUnderDirectory(workspaceRoot, path);

    private static bool IsCoveredBySandbox(
        string path,
        ExecuteCommandFilesystemOperation operation,
        string workspaceRoot,
        string workingDirectory,
        AgentSandboxRuntime sandbox)
    {
        if (!sandbox.IsEnforced)
            return true;

        if (IsWithinWorkspace(path, workspaceRoot))
            return true;

        return sandbox.Filesystem.Any(grant =>
        {
            var grantPath = ResolvePath(grant.Path, workingDirectory);
            var operationCovered = grant.Access switch
            {
                AgentSandboxPathAccess.Read => operation == ExecuteCommandFilesystemOperation.Read,
                AgentSandboxPathAccess.Write => operation is ExecuteCommandFilesystemOperation.Create
                    or ExecuteCommandFilesystemOperation.Write
                    or ExecuteCommandFilesystemOperation.Delete,
                _ => false
            };
            return operationCovered && AgentWorkspace.IsPathUnderDirectory(grantPath, path);
        });
    }

    public static IReadOnlyList<ExecuteCommandNetworkEffect> GetNetworkEffects(
        ExecuteCommandShellAnalysis analysis,
        AgentSandboxRuntime sandbox)
        => analysis.Segments.Any(segment => Policy.HasNetworkEffect(segment.Argv))
            ? [new ExecuteCommandNetworkEffect
            {
                Operation = ExecuteCommandNetworkOperation.LikelyEgress,
                CoveredBySandbox = !sandbox.IsEnforced ||
                    sandbox.Network.Mode != NetworkEgressMode.Blocked
            }]
            : [];
}

internal static class AgentSandboxRuntimeExtensions
{
    public static string Canonicalize(this AgentSandboxRuntime policy, string workingDirectory)
    {
        var filesystem = policy.Filesystem
            .Select(grant =>
            {
                var path = Path.IsPathFullyQualified(grant.Path)
                    ? Path.GetFullPath(grant.Path)
                    : Path.GetFullPath(Path.Combine(workingDirectory, grant.Path));
                return $"{grant.Access}:{path}";
            })
            .Order(StringComparer.Ordinal);
        var allowed = policy.Network.AllowedDomains
            .Select(static rule => rule.Pattern)
            .Order(StringComparer.Ordinal);
        var denied = policy.Network.DeniedDomains
            .Select(static rule => rule.Pattern)
            .Order(StringComparer.Ordinal);
        return string.Join("|", [
            "v:1",
            $"mode:{policy.Security.Sandbox}",
            $"fs:{string.Join(",", filesystem)}",
            $"net:{policy.Network.Mode}:{string.Join(",", allowed)}:{string.Join(",", denied)}",
            $"pty:{policy.Interactive.AllowPty}",
            $"bind:{policy.Interactive.AllowLocalBinding}",
            $"mach:{string.Join(",", policy.Interactive.AllowedMachLookups.Order(StringComparer.Ordinal))}"
        ]);
    }
}
