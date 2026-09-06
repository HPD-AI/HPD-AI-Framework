using HPD.Agent.TUI.Models;
using HPD.TUI.Core;
using HPD.TUI.Markdown;

namespace HPD.Agent.TUI.Markdown;

/// <summary>Creates finalized source-backed transcript cells for non-streaming projections and tests.</summary>
public static class MarkdownMessageFactory
{
    /// <summary>Creates a finalized assistant message cell.</summary>
    /// <param name="messageId">The stable message identifier.</param>
    /// <param name="source">Canonical Markdown source.</param>
    /// <param name="outerWidth">Available width before reasoning indentation.</param>
    /// <param name="theme">The resolved Markdown palette used by the consuming renderer.</param>
    /// <param name="name">Optional display author.</param>
    /// <param name="colorSystem">Terminal color encoding.</param>
    /// <returns>A final cell with a prepared source-backed projection.</returns>
    public static AssistantMessageCell CreateAssistant(string messageId, string source, int outerWidth, MarkdownTheme theme,
        string? name = null, ColorSystem colorSystem = ColorSystem.TrueColor)
    {
        var session = CreateSession(MarkdownStreamKind.Assistant, messageId, name);
        session.Append(source);
        var update = session.Complete();
        session.Projection.Prepare(update.Document,
            new(Math.Max(1, outerWidth), theme, colorSystem), new MarkdownLayoutEngine());
        return new(name, update.Document, session.Projection);
    }

    /// <summary>Creates a finalized reasoning message cell.</summary>
    /// <param name="messageId">The stable message identifier.</param>
    /// <param name="source">Canonical Markdown source.</param>
    /// <param name="outerWidth">Available width before reasoning indentation.</param>
    /// <param name="theme">The resolved Markdown palette used by the consuming renderer.</param>
    /// <param name="colorSystem">Terminal color encoding.</param>
    /// <returns>A final cell with a prepared source-backed projection.</returns>
    public static ReasoningMessageCell CreateReasoning(string messageId, string source, int outerWidth, MarkdownTheme theme,
        ColorSystem colorSystem = ColorSystem.TrueColor)
    {
        var session = CreateSession(MarkdownStreamKind.Reasoning, messageId, null);
        session.Append(source);
        var update = session.Complete();
        session.Projection.Prepare(update.Document,
            new(Math.Max(1, outerWidth - 2), theme, colorSystem), new MarkdownLayoutEngine());
        return new(update.Document, session.Projection);
    }

    private static MarkdownStreamSession CreateSession(MarkdownStreamKind kind, string messageId, string? author) =>
        new(new(kind, messageId), new MarkdownMessagePresentation(AuthorName: author));
}
