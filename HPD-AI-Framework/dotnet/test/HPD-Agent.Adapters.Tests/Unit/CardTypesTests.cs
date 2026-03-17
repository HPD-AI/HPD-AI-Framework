using FluentAssertions;
using HPD.Agent.Adapters.Cards;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for the card type discriminated union in <c>CardTypes.cs</c>.
/// Covers construction, record equality, defaults, and inheritance.
/// </summary>
public class CardTypesTests
{
    // ── CardText ──────────────────────────────────────────────────────

    [Fact]
    public void CardText_RecordEquality_SameValues_AreEqual()
    {
        var a = new CardText("Hello", "muted");
        var b = new CardText("Hello", "muted");

        a.Should().Be(b);
    }

    [Fact]
    public void CardText_RecordEquality_DifferentValues_AreNotEqual()
    {
        var a = new CardText("Hello");
        var b = new CardText("World");

        a.Should().NotBe(b);
    }

    [Fact]
    public void CardText_DefaultStyle_IsNull()
    {
        var text = new CardText("hello");

        text.Style.Should().BeNull();
    }

    [Fact]
    public void CardText_IsCardChild()
    {
        CardChild child = new CardText("test");

        child.Should().BeOfType<CardText>();
    }

    // ── CardField / CardFields ────────────────────────────────────────

    [Fact]
    public void CardField_ConstructsCorrectly()
    {
        var field = new CardField("Author", "Alice");

        field.Label.Should().Be("Author");
        field.Value.Should().Be("Alice");
    }

    [Fact]
    public void CardFields_IsCardChild()
    {
        CardChild child = new CardFields([new CardField("K", "V")]);

        child.Should().BeOfType<CardFields>();
    }

    [Fact]
    public void CardFields_FieldsAccessible()
    {
        var fields = new CardFields([new CardField("A", "1"), new CardField("B", "2")]);

        fields.Fields.Should().HaveCount(2);
        fields.Fields[0].Label.Should().Be("A");
        fields.Fields[1].Label.Should().Be("B");
    }

    // ── CardLink ──────────────────────────────────────────────────────

    [Fact]
    public void CardLink_ConstructsCorrectly()
    {
        var link = new CardLink("Open", "https://example.com");

        link.Label.Should().Be("Open");
        link.Url.Should().Be("https://example.com");
    }

    // ── CardImage ─────────────────────────────────────────────────────

    [Fact]
    public void CardImage_DefaultsAltTextAndTitleToNull()
    {
        var img = new CardImage("https://example.com/img.png");

        img.AltText.Should().BeNull();
        img.Title.Should().BeNull();
    }

    [Fact]
    public void CardImage_ExplicitAltText()
    {
        var img = new CardImage("https://example.com/img.png", AltText: "Chart");

        img.AltText.Should().Be("Chart");
    }

    // ── CardDivider ───────────────────────────────────────────────────

    [Fact]
    public void CardDivider_IsCardChild()
    {
        CardChild child = new CardDivider();

        child.Should().BeOfType<CardDivider>();
    }

    // ── CardActions ───────────────────────────────────────────────────

    [Fact]
    public void CardActions_IsCardChild()
    {
        CardChild child = new CardActions([]);

        child.Should().BeOfType<CardActions>();
    }

    [Fact]
    public void CardActions_ActionsAccessible()
    {
        var button  = new CardButton("id", "Click");
        var actions = new CardActions([button]);

        actions.Actions.Should().ContainSingle()
            .Which.Should().Be(button);
    }

    // ── CardSection ───────────────────────────────────────────────────

    [Fact]
    public void CardSection_DefaultsToNull()
    {
        var section = new CardSection();

        section.Title.Should().BeNull();
        section.Children.Should().BeNull();
    }

    [Fact]
    public void CardSection_WithChildren()
    {
        var section = new CardSection(Children: [new CardText("text"), new CardDivider()]);

        section.Children.Should().HaveCount(2);
        section.Children![0].Should().BeOfType<CardText>();
        section.Children[1].Should().BeOfType<CardDivider>();
    }

    // ── CardButton ────────────────────────────────────────────────────

    [Fact]
    public void CardButton_DefaultOptionalsAreNull()
    {
        var btn = new CardButton("action-id", "Click me");

        btn.ActionId.Should().Be("action-id");
        btn.Label.Should().Be("Click me");
        btn.Value.Should().BeNull();
        btn.Style.Should().BeNull();
        btn.Url.Should().BeNull();
    }

    [Fact]
    public void CardButton_IsCardAction()
    {
        CardAction action = new CardButton("id", "Label");

        action.Should().BeOfType<CardButton>();
    }

    // ── CardSelect ────────────────────────────────────────────────────

    [Fact]
    public void CardSelect_ConstructsWithOptions()
    {
        var options = new List<CardSelectOption>
        {
            new("Option A", "a"),
            new("Option B", "b"),
        };
        var select = new CardSelect("sel", "Choose…", options);

        select.Options.Should().HaveCount(2);
        select.InitialValue.Should().BeNull();
    }

    [Fact]
    public void CardSelectOption_ConstructsCorrectly()
    {
        var opt = new CardSelectOption("Label", "value");

        opt.Label.Should().Be("Label");
        opt.Value.Should().Be("value");
    }

    // ── CardRadioSelect ───────────────────────────────────────────────

    [Fact]
    public void CardRadioSelect_IsCardAction()
    {
        CardAction action = new CardRadioSelect("id", "Pick one", []);

        action.Should().BeOfType<CardRadioSelect>();
    }

    // ── CardElement (root) ────────────────────────────────────────────

    [Fact]
    public void CardElement_DefaultsAllNull()
    {
        var card = new CardElement();

        card.Title.Should().BeNull();
        card.Subtitle.Should().BeNull();
        card.ImageUrl.Should().BeNull();
        card.Children.Should().BeNull();
    }

    [Fact]
    public void CardElement_WithExpression_PreservesOriginal()
    {
        var original = new CardElement(Title: "Original");
        var copy     = original with { Title = "Copy" };

        original.Title.Should().Be("Original");
        copy.Title.Should().Be("Copy");
    }

    [Fact]
    public void CardElement_RecordEquality_SameValues()
    {
        var a = new CardElement(Title: "T", Subtitle: "S");
        var b = new CardElement(Title: "T", Subtitle: "S");

        a.Should().Be(b);
    }

    // ── Polymorphism ──────────────────────────────────────────────────

    [Fact]
    public void CardChild_IsAbstractBase_AllSubtypesAreCardChild()
    {
        var children = new CardChild[]
        {
            new CardText("t"),
            new CardFields([]),
            new CardLink("l", "u"),
            new CardImage("url"),
            new CardDivider(),
            new CardActions([]),
            new CardSection(),
        };

        children.Should().AllBeAssignableTo<CardChild>();
    }

    [Fact]
    public void CardAction_IsAbstractBase_AllSubtypesAreCardAction()
    {
        var actions = new CardAction[]
        {
            new CardButton("id", "l"),
            new CardSelect("id", "ph", []),
            new CardRadioSelect("id", "ph", []),
        };

        actions.Should().AllBeAssignableTo<CardAction>();
    }
}
