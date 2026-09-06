using System.ComponentModel;
using HPD.Events;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;

namespace HPD.Agent;

public sealed record QuestionOption(string Id, string Label, string? Description = null);
public sealed record AgentQuestion(string Id, string Text, string? Header = null,
    QuestionOption[]? Options = null, bool Multiple = false, string? RecommendedOptionId = null);
public sealed record QuestionAnswer(string QuestionId, string[] SelectedOptionIds,
    string? CustomText = null, string? Notes = null);
[JsonConverter(typeof(JsonStringEnumConverter<QuestionOutcome>))]
public enum QuestionOutcome { Answered, Dismissed, Discuss }

[DurableEvent]
public sealed record UserQuestionRequestEvent(string RequestId, string SourceName, AgentQuestion[] Questions)
    : AgentEvent, IAgentRequestEvent<QuestionResponseEvent>
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
}

[DurableEvent]
public sealed record QuestionResponseEvent(string RequestId, string SourceName,
    QuestionOutcome Outcome, QuestionAnswer[] Answers) : AgentEvent, IAgentResponseEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Control;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;
}

public static class QuestionValidation
{
    public static void Validate(AgentQuestion[] questions)
    {
        if (questions is null || questions.Length is < 1 or > 3)
            throw new ArgumentException("A request requires one to three questions.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in questions)
        {
            if (question is null || string.IsNullOrWhiteSpace(question.Id) || !ids.Add(question.Id) ||
                string.IsNullOrWhiteSpace(question.Text)) throw new ArgumentException("Question IDs must be unique and text nonblank.");
            if (question.Options is null)
            {
                if (question.Multiple || question.RecommendedOptionId is not null)
                    throw new ArgumentException("Free-text questions cannot have choice metadata.");
                continue;
            }
            if (question.Options.Length is < 1 or > 8) throw new ArgumentException("Provide one to eight options.");
            var options = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in question.Options)
                if (option is null || string.IsNullOrWhiteSpace(option.Id) || !options.Add(option.Id) || string.IsNullOrWhiteSpace(option.Label))
                    throw new ArgumentException("Option IDs must be unique and labels nonblank.");
            if (question.RecommendedOptionId is not null && !options.Contains(question.RecommendedOptionId))
                throw new ArgumentException("Recommendation must identify an existing option.");
        }
    }

    public static void ValidateResponse(AgentQuestion[] questions, QuestionResponseEvent response)
    {
        Validate(questions);
        if (!Enum.IsDefined(response.Outcome) || response.Answers is null) throw new ArgumentException("Invalid question response.");
        if (response.Outcome != QuestionOutcome.Answered)
        {
            if (response.Answers.Length != 0) throw new ArgumentException("Non-answer outcomes cannot contain answers.");
            return;
        }
        if (response.Answers.Length != questions.Length) throw new ArgumentException("Answer every question before submitting.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var answer in response.Answers)
        {
            if (answer is null || !ids.Add(answer.QuestionId) || answer.SelectedOptionIds is null)
                throw new ArgumentException("Invalid or duplicate answer.");
            var question = questions.SingleOrDefault(q => q.Id == answer.QuestionId)
                ?? throw new ArgumentException("Unknown question ID.");
            var selected = answer.SelectedOptionIds;
            if (selected.Distinct(StringComparer.Ordinal).Count() != selected.Length ||
                selected.Any(id => question.Options?.Any(option => option.Id == id) != true) ||
                (!question.Multiple && selected.Length > 1)) throw new ArgumentException("Invalid option selection.");
            if (selected.Length == 0 && string.IsNullOrWhiteSpace(answer.CustomText))
                throw new ArgumentException("An answer requires a selection or custom text.");
        }
    }
}

/// <summary>Waiting human questions using the standard Agent request coordinator.</summary>
public sealed class QuestionToolHarness
{
    [AIFunction(Name = "AskUser", InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly)]
    [Description("Ask the user one to three questions and wait for their answer. Use stable question and option IDs. Omit options for free text. The UI provides custom text and notes. Recommendations are suggestions, never automatic answers.")]
    public async Task<string> AskUserAsync(AgentQuestion[] questions, FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.RunConfig.AllowUserQuestions) throw new InvalidOperationException("user_questions_unavailable");
        QuestionValidation.Validate(questions);
        var response = await context.RequestAsync<UserQuestionRequestEvent, QuestionResponseEvent>(
            new(Guid.NewGuid().ToString("N"), "AskUser", questions), cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(response, QuestionJsonContext.Default.QuestionResponseEvent);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(QuestionResponseEvent))]
[JsonSerializable(typeof(QuestionAnswer[]))]
[JsonSerializable(typeof(AgentQuestion[]))]
internal partial class QuestionJsonContext : JsonSerializerContext;
