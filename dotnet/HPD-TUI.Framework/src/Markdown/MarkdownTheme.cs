using HPD.TUI.Core;

namespace HPD.TUI.Markdown;

/// <summary>Defines semantic styles used by terminal Markdown layout.</summary>
public sealed record MarkdownTheme
{
    /// <summary>Gets the structural identity of the framework theme from which these tokens were derived.</summary>
    public required ThemeKey ThemeKey { get; init; }
    /// <summary>Gets ordinary prose styling.</summary>
    public required Style Body { get; init; }
    /// <summary>Gets first-level heading styling.</summary>
    public required Style Heading1 { get; init; }
    /// <summary>Gets second-level heading styling.</summary>
    public required Style Heading2 { get; init; }
    /// <summary>Gets third-and-lower heading styling.</summary>
    public required Style Heading3 { get; init; }
    /// <summary>Gets strong-emphasis styling.</summary>
    public required Style Strong { get; init; }
    /// <summary>Gets emphasis styling.</summary>
    public required Style Emphasis { get; init; }
    /// <summary>Gets validated-link styling.</summary>
    public required Style Link { get; init; }
    /// <summary>Gets inline-code styling.</summary>
    public required Style InlineCode { get; init; }
    /// <summary>Gets fenced-code border styling.</summary>
    public required Style CodeBorder { get; init; }
    /// <summary>Gets code-language label styling.</summary>
    public required Style CodeLanguage { get; init; }
    /// <summary>Gets quote-marker styling.</summary>
    public required Style QuoteMarker { get; init; }
    /// <summary>Gets table-border styling.</summary>
    public required Style TableBorder { get; init; }
    /// <summary>Gets table-header styling.</summary>
    public required Style TableHeader { get; init; }

    /// <summary>Creates semantic Markdown styles from a framework theme.</summary>
    public static MarkdownTheme FromTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new()
        {
            ThemeKey = theme.Key,
            Body = theme.Text,
            Heading1 = theme.Accent with { Attributes = TextAttributes.Bold },
            Heading2 = theme.Accent with { Attributes = TextAttributes.Bold },
            Heading3 = theme.Accent,
            Strong = theme.Text with { Attributes = TextAttributes.Bold },
            Emphasis = theme.Text with { Attributes = TextAttributes.Italic },
            Link = theme.Accent with { Attributes = TextAttributes.Underline },
            InlineCode = theme.Accent,
            CodeBorder = theme.Border,
            CodeLanguage = theme.Warning,
            QuoteMarker = theme.Success,
            TableBorder = theme.Border,
            TableHeader = theme.Text with { Attributes = TextAttributes.Bold }
        };
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
}
