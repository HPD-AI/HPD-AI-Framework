using HPD.Agent;

namespace HPD.Agent.TUI.Interactions;

public sealed class PermissionRequestInteractionHandler :
    AgentTuiInteractionHandler<PermissionRequestEvent>
{
    protected override async Task<AgentEvent?> HandleAsync(
        AgentTuiInteractionContext<PermissionRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var options = new[]
        {
            new PermissionDialogChoice("Allow once", true, PermissionChoice.Ask, null),
            new PermissionDialogChoice("Always allow", true, PermissionChoice.AlwaysAllow, null),
            new PermissionDialogChoice("Deny once", false, PermissionChoice.Ask, "Denied by the TUI."),
            new PermissionDialogChoice("Always deny", false, PermissionChoice.AlwaysDeny, "Denied by the TUI.")
        };
        var selected = await context.Dialogs.SelectAsync(
            BuildTitle(request),
            options,
            choice => choice.Title,
            cancellationToken).ConfigureAwait(false);

        if (selected is null)
        {
            return new PermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                Approved: false,
                Reason: "Permission dialog was canceled.",
                Choice: PermissionChoice.Ask);
        }

        return new PermissionResponseEvent(
            request.PermissionId,
            request.SourceName,
            selected.Approved,
            selected.Reason,
            selected.Choice);
    }

    private static string BuildTitle(PermissionRequestEvent request)
    {
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? ""
            : $"\n{request.Description}";
        return $"Allow {request.FunctionName}?{description}";
    }

    private sealed record PermissionDialogChoice(
        string Title,
        bool Approved,
        PermissionChoice Choice,
        string? Reason);
}

public sealed class ContinuationRequestInteractionHandler :
    AgentTuiInteractionHandler<ContinuationRequestEvent>
{
    protected override async Task<AgentEvent?> HandleAsync(
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

        if (selected is null)
        {
            return new ContinuationResponseEvent(
                request.ContinuationId,
                request.SourceName,
                Approved: false,
                ExtensionAmount: 0);
        }

        return new ContinuationResponseEvent(
            request.ContinuationId,
            request.SourceName,
            selected.Approved,
            selected.ExtensionAmount);
    }

    private sealed record ContinuationDialogChoice(
        string Title,
        bool Approved,
        int ExtensionAmount);
}

public sealed class ClarificationRequestInteractionHandler :
    AgentTuiInteractionHandler<ClarificationRequestEvent>
{
    protected override async Task<AgentEvent?> HandleAsync(
        AgentTuiInteractionContext<ClarificationRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        string? answer;
        if (request.Options is { Length: > 0 } options)
        {
            answer = await context.Dialogs.SelectAsync(
                request.Question,
                options,
                option => option,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            answer = await context.Dialogs.InputAsync(
                request.Question,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (answer is null)
        {
            return null;
        }

        return new ClarificationResponseEvent(
            request.RequestId,
            request.SourceName,
            request.Question,
            answer);
    }
}
