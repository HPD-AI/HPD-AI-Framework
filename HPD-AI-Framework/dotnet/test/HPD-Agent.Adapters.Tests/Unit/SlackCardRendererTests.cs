using FluentAssertions;
using HPD.Agent.Adapters.Cards;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <see cref="SlackCardRenderer"/> — covers every partial method implementation
/// and the <c>Render(CardElement)</c> entry-point dispatcher.
/// </summary>
public class SlackCardRendererTests
{
    private readonly SlackCardRenderer _renderer = new();

    // ── RenderCard — root element ────────────────────────────────────────────

    [Fact]
    public void RenderCard_EmptyCard_ReturnsNoBlocks()
    {
        var blocks = _renderer.RenderCard(new CardElement());
        blocks.Should().BeEmpty();
    }

    [Fact]
    public void RenderCard_TitleOnly_EmitsSingleHeaderBlock()
    {
        var blocks = _renderer.RenderCard(new CardElement(Title: "Hello"));

        blocks.Should().HaveCount(1);
        blocks[0].Should().BeOfType<SlackHeaderBlock>()
            .Which.Text.Text.Should().Be("Hello");
    }

    [Fact]
    public void RenderCard_SubtitleOnly_EmitsSingleContextBlock()
    {
        var blocks = _renderer.RenderCard(new CardElement(Subtitle: "sub"));

        blocks.Should().HaveCount(1);
        var ctx = blocks[0].Should().BeOfType<SlackContextBlock>().Subject;
        ctx.Elements.Should().HaveCount(1);
        ctx.Elements[0].Should().BeOfType<SlackMrkdwn>()
            .Which.Text.Should().Be("sub");
    }

    [Fact]
    public void RenderCard_ImageUrl_EmitsImageBlock()
    {
        var blocks = _renderer.RenderCard(new CardElement(ImageUrl: "https://x.com/img.png"));

        blocks.Should().HaveCount(1);
        var img = blocks[0].Should().BeOfType<SlackImageBlock>().Subject;
        img.ImageUrl.Should().Be("https://x.com/img.png");
    }

    [Fact]
    public void RenderCard_ImageUrl_AltTextFallsBackToLiteral_WhenNoTitle()
    {
        var blocks = _renderer.RenderCard(new CardElement(ImageUrl: "https://x.com/img.png"));
        blocks[0].Should().BeOfType<SlackImageBlock>()
            .Which.AltText.Should().Be("image");
    }

    [Fact]
    public void RenderCard_ImageUrl_AltTextUsesTitle_WhenTitlePresent()
    {
        var blocks = _renderer.RenderCard(new CardElement(Title: "Card Title", ImageUrl: "https://x.com/img.png"));
        var img = blocks.OfType<SlackImageBlock>().Single();
        img.AltText.Should().Be("Card Title");
    }

    [Fact]
    public void RenderCard_TitleAndSubtitle_HeaderBeforeContext()
    {
        var blocks = _renderer.RenderCard(new CardElement(Title: "T", Subtitle: "S"));

        blocks.Should().HaveCount(2);
        blocks[0].Should().BeOfType<SlackHeaderBlock>();
        blocks[1].Should().BeOfType<SlackContextBlock>();
    }

    [Fact]
    public void RenderCard_TitleSubtitleImageChildren_CorrectOrder()
    {
        var card = new CardElement(
            Title: "T",
            Subtitle: "S",
            ImageUrl: "https://x.com/img.png",
            Children: [new CardText("body")]);

        var blocks = _renderer.RenderCard(card);

        blocks.Should().HaveCount(4);
        blocks[0].Should().BeOfType<SlackHeaderBlock>();
        blocks[1].Should().BeOfType<SlackContextBlock>();
        blocks[2].Should().BeOfType<SlackImageBlock>();
        blocks[3].Should().BeOfType<SlackSectionBlock>(); // CardText
    }

    [Fact]
    public void RenderCard_WithChildren_ChildBlocksAppendedAfterRootBlocks()
    {
        var card = new CardElement(Title: "T", Children: [new CardText("body"), new CardDivider()]);

        var blocks = _renderer.RenderCard(card);

        blocks.Should().HaveCount(3);
        blocks[0].Should().BeOfType<SlackHeaderBlock>();
        blocks[1].Should().BeOfType<SlackSectionBlock>();
        blocks[2].Should().BeOfType<SlackDividerBlock>();
    }

    // ── RenderText ───────────────────────────────────────────────────────────

    [Fact]
    public void RenderText_NullStyle_ReturnsSectionWithExpand()
    {
        var block = _renderer.RenderText(new CardText("Hello", Style: null));

        var section = block.Should().BeOfType<SlackSectionBlock>().Subject;
        section.Text.Should().BeOfType<SlackMrkdwn>().Which.Text.Should().Be("Hello");
        section.Expand.Should().BeTrue();
    }

    [Fact]
    public void RenderText_NoStyle_ReturnsSectionWithExpand()
    {
        var block = _renderer.RenderText(new CardText("Hello"));

        var section = block.Should().BeOfType<SlackSectionBlock>().Subject;
        section.Expand.Should().BeTrue();
    }

    [Fact]
    public void RenderText_MutedStyle_ReturnsContextBlock()
    {
        var block = _renderer.RenderText(new CardText("note", Style: "muted"));

        var ctx = block.Should().BeOfType<SlackContextBlock>().Subject;
        ctx.Elements.Should().HaveCount(1);
        ctx.Elements[0].Should().BeOfType<SlackMrkdwn>().Which.Text.Should().Be("note");
    }

    [Fact]
    public void RenderText_OtherStyle_ReturnsSectionWithExpand()
    {
        // Any style other than "muted" falls through to section
        var block = _renderer.RenderText(new CardText("x", Style: "primary"));
        block.Should().BeOfType<SlackSectionBlock>()
            .Which.Expand.Should().BeTrue();
    }

    // ── RenderImage ──────────────────────────────────────────────────────────

    [Fact]
    public void RenderImage_AllFields_MappedCorrectly()
    {
        var block = _renderer.RenderImage(new CardImage("https://x.com/img.png", "alt text", "A Title"));

        var img = block.Should().BeOfType<SlackImageBlock>().Subject;
        img.ImageUrl.Should().Be("https://x.com/img.png");
        img.AltText.Should().Be("alt text");
        img.Title!.Text.Should().Be("A Title");
    }

    [Fact]
    public void RenderImage_NoAltText_FallsBackToTitle()
    {
        var block = _renderer.RenderImage(new CardImage("https://x.com/img.png", AltText: null, Title: "My Title"));

        block.Should().BeOfType<SlackImageBlock>()
            .Which.AltText.Should().Be("My Title");
    }

    [Fact]
    public void RenderImage_NoAltOrTitle_FallsBackToLiteralImage()
    {
        var block = _renderer.RenderImage(new CardImage("https://x.com/img.png"));

        block.Should().BeOfType<SlackImageBlock>()
            .Which.AltText.Should().Be("image");
    }

    [Fact]
    public void RenderImage_NoTitle_TitlePropertyIsNull()
    {
        var block = _renderer.RenderImage(new CardImage("https://x.com/img.png", "alt"));

        block.Should().BeOfType<SlackImageBlock>()
            .Which.Title.Should().BeNull();
    }

    // ── RenderDivider ────────────────────────────────────────────────────────

    [Fact]
    public void RenderDivider_ReturnsSlackDividerBlock()
    {
        _renderer.RenderDivider(new CardDivider())
            .Should().BeOfType<SlackDividerBlock>();
    }

    // ── RenderActions ────────────────────────────────────────────────────────

    [Fact]
    public void RenderActions_EmptyActions_ReturnsEmptyArray()
    {
        _renderer.RenderActions(new CardActions([])).Should().BeEmpty();
    }

    [Fact]
    public void RenderActions_SingleButton_EmitsActionsBlock()
    {
        var blocks = _renderer.RenderActions(new CardActions(
        [
            new CardButton("btn-1", "Click me")
        ]));

        blocks.Should().HaveCount(1);
        var actions = blocks[0].Should().BeOfType<SlackActionsBlock>().Subject;
        actions.Elements.Should().HaveCount(1);
        actions.Elements[0].Text.Text.Should().Be("Click me");
        actions.Elements[0].ActionId.Should().Be("btn-1");
    }

    [Fact]
    public void RenderActions_PrimaryStyle_Preserved()
    {
        var blocks = _renderer.RenderActions(new CardActions([new CardButton("b", "B", Style: "primary")]));
        blocks[0].Should().BeOfType<SlackActionsBlock>()
            .Which.Elements[0].Style.Should().Be("primary");
    }

    [Fact]
    public void RenderActions_DangerStyle_Preserved()
    {
        var blocks = _renderer.RenderActions(new CardActions([new CardButton("b", "B", Style: "danger")]));
        blocks[0].Should().BeOfType<SlackActionsBlock>()
            .Which.Elements[0].Style.Should().Be("danger");
    }

    [Fact]
    public void RenderActions_UnknownStyle_StyleOmitted()
    {
        var blocks = _renderer.RenderActions(new CardActions([new CardButton("b", "B", Style: "secondary")]));
        blocks[0].Should().BeOfType<SlackActionsBlock>()
            .Which.Elements[0].Style.Should().BeNull();
    }

    [Fact]
    public void RenderActions_NullStyle_StyleOmitted()
    {
        var blocks = _renderer.RenderActions(new CardActions([new CardButton("b", "B", Style: null)]));
        blocks[0].Should().BeOfType<SlackActionsBlock>()
            .Which.Elements[0].Style.Should().BeNull();
    }

    [Fact]
    public void RenderActions_ButtonWithUrl_UrlPreserved()
    {
        var blocks = _renderer.RenderActions(new CardActions([new CardButton("b", "B", Url: "https://x.com")]));
        blocks[0].Should().BeOfType<SlackActionsBlock>()
            .Which.Elements[0].Url.Should().Be("https://x.com");
    }

    [Fact]
    public void RenderActions_ButtonWithValue_ValuePreserved()
    {
        var blocks = _renderer.RenderActions(new CardActions([new CardButton("b", "B", Value: "v1")]));
        blocks[0].Should().BeOfType<SlackActionsBlock>()
            .Which.Elements[0].Value.Should().Be("v1");
    }

    [Fact]
    public void RenderActions_MultipleButtons_AllIncluded()
    {
        var blocks = _renderer.RenderActions(new CardActions(
        [
            new CardButton("b1", "One"),
            new CardButton("b2", "Two"),
            new CardButton("b3", "Three"),
        ]));

        blocks.Should().HaveCount(1);
        blocks[0].Should().BeOfType<SlackActionsBlock>()
            .Which.Elements.Should().HaveCount(3);
    }

    [Fact]
    public void RenderActions_NonButtonActions_Filtered()
    {
        // CardSelect is not yet mapped; only CardButton should appear
        var blocks = _renderer.RenderActions(new CardActions(
        [
            new CardSelect("s1", "Pick one", [new CardSelectOption("A", "a")]),
            new CardButton("b1", "Go"),
        ]));

        blocks.Should().HaveCount(1);
        blocks[0].Should().BeOfType<SlackActionsBlock>()
            .Which.Elements.Should().HaveCount(1)
            .And.Subject.Single().ActionId.Should().Be("b1");
    }

    // ── RenderSection ────────────────────────────────────────────────────────

    [Fact]
    public void RenderSection_NoTitleNoChildren_ReturnsEmpty()
    {
        _renderer.RenderSection(new CardSection()).Should().BeEmpty();
    }

    [Fact]
    public void RenderSection_WithTitle_EmitsContextBlockFirst()
    {
        var blocks = _renderer.RenderSection(new CardSection(Title: "Section Title"));

        blocks.Should().HaveCount(1);
        var ctx = blocks[0].Should().BeOfType<SlackContextBlock>().Subject;
        ctx.Elements[0].Should().BeOfType<SlackMrkdwn>()
            .Which.Text.Should().Be("*Section Title*");
    }

    [Fact]
    public void RenderSection_WithChildren_ChildBlocksEmitted()
    {
        var blocks = _renderer.RenderSection(new CardSection(Children: [new CardText("body")]));

        blocks.Should().HaveCount(1);
        blocks[0].Should().BeOfType<SlackSectionBlock>();
    }

    [Fact]
    public void RenderSection_TitleAndChildren_ContextBeforeChildBlocks()
    {
        var blocks = _renderer.RenderSection(new CardSection(
            Title: "T",
            Children: [new CardText("body"), new CardDivider()]));

        blocks.Should().HaveCount(3);
        blocks[0].Should().BeOfType<SlackContextBlock>();  // title
        blocks[1].Should().BeOfType<SlackSectionBlock>();  // CardText
        blocks[2].Should().BeOfType<SlackDividerBlock>();  // CardDivider
    }

    [Fact]
    public void RenderSection_NestedSection_ChildBlocksFlattened()
    {
        var blocks = _renderer.RenderSection(new CardSection(
            Children: [new CardSection(Children: [new CardText("nested")])]));

        blocks.Should().HaveCount(1);
        blocks[0].Should().BeOfType<SlackSectionBlock>();
    }

    // ── RenderFields ─────────────────────────────────────────────────────────

    [Fact]
    public void RenderFields_SingleField_SectionWithFieldsArray()
    {
        var block = _renderer.RenderFields(new CardFields([new CardField("Key", "Val")]));

        var section = block.Should().BeOfType<SlackSectionBlock>().Subject;
        section.Fields.Should().HaveCount(1);
        section.Fields![0].Should().BeOfType<SlackMrkdwn>()
            .Which.Text.Should().Be("*Key*\nVal");
    }

    [Fact]
    public void RenderFields_MultipleFields_AllIncluded()
    {
        var block = _renderer.RenderFields(new CardFields(
        [
            new CardField("A", "1"),
            new CardField("B", "2"),
            new CardField("C", "3"),
        ]));

        block.Should().BeOfType<SlackSectionBlock>()
            .Which.Fields.Should().HaveCount(3);
    }

    [Fact]
    public void RenderFields_FieldFormat_LabelBoldedValueOnNextLine()
    {
        var block = _renderer.RenderFields(new CardFields(
        [
            new CardField("Status", "Active"),
            new CardField("Owner", "Alice"),
        ]));

        var section = block.Should().BeOfType<SlackSectionBlock>().Subject;
        section.Fields![0].Should().BeOfType<SlackMrkdwn>().Which.Text.Should().Be("*Status*\nActive");
        section.Fields![1].Should().BeOfType<SlackMrkdwn>().Which.Text.Should().Be("*Owner*\nAlice");
    }

    [Fact]
    public void RenderFields_NoTextProperty_OnlyFieldsPopulated()
    {
        var block = _renderer.RenderFields(new CardFields([new CardField("K", "V")]));

        block.Should().BeOfType<SlackSectionBlock>()
            .Which.Text.Should().BeNull();
    }

    // ── RenderLink ───────────────────────────────────────────────────────────

    [Fact]
    public void RenderLink_EmitsSlackLinkFormatInSection()
    {
        var block = _renderer.RenderLink(new CardLink("click here", "https://example.com"));

        var section = block.Should().BeOfType<SlackSectionBlock>().Subject;
        section.Text.Should().BeOfType<SlackMrkdwn>()
            .Which.Text.Should().Be("<https://example.com|click here>");
    }

    // ── Render (entry point — end-to-end) ────────────────────────────────────

    [Fact]
    public void Render_RealisticCard_FullSnapshot()
    {
        var card = new CardElement(
            Title: "Search Results",
            Subtitle: "3 items found",
            Children:
            [
                new CardText("Found 3 matching documents"),
                new CardFields(
                [
                    new CardField("Title", "Q4 Report"),
                    new CardField("Date",  "2025-01-15"),
                ]),
                new CardDivider(),
                new CardActions(
                [
                    new CardButton("open", "Open", Value: "doc_123", Style: "primary"),
                    new CardButton("dismiss", "Dismiss"),
                ]),
            ]);

        var blocks = _renderer.RenderCard(card);

        // Exact block sequence: header + context + text + fields + divider + actions = 6
        blocks.Should().HaveCount(6);
        blocks[0].Should().BeOfType<SlackHeaderBlock>()
            .Which.Text.Text.Should().Be("Search Results");
        blocks[1].Should().BeOfType<SlackContextBlock>();  // subtitle
        blocks[2].Should().BeOfType<SlackSectionBlock>();  // CardText
        blocks[3].Should().BeOfType<SlackSectionBlock>();  // CardFields
        blocks[4].Should().BeOfType<SlackDividerBlock>();  // CardDivider
        blocks[5].Should().BeOfType<SlackActionsBlock>()   // CardActions
            .Which.Elements.Should().HaveCount(2);
    }

    [Fact]
    public void Render_CardWithActions_ActionsBlockIncluded()
    {
        var card = new CardElement(Children:
        [
            new CardText("Hello"),
            new CardActions([new CardButton("b", "Go")]),
        ]);

        var blocks = _renderer.RenderCard(card);

        blocks.Should().HaveCount(2);
        blocks[0].Should().BeOfType<SlackSectionBlock>();
        blocks[1].Should().BeOfType<SlackActionsBlock>();
    }
}
