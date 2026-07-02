using HPD.Agent;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.TUI.Interactions;

public sealed class PermissionRequestInteractionHandler :
    AgentTuiInteractionHandler<PermissionRequestEvent>
{
    protected override async Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext<PermissionRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var options = new[]
        {
            new PermissionDialogChoice("Allow once", PermissionDialogChoiceKind.Response, true, PermissionChoice.Ask, null),
            new PermissionDialogChoice("Always allow", PermissionDialogChoiceKind.Response, true, PermissionChoice.AlwaysAllow, null),
            new PermissionDialogChoice("Deny once", PermissionDialogChoiceKind.Response, false, PermissionChoice.Ask, "Denied by the TUI."),
            new PermissionDialogChoice("Tell agent what to do instead", PermissionDialogChoiceKind.Feedback, false, PermissionChoice.Ask, null)
        };
        var selected = await context.Dialogs.SelectAsync(
            BuildTitle(request),
            options,
            choice => choice.Title,
            cancellationToken).ConfigureAwait(false);

        if (!selected.IsSubmitted || selected.Value is null)
        {
            return AgentTuiInteractionResult.AnswerRequest(new PermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                Approved: false,
                Reason: "Permission dialog was canceled.",
                Choice: PermissionChoice.Ask));
        }

        var choice = selected.Value;
        if (choice.Kind == PermissionDialogChoiceKind.Feedback)
        {
            var feedback = await context.Dialogs.InputAsync(
                "Tell agent what to do instead",
                allowEmpty: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return AgentTuiInteractionResult.AnswerRequest(new PermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                Approved: false,
                Reason: !feedback.IsSubmitted || string.IsNullOrWhiteSpace(feedback.Value)
                    ? "Permission dialog was canceled."
                    : feedback.Value,
                Choice: PermissionChoice.Ask,
                DeniedBehavior: PermissionDeniedBehavior.ReturnToModel));
        }

        return AgentTuiInteractionResult.AnswerRequest(new PermissionResponseEvent(
            request.PermissionId,
            request.SourceName,
            choice.Approved,
            choice.Reason,
            choice.Choice));
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
        PermissionDialogChoiceKind Kind,
        bool Approved,
        PermissionChoice Choice,
        string? Reason);

    private enum PermissionDialogChoiceKind
    {
        Response,
        Feedback
    }
}

public sealed class ContinuationRequestInteractionHandler :
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

public sealed class ClarificationRequestInteractionHandler :
    AgentTuiInteractionHandler<ClarificationRequestEvent>
{
    protected override async Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext<ClarificationRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        AgentTuiDialogResult<string> answer;
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

        if (!answer.IsSubmitted || answer.Value is null)
        {
            return AgentTuiInteractionResult.Dismiss;
        }

        return AgentTuiInteractionResult.AnswerRequest(new ClarificationResponseEvent(
            request.RequestId,
            request.SourceName,
            request.Question,
            answer.Value));
    }
}
