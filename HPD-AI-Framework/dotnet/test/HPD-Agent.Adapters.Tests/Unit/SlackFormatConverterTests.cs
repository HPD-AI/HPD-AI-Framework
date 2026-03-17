using FluentAssertions;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <see cref="SlackFormatConverter"/>.
/// Covers <c>ToMrkdwn</c> (markdown → Slack mrkdwn) and <c>ToPlainText</c> (mrkdwn → plain text).
/// </summary>
public class SlackFormatConverterTests
{
    private readonly SlackFormatConverter _converter = new();

    // ── ToMrkdwn — guards ────────────────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_Empty_ReturnsEmpty()
    {
        _converter.ToMrkdwn("").Should().Be("");
    }

    [Fact]
    public void ToMrkdwn_Null_ReturnsNull()
    {
        _converter.ToMrkdwn(null!).Should().BeNull();
    }

    [Fact]
    public void ToMrkdwn_PlainText_Passthrough()
    {
        _converter.ToMrkdwn("hello world").Should().Be("hello world");
    }

    // ── ToMrkdwn — inline: bold ───────────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_Bold_DoubleAsterisks_Converted()
    {
        _converter.ToMrkdwn("**bold**").Should().Be("*bold*");
    }

    [Fact]
    public void ToMrkdwn_Bold_DoubleUnderscores_Converted()
    {
        _converter.ToMrkdwn("__bold__").Should().Be("*bold*");
    }

    [Fact]
    public void ToMrkdwn_Bold_MidSentence()
    {
        _converter.ToMrkdwn("This is **important** text.").Should().Be("This is *important* text.");
    }

    // ── ToMrkdwn — inline: italic ────────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_Italic_SingleAsterisk_Converted()
    {
        _converter.ToMrkdwn("*italic*").Should().Be("_italic_");
    }

    [Fact]
    public void ToMrkdwn_Italic_SingleUnderscore_Preserved()
    {
        // Slack _x_ is already correct — identity conversion
        _converter.ToMrkdwn("_italic_").Should().Be("_italic_");
    }

    // ── ToMrkdwn — inline: strikethrough ────────────────────────────────────

    [Fact]
    public void ToMrkdwn_Strike_DoublesTilde_Converted()
    {
        _converter.ToMrkdwn("~~strike~~").Should().Be("~strike~");
    }

    // ── ToMrkdwn — inline: code ──────────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_InlineCode_BackticksPreserved()
    {
        _converter.ToMrkdwn("`code`").Should().Be("`code`");
    }

    [Fact]
    public void ToMrkdwn_InlineCode_ContentsNotConvertedForBold()
    {
        // Bold markers inside backticks must not be converted
        _converter.ToMrkdwn("`**not bold**`").Should().Be("`**not bold**`");
    }

    // ── ToMrkdwn — inline: links ─────────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_Link_ConvertedToSlackFormat()
    {
        _converter.ToMrkdwn("[click here](https://example.com)")
            .Should().Be("<https://example.com|click here>");
    }

    [Fact]
    public void ToMrkdwn_Link_MidSentence()
    {
        _converter.ToMrkdwn("See [docs](https://docs.example.com) for details.")
            .Should().Be("See <https://docs.example.com|docs> for details.");
    }

    // ── ToMrkdwn — inline: combined ──────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_BoldAndItalicCombined()
    {
        _converter.ToMrkdwn("**bold** and *italic*").Should().Be("*bold* and _italic_");
    }

    [Fact]
    public void ToMrkdwn_AllInlineFormats_OnOneLine()
    {
        _converter.ToMrkdwn("**b** *i* ~~s~~ `c` [l](https://x.com)")
            .Should().Be("*b* _i_ ~s~ `c` <https://x.com|l>");
    }

    // ── ToMrkdwn — block: blockquote ─────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_Blockquote_Preserved()
    {
        _converter.ToMrkdwn("> quote text").Should().Be("> quote text");
    }

    [Fact]
    public void ToMrkdwn_Blockquote_InlineFormattingConvertedInside()
    {
        _converter.ToMrkdwn("> **bold quote**").Should().Be("> *bold quote*");
    }

    // ── ToMrkdwn — block: unordered lists ────────────────────────────────────

    [Fact]
    public void ToMrkdwn_UnorderedList_Dash_ConvertedToBullet()
    {
        _converter.ToMrkdwn("- item").Should().Be("• item");
    }

    [Fact]
    public void ToMrkdwn_UnorderedList_Asterisk_ConvertedToBullet()
    {
        _converter.ToMrkdwn("* item").Should().Be("• item");
    }

    [Fact]
    public void ToMrkdwn_UnorderedList_Plus_ConvertedToBullet()
    {
        _converter.ToMrkdwn("+ item").Should().Be("• item");
    }

    [Fact]
    public void ToMrkdwn_UnorderedList_MultipleItems()
    {
        _converter.ToMrkdwn("- first\n- second\n- third")
            .Should().Be("• first\n• second\n• third");
    }

    // ── ToMrkdwn — block: ordered lists ──────────────────────────────────────

    [Fact]
    public void ToMrkdwn_OrderedList_SingleItem()
    {
        _converter.ToMrkdwn("1. first").Should().Be("1. first");
    }

    [Fact]
    public void ToMrkdwn_OrderedList_MultipleItems_CounterIncrements()
    {
        _converter.ToMrkdwn("1. a\n2. b\n3. c").Should().Be("1. a\n2. b\n3. c");
    }

    [Fact]
    public void ToMrkdwn_OrderedList_NonSequential_Renumbered()
    {
        // Source has non-sequential numbers; counter always increments from 1
        _converter.ToMrkdwn("1. a\n5. b\n99. c").Should().Be("1. a\n2. b\n3. c");
    }

    // ── ToMrkdwn — block: multiline ──────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_MultilineText_NewlinesPreserved()
    {
        _converter.ToMrkdwn("line one\nline two\nline three")
            .Should().Be("line one\nline two\nline three");
    }

    // ── ToMrkdwn — code blocks ───────────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_FencedCodeBlock_Backtick_Preserved()
    {
        _converter.ToMrkdwn("```\nsome code\n```")
            .Should().Be("```\nsome code\n```");
    }

    [Fact]
    public void ToMrkdwn_FencedCodeBlock_Tilde_NormalizedToBacktick()
    {
        _converter.ToMrkdwn("~~~\nsome code\n~~~")
            .Should().Be("```\nsome code\n```");
    }

    [Fact]
    public void ToMrkdwn_FencedCodeBlock_LanguageHintStripped()
    {
        _converter.ToMrkdwn("```csharp\nvar x = 1;\n```")
            .Should().Be("```\nvar x = 1;\n```");
    }

    [Fact]
    public void ToMrkdwn_FencedCodeBlock_ContentsNotConverted()
    {
        // Bold markers inside a code fence must not be touched
        _converter.ToMrkdwn("```\n**not bold**\n~~not strike~~\n```")
            .Should().Be("```\n**not bold**\n~~not strike~~\n```");
    }

    [Fact]
    public void ToMrkdwn_UnclosedCodeFence_EmitsContentAndClosingFence()
    {
        _converter.ToMrkdwn("```\ncode without closing fence")
            .Should().Be("```\ncode without closing fence\n```");
    }

    [Fact]
    public void ToMrkdwn_CodeBlockSurroundedByText()
    {
        _converter.ToMrkdwn("before\n```\ncode\n```\nafter")
            .Should().Be("before\n```\ncode\n```\nafter");
    }

    // ── ToMrkdwn — snapshot ──────────────────────────────────────────────────

    [Fact]
    public void ToMrkdwn_RealisticMessage_FullSnapshot()
    {
        var markdown =
            "## Summary\n" +
            "\n" +
            "The build **failed** due to a missing dependency. Steps to fix:\n" +
            "\n" +
            "1. Run `dotnet restore`\n" +
            "2. Check the ~~old~~ new [lock file](https://example.com/lock)\n" +
            "3. Rebuild\n" +
            "\n" +
            "```bash\ndotnet build --no-incremental\n```\n" +
            "\n" +
            "> Note: _incremental builds_ are disabled until resolved.";

        var result = _converter.ToMrkdwn(markdown);

        result.Should().Contain("*failed*");                          // bold converted
        result.Should().Contain("1. Run `dotnet restore`");           // ordered list + inline code
        result.Should().Contain("2. Check the ~old~");                // strike converted
        result.Should().Contain("<https://example.com/lock|lock file>"); // link converted
        result.Should().Contain("3. Rebuild");                        // ordered list counter
        result.Should().Contain("```\ndotnet build --no-incremental\n```"); // code block
        result.Should().Contain("> Note: _incremental builds_");      // blockquote + italic
    }

    // ── ToPlainText ──────────────────────────────────────────────────────────

    [Fact]
    public void ToPlainText_Empty_ReturnsEmpty()
    {
        _converter.ToPlainText("").Should().Be("");
    }

    [Fact]
    public void ToPlainText_PlainText_Unchanged()
    {
        _converter.ToPlainText("hello world").Should().Be("hello world");
    }

    [Fact]
    public void ToPlainText_StripsBoldAsterisks()
    {
        _converter.ToPlainText("*bold*").Should().Be("bold");
    }

    [Fact]
    public void ToPlainText_StripsItalicUnderscores()
    {
        _converter.ToPlainText("_italic_").Should().Be("italic");
    }

    [Fact]
    public void ToPlainText_StripsStrikeTilde()
    {
        _converter.ToPlainText("~strike~").Should().Be("strike");
    }

    [Fact]
    public void ToPlainText_StripsBacktick()
    {
        _converter.ToPlainText("`code`").Should().Be("code");
    }

    [Fact]
    public void ToPlainText_Trims_LeadingAndTrailingWhitespace()
    {
        _converter.ToPlainText("  hello  ").Should().Be("hello");
    }
}
