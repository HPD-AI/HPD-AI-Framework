using FluentAssertions;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Edge case tests for <see cref="SlackFormatConverter"/>.
/// Tests complex formatting scenarios: nested formatting, code protection,
/// multiple consecutive elements, whitespace handling, and unicode preservation.
/// </summary>
public class SlackFormatConverterEdgeCasesTests
{
    private readonly SlackFormatConverter _converter = new();

    // ── Edge Cases: Nested Formatting ──────────────────────────────────────

    [Fact]
    public void ToMrkdwn_BoldThenItalic_BothConverted()
    {
        // **bold** followed by *italic*
        var result = _converter.ToMrkdwn("**bold** and *italic*");
        result.Should().Be("*bold* and _italic_");
    }

    [Fact]
    public void ToMrkdwn_ItalicThenBold_BothConverted()
    {
        // *italic* followed by **bold**
        var result = _converter.ToMrkdwn("*italic* then **bold**");
        result.Should().Be("_italic_ then *bold*");
    }

    [Fact]
    public void ToMrkdwn_StrikeThroughPreserved_ConvertedCorrectly()
    {
        // ~~strikethrough~~ → ~strikethrough~
        var result = _converter.ToMrkdwn("~~strikethrough~~");
        result.Should().Be("~strikethrough~");
    }

    [Fact]
    public void ToMrkdwn_StrikeWithBoldAndItalic_AllConverted()
    {
        var result = _converter.ToMrkdwn("~~**bold strike**~~ and *strike italic*");
        result.Should().Be("~*bold strike*~ and _strike italic_");
    }

    // ── Edge Cases: Code Protection ────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_CodeSpan_NotConverted()
    {
        // Inside backticks, ** should NOT become *
        var result = _converter.ToMrkdwn("`**literal asterisks**`");
        result.Should().Be("`**literal asterisks**`");
    }

    [Fact]
    public void ToMrkdwn_CodeSpan_ProtectsUnderscores()
    {
        // Underscores inside code should be preserved
        var result = _converter.ToMrkdwn("`_preserved_`");
        result.Should().Be("`_preserved_`");
    }

    [Fact]
    public void ToMrkdwn_CodeSpan_ProtectsTildes()
    {
        // Tildes inside code should be preserved as-is
        var result = _converter.ToMrkdwn("`~~not strike~~`");
        result.Should().Be("`~~not strike~~`");
    }

    [Fact]
    public void ToMrkdwn_CodeBlock_ContentPreserved()
    {
        // Line 2 has **markdown** that should NOT be converted in code block
        var markdown = "text\n```\n**not bold**\n```\nmore";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Contain("**not bold**");
    }

    [Fact]
    public void ToMrkdwn_CodeBlockTilde_LanguageHintIgnored()
    {
        // ~~~ fences are also code blocks
        var markdown = "~~~csharp\n**not bold**\n~~~";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Contain("```") // Output should use backticks
            .And.Contain("**not bold**");
    }

    [Fact]
    public void ToMrkdwn_MultipleCodeSpans_AllProtected()
    {
        var result = _converter.ToMrkdwn("Use `**code1**` and `_code2_` together");
        result.Should().Be("Use `**code1**` and `_code2_` together");
    }

    [Fact]
    public void ToMrkdwn_CodeSpanAdjacentToFormatting_CodeProtected()
    {
        // Bold adjacent to code: **bold**`code`**more**
        var result = _converter.ToMrkdwn("**bold**`code`**more**");
        result.Should().Be("*bold*`code`*more*");
    }

    // ── Edge Cases: Multiple Links ────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_TwoConsecutiveLinks_BothConverted()
    {
        var result = _converter.ToMrkdwn("[link1](url1)[link2](url2)");
        // Note: no space between, should both convert
        result.Should().Contain("<url1|link1>")
            .And.Contain("<url2|link2>");
    }

    [Fact]
    public void ToMrkdwn_LinksWithSpaces_BothConverted()
    {
        var result = _converter.ToMrkdwn("[first](http://a) [second](http://b)");
        result.Should().Be("<http://a|first> <http://b|second>");
    }

    [Fact]
    public void ToMrkdwn_LinkWithFormattedText_TextConverted()
    {
        // Link text itself contains formatting — both should convert
        var result = _converter.ToMrkdwn("[**bold link**](url)");
        result.Should().Contain("<url|*bold link*>");
    }

    [Fact]
    public void ToMrkdwn_LinkWithCodeInText_CodeProtected()
    {
        var result = _converter.ToMrkdwn("[`code` in link](url)");
        result.Should().Contain("<url|`code` in link>");
    }

    // ── Edge Cases: List Formatting ────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_UnorderedList_ConvertedToBullets()
    {
        var markdown = "- item1\n- item2\n- item3";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Be("• item1\n• item2\n• item3");
    }

    [Fact]
    public void ToMrkdwn_OrderedList_NumbersIncremented()
    {
        var markdown = "1. first\n2. second\n3. third";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Be("1. first\n2. second\n3. third");
    }

    [Fact]
    public void ToMrkdwn_OrderedListIgnoresSourceNumbers_ReNumber()
    {
        // Input has wrong numbering, should be fixed
        var markdown = "5. first\n3. second\n1. third";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Be("1. first\n2. second\n3. third");
    }

    [Fact]
    public void ToMrkdwn_MixedListTypes_ResetCounter()
    {
        var markdown = "1. ordered\n- unordered\n2. ordered again";
        var result = _converter.ToMrkdwn(markdown);
        // After switching to unordered, ordered counter should reset
        result.Should().StartWith("1. ordered\n• unordered\n1. ordered again");
    }

    [Fact]
    public void ToMrkdwn_ListItemsWithFormatting_FormattingConverted()
    {
        var markdown = "- **bold** item\n- *italic* item";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Be("• *bold* item\n• _italic_ item");
    }

    // ── Edge Cases: Whitespace ────────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_CodeBlockWhitespace_Preserved()
    {
        var markdown = "```\n  indented\n    more indented\n```";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Contain("  indented")
            .And.Contain("    more indented");
    }

    [Fact]
    public void ToMrkdwn_TrailingSpacesInCodeBlock_Preserved()
    {
        var markdown = "```\nline with spaces   \n```";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Contain("line with spaces   ");
    }

    [Fact]
    public void ToMrkdwn_EmptyLinesInCodeBlock_Preserved()
    {
        var markdown = "```\nfirst\n\nsecond\n```";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Contain("first\n\nsecond");
    }

    [Fact]
    public void ToMrkdwn_NormalizeLineEndings_LFPreserved()
    {
        var markdown = "line1\nline2";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Be("line1\nline2");
    }

    // ── Edge Cases: Unicode & Emoji ────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_Emoji_Preserved()
    {
        var result = _converter.ToMrkdwn("**Important** 🎉 announcement");
        result.Should().Be("*Important* 🎉 announcement");
    }

    [Fact]
    public void ToMrkdwn_Emoji_InCode_Preserved()
    {
        var result = _converter.ToMrkdwn("`emoji: 😀`");
        result.Should().Be("`emoji: 😀`");
    }

    [Fact]
    public void ToMrkdwn_UnicodeCharacters_Preserved()
    {
        var result = _converter.ToMrkdwn("**Héllo** wørld");
        result.Should().Be("*Héllo* wørld");
    }

    [Fact]
    public void ToMrkdwn_MixedScripts_Preserved()
    {
        var result = _converter.ToMrkdwn("**Bold 中文** και *ελληνικά*");
        result.Should().Be("*Bold 中文* και _ελληνικά_");
    }

    // ── Edge Cases: Empty Elements ─────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_EmptyBold_PassThrough()
    {
        var result = _converter.ToMrkdwn("****");
        // Empty bold markers should still be attempted to convert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ToMrkdwn_EmptyCodeBlock_Preserved()
    {
        var markdown = "```\n```";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Contain("```");
    }

    [Fact]
    public void ToMrkdwn_SingleNewline_Preserved()
    {
        var result = _converter.ToMrkdwn("line1\n\nline2");
        result.Should().Be("line1\n\nline2");
    }

    // ── Edge Cases: Blockquotes ────────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_Blockquote_ConvertedCorrectly()
    {
        var markdown = "> This is a quote";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Be("> This is a quote");
    }

    [Fact]
    public void ToMrkdwn_BlockquoteWithFormatting_FormattingConverted()
    {
        var markdown = "> **quoted bold** and *italic*";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Be("> *quoted bold* and _italic_");
    }

    [Fact]
    public void ToMrkdwn_MultipleBlockquotes_AllConverted()
    {
        var markdown = "> line 1\n> line 2";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Be("> line 1\n> line 2");
    }

    // ── Edge Cases: Complex Combinations ───────────────────────────────────

    [Fact]
    public void ToMrkdwn_CodeBlockFollowedByList_BothConverted()
    {
        var markdown = "```\n**code**\n```\n- bullet";
        var result = _converter.ToMrkdwn(markdown);
        result.Should().Contain("**code**")  // Inside code block
            .And.Contain("• bullet");
    }

    [Fact]
    public void ToMrkdwn_UnclosedCodeFence_StillConverted()
    {
        var markdown = "text\n```\n**unclosed**";
        var result = _converter.ToMrkdwn(markdown);
        // Should treat remainder as code block
        result.Should().Contain("**unclosed**");
    }

    [Fact]
    public void ToMrkdwn_SpecialSlackCharacters_EscapingPreserved()
    {
        // Test that < > @ # characters are preserved (they have meaning in Slack)
        var result = _converter.ToMrkdwn("**text** with @mention and <url>");
        result.Should().Contain("@mention")
            .And.Contain("<url>");
    }
}
