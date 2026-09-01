using HPD.Agent;
using HPD.Agent.Permissions;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.TUI.Interactions;

public sealed class PermissionRequestInteractionHandler :
    AgentTuiInteractionHandler<PermissionRequestEvent>
{
    private readonly PermissionPresentationRendererRegistry _renderers;

    /// <summary>Creates the default handler without optional typed renderers.</summary>
    public PermissionRequestInteractionHandler()
        : this(new PermissionPresentationRendererRegistry())
    {
    }

    internal PermissionRequestInteractionHandler(PermissionPresentationRendererRegistry renderers) =>
        _renderers = renderers;

    protected override async Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext<PermissionRequestEvent> context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (request.Evaluation.Presentation is { } presentation &&
            _renderers.TryGet(presentation.PresentationId, out var renderer))
        {
            var decision = await renderer.RenderAsync(
                presentation.Payload,
                request.Evaluation.Choices,
                cancellationToken).ConfigureAwait(false);
            var legalChoice = request.Evaluation.Choices.Items.FirstOrDefault(choice =>
                string.Equals(choice.Id, decision.ChoiceId, StringComparison.Ordinal));
            if (legalChoice is null || legalChoice.Decision != decision.Kind)
                throw new InvalidOperationException(
                    "Permission presentation renderer returned a decision outside the server-owned choice set.");
            return AgentTuiInteractionResult.AnswerRequest(new PermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                decision.ChoiceId,
                decision.Feedback ?? decision.Reason));
        }

        var options = request.Evaluation.Choices.Items;
        var selected = await context.Dialogs.SelectAsync(
            BuildTitle(request),
            options,
            choice => choice.Label,
            cancellationToken).ConfigureAwait(false);

        if (!selected.IsSubmitted || selected.Value is null)
        {
            return AgentTuiInteractionResult.AnswerRequest(new PermissionResponseEvent(
                request.PermissionId,
                request.SourceName,
                ChoiceId: "deny_once",
                Feedback: "Permission dialog was canceled."));
        }

        var choice = selected.Value;
        if (choice.Decision == PermissionDecisionKind.Feedback)
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

    private static string BuildTitle(PermissionRequestEvent request)
    {
        var description = string.IsNullOrWhiteSpace(request.Evaluation.Summary)
            ? ""
            : $"\n{request.Evaluation.Summary}";
        return $"{request.Evaluation.Title}{description}";
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
