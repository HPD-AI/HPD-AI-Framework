using HPD.Agent;
using HPD.Agent.Permissions;
using HPD.Agent.TUI.Composition;
using System.Text.Json;

namespace HPD.Agent.TUI.Interactions;

internal sealed class PermissionRequestInteractionHandler :
    AgentTuiInteractionHandler<PermissionRequestEvent>
{
    private static readonly TimeSpan TurnGrantLifetime = TimeSpan.FromHours(24);
    private const int MaximumTurnGrants = 64;
    private readonly TimeProvider _timeProvider;
    private readonly object _grantGate = new();
    private readonly Dictionary<PermissionTurnGrantKey, DateTimeOffset> _turnGrants = [];

    public PermissionRequestInteractionHandler(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext<PermissionRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (IsSandboxRequest(request) && TryGetTurnGrant(context, request, out var grantChoice))
        {
            return AgentTuiInteractionResult.AnswerRequest(new PermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                ChoiceId: grantChoice.Id));
        }

        var previousStatus = context.Shell.PromptStatusText;
        context.Shell.PromptStatusText =
            $"state: awaiting permission | {request.FunctionName ?? request.Authority ?? "decision"}";
        try
        {
            var selected = await context.Dialogs.SelectAsync(
                BuildTitle(request),
                request.Choices,
                choice => choice.Label,
                cancellationToken).ConfigureAwait(false);

            if (!selected.IsSubmitted || selected.Value is null)
            {
                return AgentTuiInteractionResult.AnswerRequest(new PermissionResponseEvent(
                    request.PermissionId,
                    request.SourceName,
                    ChoiceId: request.Choices.First(choice => choice.Kind == PermissionDecisionKind.Deny).Id,
                    Feedback: "Permission dialog was canceled."));
            }

            var choice = selected.Value;
            if (IsSandboxRequest(request) && choice.Id == "allow_for_turn")
                RememberTurnGrant(context, request);
            if (choice.Kind == PermissionDecisionKind.Feedback)
            {
                var feedback = await context.Dialogs.InputAsync(
                    "Tell agent what to do instead",
                    allowEmpty: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return AgentTuiInteractionResult.AnswerRequest(new PermissionResponseEvent(
                    request.PermissionId,
                    request.SourceName,
                    ChoiceId: choice.Id,
                    Feedback: !feedback.IsSubmitted || string.IsNullOrWhiteSpace(feedback.Value)
                        ? "Permission dialog was canceled."
                        : feedback.Value));
            }

            return AgentTuiInteractionResult.AnswerRequest(new PermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                choice.Id));
        }
        finally
        {
            context.Shell.PromptStatusText = previousStatus;
        }
    }

    private static string BuildTitle(PermissionRequestEvent request)
    {
        var lines = new List<string>
        {
            request.Reason ?? "Approve this operation?",
            string.IsNullOrWhiteSpace(request.Action)
                ? $"Function: {request.FunctionName ?? request.Authority ?? "unknown"}"
                : $"Function: {request.FunctionName ?? request.Authority ?? "unknown"} | Action: {request.Action}"
        };

        if (TryGetCommand(request.Arguments, out var command))
        {
            lines.Add("Command:");
            lines.Add(Truncate(command, 1_000));
        }
        else if (request.PermissionResource is { } resource)
        {
            lines.Add($"Target: {resource.DisplayName}");
        }
        else if (request.Arguments is { } arguments &&
                 arguments.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            lines.Add($"Arguments: {Truncate(arguments.GetRawText(), 500)}");
        }

        if (request.Risk is PermissionRisk.High or PermissionRisk.Critical)
            lines.Add($"Risk: {request.Risk}");
        if (request.PersistenceUnavailableReason is { } unavailable)
            lines.Add($"Reusable approval unavailable: {unavailable}.");
        return string.Join('\n', lines);
    }

    private static bool TryGetCommand(JsonElement? arguments, out string command)
    {
        command = "";
        if (arguments is not { ValueKind: JsonValueKind.Object } args)
            return false;
        if (args.TryGetProperty("command", out var direct) &&
            direct.ValueKind == JsonValueKind.String)
        {
            command = direct.GetString() ?? "";
            return command.Length > 0;
        }
        if (args.TryGetProperty("request", out var request) &&
            request.ValueKind == JsonValueKind.Object &&
            request.TryGetProperty("command", out var nested) &&
            nested.ValueKind == JsonValueKind.String)
        {
            command = nested.GetString() ?? "";
            return command.Length > 0;
        }
        return false;
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..Math.Max(0, maximumLength - 3)] + "...";

    private static bool IsSandboxRequest(PermissionRequestEvent request) =>
        string.Equals(request.PermissionDomain, PermissionDomains.SandboxCapability,
            StringComparison.Ordinal);

    private bool TryGetTurnGrant(
        AgentTuiInteractionContext<PermissionRequestEvent> context,
        PermissionRequestEvent request,
        out PermissionChoice choice)
    {
        choice = null!;
        if (TryCreateTurnKey(context, request, out var key) is false)
            return false;
        lock (_grantGate)
        {
            PruneExpiredGrants();
            if (!_turnGrants.TryGetValue(key, out var expiresAt) ||
                expiresAt <= _timeProvider.GetUtcNow())
                return false;
            choice = request.Choices.FirstOrDefault(candidate => candidate.Id == "allow_for_turn")!;
            return choice is not null;
        }
    }

    private void RememberTurnGrant(
        AgentTuiInteractionContext<PermissionRequestEvent> context,
        PermissionRequestEvent request)
    {
        if (TryCreateTurnKey(context, request, out var key) is false)
            return;
        lock (_grantGate)
        {
            PruneExpiredGrants();
            _turnGrants[key] = _timeProvider.GetUtcNow() + TurnGrantLifetime;
            if (_turnGrants.Count <= MaximumTurnGrants)
                return;
            var oldest = _turnGrants.MinBy(static grant => grant.Value).Key;
            _turnGrants.Remove(oldest);
        }
    }

    private void PruneExpiredGrants()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var key in _turnGrants
                     .Where(grant => grant.Value <= now)
                     .Select(static grant => grant.Key)
                     .ToArray())
            _turnGrants.Remove(key);
    }

    private static bool TryCreateTurnKey(
        AgentTuiInteractionContext<PermissionRequestEvent> context,
        PermissionRequestEvent request,
        out PermissionTurnGrantKey key)
    {
        if (string.IsNullOrWhiteSpace(request.ThreadExecutionId))
        {
            key = default;
            return false;
        }
        key = new(
            context.Scope.AgentId,
            context.Scope.SessionId,
            context.Scope.ThreadId,
            request.ThreadExecutionId,
            request.PermissionDomain,
            GrantFamily(request.Authority));
        return true;
    }

    private static string GrantFamily(string? authority)
    {
        if (authority?.StartsWith("sandbox/filesystem-", StringComparison.Ordinal) == true)
            return "filesystem";
        return authority ?? "unknown";
    }

    private readonly record struct PermissionTurnGrantKey(
        string AgentId,
        string SessionId,
        string ThreadId,
        string ThreadExecutionId,
        string PermissionDomain,
        string GrantFamily);
}

internal sealed class ContinuationRequestInteractionHandler :
    AgentTuiInteractionHandler<ContinuationRequestEvent>
{
    protected override async Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext<ContinuationRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var options = new[]
        {
            new ContinuationDialogChoice("Continue 10 more iterations", true, 10),
            new ContinuationDialogChoice("Continue 25 more iterations", true, 25),
            new ContinuationDialogChoice("Stop", false, 0)
        };
        var selected = await context.Dialogs.SelectAsync(
            $"Continue past iteration {request.CurrentIteration}/{request.MaxIterations}?",
            options,
            choice => choice.Title,
            cancellationToken).ConfigureAwait(false);

        if (!selected.IsSubmitted || selected.Value is null)
        {
            return AgentTuiInteractionResult.AnswerRequest(new ContinuationResponseEvent(
                request.ContinuationId,
                request.SourceName,
                Approved: false,
                ExtensionAmount: 0));
        }

        var choice = selected.Value;
        return AgentTuiInteractionResult.AnswerRequest(new ContinuationResponseEvent(
            request.ContinuationId,
            request.SourceName,
            choice.Approved,
            choice.ExtensionAmount));
    }

    private sealed record ContinuationDialogChoice(
        string Title,
        bool Approved,
        int ExtensionAmount);
}
