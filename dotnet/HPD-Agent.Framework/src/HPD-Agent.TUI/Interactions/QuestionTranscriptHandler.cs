using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Interactions;

/// <summary>Readable, replayable question history independent of the live dialog.</summary>
public sealed class QuestionTranscriptHandler(Action<AgentEvent>? settle = null) : IAgentTuiEventHandler
{
    public bool CanHandle(AgentEvent evt) => evt is UserQuestionRequestEvent or ParentQuestionRequestEvent
        or QuestionResponseEvent or AgentRequestTerminatedEvent;

    public ValueTask HandleAsync(AgentEvent evt, AgentTuiEventContext context, CancellationToken cancellationToken)
    {
        settle?.Invoke(evt);
        var requests = context.State.GetOrCreate("hpd.question-history", () => new Dictionary<string, AgentQuestion[]>());
        static string Key(AgentEvent evt, string requestId) => $"{evt.SessionId}/{evt.ThreadId}/{evt.ThreadExecutionId}/{requestId}";
        string title;
        string body;
        switch (evt)
        {
            case UserQuestionRequestEvent request:
                requests[Key(evt, request.RequestId)] = request.Questions;
                title = "Question for you";
                body = FormatQuestions(request.Questions);
                break;
            case ParentQuestionRequestEvent request:
                requests[Key(evt, request.RequestId)] = request.Questions;
                title = "Question for parent";
                body = FormatQuestions(request.Questions);
                break;
            case QuestionResponseEvent response:
                requests.TryGetValue(Key(evt, response.RequestId), out var questions);
                title = response.Outcome switch
                {
                    QuestionOutcome.Answered => "Questions answered",
                    QuestionOutcome.Discuss => "Questions · discuss in chat",
                    _ => "Questions dismissed"
                };
                body = string.Join("\n", response.Answers.Select(answer =>
                {
                    var question = questions?.FirstOrDefault(q => q.Id == answer.QuestionId);
                    var parts = answer.SelectedOptionIds.Select(id => question?.Options?.FirstOrDefault(o => o.Id == id)?.Label ?? id).ToList();
                    if (!string.IsNullOrWhiteSpace(answer.CustomText)) parts.Add(answer.CustomText);
                    if (!string.IsNullOrWhiteSpace(answer.Notes)) parts.Add("Notes: " + answer.Notes);
                    return $"{question?.Header ?? question?.Text ?? answer.QuestionId}: {string.Join(" · ", parts)}";
                }));
                break;
            case AgentRequestTerminatedEvent terminal when requests.ContainsKey(Key(evt, terminal.RequestId)):
                title = "Questions · " + terminal.TerminalKind.ToString().ToLowerInvariant();
                body = terminal.Reason ?? "No answer was submitted.";
                break;
            default: return ValueTask.CompletedTask;
        }
        if (evt.ThreadId != context.Scope.ThreadId) title += " · " + evt.ThreadId;
        context.Shell.Transcript.AddFinal(TranscriptEntry.FromEvent(evt,
            new NoticeCell(title, string.IsNullOrEmpty(body) ? null : new Text(body), TranscriptSeverity.Info)));
        return ValueTask.CompletedTask;
    }

    private static string FormatQuestions(AgentQuestion[] questions) => string.Join("\n", questions.Select(q =>
        q.Text + (q.Options is null ? "" : "\n" + string.Join("\n", q.Options.Select(o => "  • " + o.Label +
            (q.RecommendedOptionId == o.Id ? " (Recommended)" : "") +
            (string.IsNullOrWhiteSpace(o.Description) ? "" : " — " + o.Description))))));
}
