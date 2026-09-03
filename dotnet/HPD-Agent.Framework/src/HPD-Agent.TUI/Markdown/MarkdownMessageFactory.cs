using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Composition;
using HPD.TUI.Core;
using HPD.TUI.Markdown;

namespace HPD.Agent.TUI.Markdown;

/// <summary>Creates finalized source-backed transcript cells for non-streaming projections and tests.</summary>
public static class MarkdownMessageFactory
{
    /// <summary>Creates a finalized assistant message cell.</summary>
    public static AssistantMessageCell CreateAssistant(string messageId, string source, int outerWidth, Theme theme,
        string? name = null, ColorSystem colorSystem = ColorSystem.TrueColor)
    {
        var session = CreateSession(MarkdownStreamKind.Assistant, messageId, name);
        session.Append(source);
        var update = session.Complete();
        session.Projection.Prepare(update.Document,
            new(Math.Max(1, outerWidth), MarkdownTheme.FromTheme(theme), colorSystem), new MarkdownLayoutEngine());
        return new(name, update.Document, session.Projection);
    }

    /// <summary>Creates a finalized reasoning message cell.</summary>
    public static ReasoningMessageCell CreateReasoning(string messageId, string source, int outerWidth, Theme theme,
        ColorSystem colorSystem = ColorSystem.TrueColor)
    {
        var session = CreateSession(MarkdownStreamKind.Reasoning, messageId, null);
        session.Append(source);
        var update = session.Complete();
        var mutedTheme = AgentTuiTranscriptRenderServices.Default.CreateMutedTheme(theme);
        session.Projection.Prepare(update.Document,
            new(Math.Max(1, outerWidth - 2), MarkdownTheme.FromTheme(mutedTheme), colorSystem), new MarkdownLayoutEngine());
        return new(update.Document, session.Projection);
    }

    private static MarkdownStreamSession CreateSession(MarkdownStreamKind kind, string messageId, string? author) =>
        new(new(kind, messageId), new MarkdownMessagePresentation(AuthorName: author));
}
