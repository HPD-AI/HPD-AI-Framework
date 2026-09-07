using System.Collections.Concurrent;
using HPD.Agent.TUI.Composition;
using HPD.TUI.Forms;
using HPD.TUI.Flows;

namespace HPD.Agent.TUI.Interactions;

/// <summary>Queued, paged questions using the shared form/dialog infrastructure.</summary>
public sealed class UserQuestionInteractionHandler : AgentTuiInteractionHandler<UserQuestionRequestEvent>
{
    private readonly ConcurrentDictionary<string, Draft> _drafts = new(StringComparer.Ordinal);
    private static string Key(AgentEvent request, string id) => $"{request.SessionId}/{request.ThreadId}/{request.ThreadExecutionId}/{id}";

    internal void Settle(AgentEvent evt)
    {
        var id = evt switch { QuestionResponseEvent response => response.RequestId,
            AgentRequestTerminatedEvent terminal => terminal.RequestId, _ => null };
        if (id is not null) _drafts.TryRemove(Key(evt, id), out _);
    }

    protected override async Task<AgentTuiInteractionResult> HandleAsync(
        AgentTuiInteractionContext<UserQuestionRequestEvent> context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        QuestionValidation.Validate(request.Questions);
        var draft = _drafts.GetOrAdd(Key(request, request.RequestId), _ => new Draft(request));
        while (true)
        {
            var page = draft.Pages[draft.Index];
            var title = $"Question {draft.Index + 1}/{draft.Pages.Length} · {context.Scope.AgentId} · {context.Scope.ThreadId}";
            var result = await context.Dialogs.FormAsync(title,
                new FormDefinition<QuestionResponseEvent>(page.Model, () => draft.Build(request)), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.IsSubmitted && page.Defer.Value)
            {
                page.Defer.Select(false);
                return AgentTuiInteractionResult.Defer;
            }
            var response = result.IsSubmitted && result.Value is not null ? result.Value :
                new QuestionResponseEvent(request.RequestId, request.SourceName, QuestionOutcome.Dismissed, []);
            if (response.Outcome == QuestionOutcome.Answered)
            {
                if (page.Back.Value && draft.Index > 0) { page.Back.Select(false); draft.Index--; continue; }
                if (draft.Index + 1 < draft.Pages.Length) { draft.Index++; continue; }
            }
            QuestionValidation.ValidateResponse(request.Questions, response);
            // Keep drafts until the accepted response/terminal is projected, including a rejected reply.
            return AgentTuiInteractionResult.AnswerRequest(response with
            { SessionId = request.SessionId, ThreadId = request.ThreadId, ThreadExecutionId = request.ThreadExecutionId });
        }
    }

    private sealed class Draft
    {
        internal int Index;
        internal Page[] Pages;
        internal Draft(UserQuestionRequestEvent request) => Pages = request.Questions.Select((question, index) =>
            new Page(question, index, request.Questions.Length)).ToArray();
        internal QuestionResponseEvent Build(UserQuestionRequestEvent request)
        {
            var outcome = Pages[Index].Outcome.Value;
            return new(request.RequestId, request.SourceName, outcome,
                outcome == QuestionOutcome.Answered ? Pages.Select(p => p.Answer()).ToArray() : []);
        }
    }

    private sealed class Page
    {
        internal readonly FormModel Model = new();
        internal readonly ChoiceFormField<QuestionOutcome> Outcome;
        internal readonly ChoiceFormField<bool> Back;
        internal readonly ChoiceFormField<bool> Defer = FormFields.Boolean("defer", "Answer later (/questions to reopen)", false);
        internal readonly Func<QuestionAnswer> Answer;
        internal Page(AgentQuestion question, int index, int count)
        {
            Outcome = new("outcome", "Response",
                [new("answer", QuestionOutcome.Answered, "Answer"),
                 new("discuss", QuestionOutcome.Discuss, "Discuss in chat"),
                 new("dismiss", QuestionOutcome.Dismissed, "Dismiss without answering")], QuestionOutcome.Answered);
            Back = new("navigation", "Continue",
                index == 0 ? [new("forward", false, count == 1 ? "Submit answers" : "Next question")]
                    : [new("forward", false, index + 1 == count ? "Submit answers" : "Next question"), new("back", true, "Back to previous question")], false);
            Model.Add(Outcome);
            var selections = new List<(string Id, ChoiceFormField<bool> Field)>();
            ChoiceFormField<string>? choice = null;
            if (question.Options is { } options)
            {
                string Label(QuestionOption option) => option.Label + (option.Id == question.RecommendedOptionId ? " (Recommended)" : "");
                if (question.Multiple)
                    foreach (var option in options)
                    {
                        var field = FormFields.Boolean($"{question.Id}:option:{option.Id}", Label(option), false, option.Description);
                        Model.Add(field); selections.Add((option.Id, field));
                    }
                else
                {
                    choice = new ChoiceFormField<string>($"{question.Id}:choice", question.Header ?? question.Text,
                        new[] { new FormChoice<string>("none", "", "Choose an option or enter your own answer") }
                            .Concat(options.Select((option, i) => new FormChoice<string>($"option:{i}", option.Id, Label(option), option.Description))).ToArray(),
                        "", question.Text);
                    Model.Add(choice);
                }
            }
            var custom = new TextFormField($"{question.Id}:custom", question.Text,
                description: "Your answer or additional text", isMultiline: true,
                validator: text => Defer.Value || Back.Value || Outcome.Value != QuestionOutcome.Answered || !string.IsNullOrWhiteSpace(text) ||
                    !string.IsNullOrEmpty(choice?.Value) || selections.Any(s => s.Field.Value)
                    ? PromptValidationResult.Valid : PromptValidationResult.Invalid("Choose an option or enter an answer."));
            var notes = new TextFormField($"{question.Id}:notes", "Optional notes", isMultiline: true);
            Model.Add(custom); Model.Add(notes);
            if (count > 1) Model.Add(Back);
            Model.Add(Defer);
            Answer = () => new(question.Id,
                choice is not null ? (choice.Value.Length == 0 ? [] : [choice.Value]) : selections.Where(s => s.Field.Value).Select(s => s.Id).ToArray(),
                custom.Value, notes.Value);
        }
    }
}
