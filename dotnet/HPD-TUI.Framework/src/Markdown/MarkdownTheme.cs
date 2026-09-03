using HPD.TUI.Core;

namespace HPD.TUI.Markdown;

/// <summary>Defines semantic styles used by terminal Markdown layout.</summary>
public sealed class MarkdownTheme
{
    private MarkdownTheme(Theme theme)
    {
        ThemeKey = theme.Key;
        Body = theme.Text;
        Heading1 = theme.Accent with { Attributes = TextAttributes.Bold };
        Heading2 = theme.Accent with { Attributes = TextAttributes.Bold };
        Heading3 = theme.Accent;
        Strong = theme.Text with { Attributes = TextAttributes.Bold };
        Emphasis = theme.Text with { Attributes = TextAttributes.Italic };
        Link = theme.Accent with { Attributes = TextAttributes.Underline };
        InlineCode = theme.Accent;
        CodeBorder = theme.Border;
        CodeLanguage = theme.Warning;
        QuoteMarker = theme.Success;
        TableBorder = theme.Border;
        TableHeader = theme.Text with { Attributes = TextAttributes.Bold };
    }
    /// <summary>Gets the structural identity of the framework theme from which these tokens were derived.</summary>
    public ThemeKey ThemeKey { get; }
    /// <summary>Gets ordinary prose styling.</summary>
    public Style Body { get; }
    /// <summary>Gets first-level heading styling.</summary>
    public Style Heading1 { get; }
    /// <summary>Gets second-level heading styling.</summary>
    public Style Heading2 { get; }
    /// <summary>Gets third-and-lower heading styling.</summary>
    public Style Heading3 { get; }
    /// <summary>Gets strong-emphasis styling.</summary>
    public Style Strong { get; }
    /// <summary>Gets emphasis styling.</summary>
    public Style Emphasis { get; }
    /// <summary>Gets validated-link styling.</summary>
    public Style Link { get; }
    /// <summary>Gets inline-code styling.</summary>
    public Style InlineCode { get; }
    /// <summary>Gets fenced-code border styling.</summary>
    public Style CodeBorder { get; }
    /// <summary>Gets code-language label styling.</summary>
    public Style CodeLanguage { get; }
    /// <summary>Gets quote-marker styling.</summary>
    public Style QuoteMarker { get; }
    /// <summary>Gets table-border styling.</summary>
    public Style TableBorder { get; }
    /// <summary>Gets table-header styling.</summary>
    public Style TableHeader { get; }

    /// <summary>Creates semantic Markdown styles from a framework theme.</summary>
    public static MarkdownTheme FromTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new(theme);
    }

    internal Theme ToFrameworkTheme() => new()
    {
        Text = Body,
        Accent = Heading1,
        Blue = Link,
        Border = CodeBorder,
        Error = Body,
        Success = QuoteMarker,
        Warning = CodeLanguage
    };
}

/// <summary>Defines semantic block spacing for Markdown layout.</summary>
public sealed record MarkdownSpacing
{
    /// <summary>Gets the paragraph separator row count.</summary>
    public int ParagraphGap { get; init; } = 1;
    /// <summary>Gets rows inserted before headings.</summary>
    public int HeadingTopGap { get; init; } = 1;
    /// <summary>Gets rows inserted after headings.</summary>
    public int HeadingBottomGap { get; init; }
    /// <summary>Gets list indentation columns.</summary>
    public int ListIndent { get; init; } = 2;
    /// <summary>Gets quote indentation columns.</summary>
    public int QuoteIndent { get; init; } = 2;
    /// <summary>Gets code indentation columns.</summary>
    public int CodeIndent { get; init; } = 2;

    /// <summary>Gets a structural identity containing every behavior-affecting spacing value.</summary>
    public MarkdownSpacingKey Key => new(ParagraphGap, HeadingTopGap, HeadingBottomGap, ListIndent, QuoteIndent, CodeIndent);
}

/// <summary>Structurally identifies Markdown spacing inputs used by layout.</summary>
public readonly record struct MarkdownSpacingKey(
    int ParagraphGap,
    int HeadingTopGap,
    int HeadingBottomGap,
    int ListIndent,
    int QuoteIndent,
    int CodeIndent);
