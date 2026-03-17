using FluentAssertions;
using HPD.Agent.Adapters;
using HPD.Agent.Adapters.Cards;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <see cref="CardFallbackText.From"/> — plain-text generation from a CardElement tree.
/// </summary>
public class CardFallbackTextTests
{
    // ── Empty / header-only cards ──────────────────────────────────────

    [Fact]
    public void From_EmptyCard_ReturnsEmptyString()
    {
        var card = new CardElement();

        var result = CardFallbackText.From(card);

        result.Should().BeEmpty();
    }

    [Fact]
    public void From_TitleOnly_ReturnsTitleTrimmed()
    {
        var card = new CardElement(Title: "My Title");

        var result = CardFallbackText.From(card);

        result.Should().Be("My Title");
    }

    [Fact]
    public void From_SubtitleOnly_ReturnsSubtitle()
    {
        var card = new CardElement(Subtitle: "My Subtitle");

        var result = CardFallbackText.From(card);

        result.Should().Be("My Subtitle");
    }

    [Fact]
    public void From_TitleAndSubtitle_BothAppearInOrder()
    {
        var card = new CardElement(Title: "Title", Subtitle: "Subtitle");

        var result = CardFallbackText.From(card);

        result.Should().StartWith("Title");
        result.Should().Contain("Subtitle");
        result.IndexOf("Title").Should().BeLessThan(result.IndexOf("Subtitle"));
    }

    [Fact]
    public void From_TitleAndSubtitle_ResultIsTrimmed()
    {
        var card = new CardElement(Title: "Title", Subtitle: "Subtitle");

        var result = CardFallbackText.From(card);

        result.Should().NotStartWith("\n").And.NotEndWith("\n");
    }

    // ── Children ──────────────────────────────────────────────────────

    [Fact]
    public void From_CardTextChild_AppendsText()
    {
        var card = new CardElement(Children: [new CardText("Hello world")]);

        var result = CardFallbackText.From(card);

        result.Should().Contain("Hello world");
    }

    [Fact]
    public void From_CardFieldsChild_AppendsLabelColonValue()
    {
        var fields = new CardFields([
            new CardField("Author", "Alice"),
            new CardField("Date",   "2025-01-15"),
        ]);
        var card = new CardElement(Children: [fields]);

        var result = CardFallbackText.From(card);

        result.Should().Contain("Author: Alice");
        result.Should().Contain("Date: 2025-01-15");
    }

    [Fact]
    public void From_CardLinkChild_AppendsLabelParenUrl()
    {
        var card = new CardElement(Children: [new CardLink("Open Doc", "https://example.com/doc")]);

        var result = CardFallbackText.From(card);

        result.Should().Contain("Open Doc (https://example.com/doc)");
    }

    [Fact]
    public void From_CardSectionChild_RecursesIntoChildren()
    {
        var section = new CardSection(Children: [
            new CardText("Section text"),
            new CardFields([new CardField("Key", "Value")]),
        ]);
        var card = new CardElement(Children: [section]);

        var result = CardFallbackText.From(card);

        result.Should().Contain("Section text");
        result.Should().Contain("Key: Value");
    }

    // ── Excluded types ────────────────────────────────────────────────

    [Fact]
    public void From_CardImageChild_ProducesNoOutput()
    {
        var card = new CardElement(Children: [new CardImage("https://example.com/img.png")]);

        var result = CardFallbackText.From(card);

        result.Should().BeEmpty();
    }

    [Fact]
    public void From_CardDividerChild_ProducesNoOutput()
    {
        var card = new CardElement(Children: [new CardDivider()]);

        var result = CardFallbackText.From(card);

        result.Should().BeEmpty();
    }

    [Fact]
    public void From_CardActionsChild_ProducesNoOutput()
    {
        var button = new CardButton("btn", "Click me");
        var card = new CardElement(Children: [new CardActions([button])]);

        var result = CardFallbackText.From(card);

        result.Should().BeEmpty();
    }

    // ── Mixed children ────────────────────────────────────────────────

    [Fact]
    public void From_MixedChildren_OnlyRendersTextualElements()
    {
        var card = new CardElement(
            Title: "Report",
            Children: [
                new CardText("Summary line"),
                new CardImage("https://example.com/chart.png"),
                new CardFields([new CardField("Status", "OK")]),
                new CardDivider(),
                new CardLink("Download", "https://example.com/file"),
                new CardActions([new CardButton("dl", "Download")]),
            ]);

        var result = CardFallbackText.From(card);

        result.Should().Contain("Report");
        result.Should().Contain("Summary line");
        result.Should().Contain("Status: OK");
        result.Should().Contain("Download (https://example.com/file)");
        // Excluded elements leave no artefacts
        result.Should().NotContain("chart.png");
        result.Should().NotContain("Click me");
    }

    // ── Nested sections ───────────────────────────────────────────────

    [Fact]
    public void From_NestedSections_RecursesMultipleLevels()
    {
        var inner = new CardSection(Children: [new CardText("Deep text")]);
        var outer = new CardSection(Children: [inner, new CardText("Outer text")]);
        var card  = new CardElement(Children: [outer]);

        var result = CardFallbackText.From(card);

        result.Should().Contain("Deep text");
        result.Should().Contain("Outer text");
    }

    // ── Null / empty guards ───────────────────────────────────────────

    [Fact]
    public void From_NullChildren_ReturnsHeaderOnly()
    {
        var card = new CardElement(Title: "Title", Children: null);

        var act = () => CardFallbackText.From(card);

        act.Should().NotThrow();
        CardFallbackText.From(card).Should().Be("Title");
    }

    [Fact]
    public void From_SectionWithNullChildren_DoesNotThrow()
    {
        var section = new CardSection(Children: null);
        var card    = new CardElement(Children: [section]);

        var act = () => CardFallbackText.From(card);

        act.Should().NotThrow();
    }

    [Fact]
    public void From_EmptyFieldsList_NoExtraLines()
    {
        var card = new CardElement(Children: [new CardFields([])]);

        var result = CardFallbackText.From(card);

        // No content means empty result; must not produce ": " artefacts
        result.Should().BeEmpty();
        result.Should().NotContain(": ");
    }

    // ── Snapshot ──────────────────────────────────────────────────────

    [Fact]
    public void From_FullRealisticCard_ProducesExpectedSnapshot()
    {
        var card = new CardElement(
            Title:    "Search Results",
            Subtitle: "Found 2 documents",
            Children: [
                new CardText("Matching documents from Q4"),
                new CardFields([
                    new CardField("Title", "Q4 Report"),
                    new CardField("Date",  "2025-01-15"),
                ]),
                new CardLink("Open", "https://example.com/q4"),
                new CardDivider(),
                new CardActions([new CardButton("open", "Open")]),
            ]);

        var result = CardFallbackText.From(card);

        result.Should().Contain("Search Results");
        result.Should().Contain("Found 2 documents");
        result.Should().Contain("Matching documents from Q4");
        result.Should().Contain("Title: Q4 Report");
        result.Should().Contain("Date: 2025-01-15");
        result.Should().Contain("Open (https://example.com/q4)");
        result.Should().NotContain("Open\n");  // button label excluded
    }
}
