using HPD.Agent.TUI.Models;

namespace HPD.Agent.TUI.Markdown;

/// <summary>Creates finalized source-backed transcript cells for non-streaming projections and tests.</summary>
public static class MarkdownMessageFactory
{
    /// <summary>Creates a finalized assistant message cell.</summary>
    public static AssistantMessageCell CreateAssistant(string messageId, string source, string? name = null)
    {
        var session = CreateSession(MarkdownStreamKind.Assistant, messageId, name);
        session.Append(source);
        var update = session.Complete();
        return new(name, update.Document, session.Projection);
    }

    /// <summary>Creates a finalized reasoning message cell.</summary>
    public static ReasoningMessageCell CreateReasoning(string messageId, string source)
    {
        var session = CreateSession(MarkdownStreamKind.Reasoning, messageId, null);
        session.Append(source);
        var update = session.Complete();
        return new(update.Document, session.Projection);
    }

    private static MarkdownStreamSession CreateSession(MarkdownStreamKind kind, string messageId, string? author) =>
        new(new(kind, messageId), new MarkdownMessagePresentation(AuthorName: author));
}
