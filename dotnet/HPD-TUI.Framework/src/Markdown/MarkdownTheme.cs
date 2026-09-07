using HPD.TUI.Core;

namespace HPD.TUI.Markdown;

/// <summary>Defines immutable, independently configurable Markdown styles.</summary>
/// <remarks>Use a record copy to override selected styles. Nested emphasis inherits unspecified colors
/// and combines attributes with its surrounding style; explicit inner colors take precedence.</remarks>
public sealed record MarkdownTheme
{
    /// <summary>Gets the Body style.</summary>
    public Style Body { get; init; } = Theme.Default.Text;
    /// <summary>Gets the Heading1 style.</summary>
    public Style Heading1 { get; init; } = Theme.Default.Accent with { Attributes = TextAttributes.Bold };
    /// <summary>Gets the Heading2 style.</summary>
    public Style Heading2 { get; init; } = Theme.Default.Accent with { Attributes = TextAttributes.Bold };
    /// <summary>Gets the Heading3 style.</summary>
    public Style Heading3 { get; init; } = Theme.Default.Accent;
    /// <summary>Gets the Heading4 style.</summary>
    public Style Heading4 { get; init; } = Theme.Default.Accent;
    /// <summary>Gets the Heading5 style.</summary>
    public Style Heading5 { get; init; } = Theme.Default.Accent;
    /// <summary>Gets the Heading6 style.</summary>
    public Style Heading6 { get; init; } = Theme.Default.Accent;
    /// <summary>Gets the Link style.</summary>
    public Style Link { get; init; } = Theme.Default.Accent with { Attributes = TextAttributes.Underline };
    /// <summary>Gets the InlineCode style.</summary>
    public Style InlineCode { get; init; } = Theme.Default.Accent;
    /// <summary>Gets the style for unhighlighted fenced-code content.</summary>
    public Style CodeBody { get; init; } = Theme.Default.Text;
    /// <summary>Gets the CodeBorder style.</summary>
    public Style CodeBorder { get; init; } = Theme.Default.Border;
    /// <summary>Gets the CodeLanguage style.</summary>
    public Style CodeLanguage { get; init; } = Theme.Default.Warning;
    /// <summary>Gets the QuoteText style.</summary>
    public Style QuoteText { get; init; } = Theme.Default.Text with { Attributes = TextAttributes.Italic };
    /// <summary>Gets the QuoteMarker style.</summary>
    public Style QuoteMarker { get; init; } = Theme.Default.Success;
    /// <summary>Gets the ListMarker style.</summary>
    public Style ListMarker { get; init; } = Theme.Default.Border;
    /// <summary>Gets the TaskChecked style.</summary>
    public Style TaskChecked { get; init; } = Theme.Default.Success;
    /// <summary>Gets the TaskUnchecked style.</summary>
    public Style TaskUnchecked { get; init; } = Theme.Default.Border;
    /// <summary>Gets the TableBorder style.</summary>
    public Style TableBorder { get; init; } = Theme.Default.Border;
    /// <summary>Gets the TableHeader style.</summary>
    public Style TableHeader { get; init; } = Theme.Default.Text with { Attributes = TextAttributes.Bold };
    /// <summary>Gets the TableBody style.</summary>
    public Style TableBody { get; init; } = Theme.Default.Text;
    /// <summary>Gets the ThematicBreak style.</summary>
    public Style ThematicBreak { get; init; } = Theme.Default.Border;
    /// <summary>Gets the Image style.</summary>
    public Style Image { get; init; } = Theme.Default.Border;
    /// <summary>Gets the Html style.</summary>
    public Style Html { get; init; } = Theme.Default.Border;
    /// <summary>Gets the strong overrides applied to the enclosing style.</summary>
    public MarkdownInlineStyle Strong { get; init; } = new() { Attributes = TextAttributes.Bold };
    /// <summary>Gets the emphasis overrides applied to the enclosing style.</summary>
    public MarkdownInlineStyle Emphasis { get; init; } = new() { Attributes = TextAttributes.Italic };
    /// <summary>Gets the strikethrough overrides applied to the enclosing style.</summary>
    public MarkdownInlineStyle Strikethrough { get; init; } = new() { Attributes = TextAttributes.Strikethrough };
    /// <summary>Gets independent fenced-code syntax styles.</summary>
    public CodeSyntaxTheme Syntax { get; init; } = CodeSyntaxTheme.FromTheme(Theme.Default);
    /// <summary>Gets an exact structural cache identity covering every Markdown and syntax style.</summary>
    public MarkdownThemeKey ThemeKey => new(this);

    /// <summary>Creates a Markdown palette using the supplied UI theme as its defaults.</summary>
    /// <param name="theme">The UI palette from which to derive defaults.</param>
    /// <returns>An immutable palette that can be customized with a record copy.</returns>
    public static MarkdownTheme FromTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new()
        {
            Body = theme.Text,
            Heading1 = theme.Accent with { Attributes = TextAttributes.Bold },
            Heading2 = theme.Accent with { Attributes = TextAttributes.Bold },
            Heading3 = theme.Accent,
            Heading4 = theme.Accent,
            Heading5 = theme.Accent,
            Heading6 = theme.Accent,
            Link = theme.Accent with { Attributes = TextAttributes.Underline },
            InlineCode = theme.Accent,
            CodeBody = theme.Text,
            CodeBorder = theme.Border,
            CodeLanguage = theme.Warning,
            QuoteText = theme.Text with { Attributes = TextAttributes.Italic },
            QuoteMarker = theme.Success,
            ListMarker = theme.Border,
            TaskChecked = theme.Success,
            TaskUnchecked = theme.Border,
            TableBorder = theme.Border,
            TableHeader = theme.Text with { Attributes = TextAttributes.Bold },
            TableBody = theme.Text,
            ThematicBreak = theme.Border,
            Image = theme.Border,
            Html = theme.Border,
            Syntax = CodeSyntaxTheme.FromTheme(theme)
        };
    }
}

/// <summary>Identifies a complete immutable Markdown palette by structural value.</summary>
/// <param name="Palette">Every style affecting Markdown output, including syntax colors.</param>
public readonly record struct MarkdownThemeKey(MarkdownTheme Palette);

/// <summary>Overrides selected colors and adds attributes without discarding enclosing inline styles.</summary>
public sealed record MarkdownInlineStyle
{
    /// <summary>Gets the foreground override, or null to inherit.</summary>
    public Color? Foreground { get; init; }
    /// <summary>Gets the background override, or null to inherit.</summary>
    public Color? Background { get; init; }
    /// <summary>Gets attributes to combine with the enclosing style.</summary>
    public TextAttributes Attributes { get; init; }
    /// <summary>Applies this override to an enclosing style.</summary>
    /// <param name="parent">The surrounding text, heading, or link style.</param>
    /// <returns>The combined style.</returns>
    public Style Apply(Style parent) => new(Foreground ?? parent.Foreground, Background ?? parent.Background,
        parent.Attributes | Attributes);
}

/// <summary>Defines fenced-code token styles independently of Markdown element styles.</summary>
public sealed record CodeSyntaxTheme
{
    /// <summary>Gets the style for text tokens.</summary>
    public Style Text { get; init; } = Theme.Default.Text;
    /// <summary>Gets the style for keyword tokens.</summary>
    public Style Keyword { get; init; } = Theme.Default.Accent with { Attributes = TextAttributes.Bold };
    /// <summary>Gets the style for string tokens.</summary>
    public Style String { get; init; } = Theme.Default.Warning;
    /// <summary>Gets the style for number tokens.</summary>
    public Style Number { get; init; } = Theme.Default.Success;
    /// <summary>Gets the style for identifier tokens.</summary>
    public Style Identifier { get; init; } = Theme.Default.Text;
    /// <summary>Gets the style for function declarations and call names recognized lexically.</summary>
    public Style Function { get; init; } = Theme.Default.Blue;
    /// <summary>Gets the style for built-in types, type declarations, and conventionally capitalized type names.</summary>
    public Style Type { get; init; } = Theme.Default.Warning;
    /// <summary>Gets the style for symbolic operators.</summary>
    public Style Operator { get; init; } = Theme.Default.Border;
    /// <summary>Gets the style for punctuation tokens.</summary>
    public Style Punctuation { get; init; } = Theme.Default.Border;
    /// <summary>Gets the style for comment tokens.</summary>
    public Style Comment { get; init; } = Theme.Default.Border with { Attributes = TextAttributes.Italic };
    /// <summary>Creates default syntax styles from a UI theme.</summary>
    /// <param name="theme">The source UI theme.</param>
    /// <returns>An independently customizable syntax palette.</returns>
    public static CodeSyntaxTheme FromTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new()
        {
            Text = theme.Text,
            Keyword = theme.Accent with { Attributes = TextAttributes.Bold },
            String = theme.Warning,
            Number = theme.Success,
            Identifier = theme.Text,
            Function = theme.Blue,
            Type = theme.Warning,
            Operator = theme.Border,
            Punctuation = theme.Border,
            Comment = theme.Border with { Attributes = TextAttributes.Italic },
        };
    }
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
