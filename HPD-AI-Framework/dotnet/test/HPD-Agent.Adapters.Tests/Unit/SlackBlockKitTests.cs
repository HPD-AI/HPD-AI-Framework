using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <c>SlackBlockKit.cs</c> — verifies JSON serialization shape for every Block Kit
/// type. Uses the default <see cref="JsonSerializer"/> with <c>WhenWritingNull</c> so optional
/// fields are omitted, matching what Slack expects.
/// </summary>
public class SlackBlockKitTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static JsonElement Serialize<T>(T obj) =>
        JsonDocument.Parse(JsonSerializer.Serialize(obj, JsonOptions)).RootElement;

    // ── SlackSectionBlock ─────────────────────────────────────────────

    [Fact]
    public void SlackSectionBlock_WithText_TypeIsSection()
    {
        var block = new SlackSectionBlock(Text: new SlackMrkdwn("Hello"));

        Serialize(block).GetProperty("type").GetString().Should().Be("section");
    }

    [Fact]
    public void SlackSectionBlock_WithExpand_EmitsExpandTrue()
    {
        var block = new SlackSectionBlock(Text: new SlackMrkdwn("text"), Expand: true);

        Serialize(block).GetProperty("expand").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void SlackSectionBlock_WithFields_EmitsFieldsArray()
    {
        var block = new SlackSectionBlock(Fields: [new SlackMrkdwn("*Key*\nValue")]);

        var fields = Serialize(block).GetProperty("fields");

        fields.GetArrayLength().Should().Be(1);
        fields[0].GetProperty("type").GetString().Should().Be("mrkdwn");
    }

    // ── SlackActionsBlock ─────────────────────────────────────────────

    [Fact]
    public void SlackActionsBlock_WithButtons_EmitsElementsArray()
    {
        var block = new SlackActionsBlock(Elements: [
            new SlackButton(ActionId: "a1", Text: new SlackPlainText("Go"), Value: "go"),
        ]);

        var elements = Serialize(block).GetProperty("elements");

        elements.GetArrayLength().Should().Be(1);
        elements[0].GetProperty("type").GetString().Should().Be("button");
        elements[0].GetProperty("action_id").GetString().Should().Be("a1");
    }

    // ── SlackButton styles ────────────────────────────────────────────

    [Fact]
    public void SlackButton_StylePrimary_EmitsStyleField()
    {
        var button = new SlackButton(ActionId: "b", Text: new SlackPlainText("OK"), Style: "primary");

        Serialize(button).GetProperty("style").GetString().Should().Be("primary");
    }

    [Fact]
    public void SlackButton_StyleDanger_EmitsStyleField()
    {
        var button = new SlackButton(ActionId: "b", Text: new SlackPlainText("No"), Style: "danger");

        Serialize(button).GetProperty("style").GetString().Should().Be("danger");
    }

    [Fact]
    public void SlackButton_StyleNull_OmitsStyleField()
    {
        var button = new SlackButton(ActionId: "b", Text: new SlackPlainText("OK"));

        Serialize(button).TryGetProperty("style", out _).Should().BeFalse();
    }

    [Fact]
    public void SlackButton_Value_IsPreserved()
    {
        var button = new SlackButton(ActionId: "b", Text: new SlackPlainText("Go"), Value: "go-value");

        Serialize(button).GetProperty("value").GetString().Should().Be("go-value");
    }

    // ── Block type discriminators ─────────────────────────────────────

    [Fact]
    public void SlackHeaderBlock_TypeIsHeader()
    {
        var block = new SlackHeaderBlock(Text: new SlackPlainText("Title"));

        var el = Serialize(block);

        el.GetProperty("type").GetString().Should().Be("header");
        el.GetProperty("text").GetProperty("text").GetString().Should().Be("Title");
    }

    [Fact]
    public void SlackContextBlock_TypeIsContext()
    {
        var block = new SlackContextBlock(Elements: [new SlackMrkdwn("secondary")]);

        var el = Serialize(block);

        el.GetProperty("type").GetString().Should().Be("context");
        el.GetProperty("elements").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void SlackImageBlock_TypeIsImage_AllFieldsPresent()
    {
        var block = new SlackImageBlock(
            ImageUrl: "https://example.com/img.png",
            AltText:  "A chart");

        var el = Serialize(block);

        el.GetProperty("type").GetString().Should().Be("image");
        el.GetProperty("image_url").GetString().Should().Be("https://example.com/img.png");
        el.GetProperty("alt_text").GetString().Should().Be("A chart");
    }

    [Fact]
    public void SlackDividerBlock_TypeIsDivider_MinimalShape()
    {
        var block = new SlackDividerBlock();

        Serialize(block).GetProperty("type").GetString().Should().Be("divider");
    }

    // ── Text objects ──────────────────────────────────────────────────

    [Fact]
    public void SlackPlainText_EmojiDefaultsTrue()
    {
        var text = new SlackPlainText("Hello");

        text.Emoji.Should().BeTrue();
        Serialize(text).GetProperty("emoji").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void SlackMrkdwn_TypeIsMrkdwn()
    {
        var text = new SlackMrkdwn("*bold*");

        var el = Serialize(text);

        el.GetProperty("type").GetString().Should().Be("mrkdwn");
        el.GetProperty("text").GetString().Should().Be("*bold*");
    }

    // ── Polymorphic block array ───────────────────────────────────────

    [Fact]
    public void PolymorphicBlockArray_EachBlockHasCorrectTypeDiscriminator()
    {
        var blocks = new SlackBlock[]
        {
            new SlackSectionBlock(Text: new SlackMrkdwn("text")),
            new SlackActionsBlock(Elements: [new SlackButton("a", new SlackPlainText("Go"))]),
            new SlackHeaderBlock(Text: new SlackPlainText("Title")),
            new SlackContextBlock(Elements: [new SlackMrkdwn("hint")]),
            new SlackDividerBlock(),
        };

        var arr = JsonDocument.Parse(JsonSerializer.Serialize(blocks)).RootElement;

        arr[0].GetProperty("type").GetString().Should().Be("section");
        arr[1].GetProperty("type").GetString().Should().Be("actions");
        arr[2].GetProperty("type").GetString().Should().Be("header");
        arr[3].GetProperty("type").GetString().Should().Be("context");
        arr[4].GetProperty("type").GetString().Should().Be("divider");
    }

    // ── BlockId ───────────────────────────────────────────────────────

    [Fact]
    public void SlackSectionBlock_BlockIdSet_AppearsInJson()
    {
        var block = new SlackSectionBlock(BlockId: "my-block");

        Serialize(block).GetProperty("block_id").GetString().Should().Be("my-block");
    }

    [Fact]
    public void SlackSectionBlock_BlockIdNull_KeyAbsent()
    {
        var block = new SlackSectionBlock();

        Serialize(block).TryGetProperty("block_id", out _).Should().BeFalse();
    }

    // ── SlackOption ───────────────────────────────────────────────────

    [Fact]
    public void SlackOption_WithDescription_DescriptionPresent()
    {
        var option = new SlackOption(
            Text:        new SlackPlainText("Option A"),
            Value:       "a",
            Description: new SlackPlainText("Helpful hint"));

        Serialize(option)
            .GetProperty("description")
            .GetProperty("text").GetString()
            .Should().Be("Helpful hint");
    }

    [Fact]
    public void SlackOption_WithoutDescription_DescriptionAbsent()
    {
        var option = new SlackOption(
            Text:  new SlackPlainText("Option A"),
            Value: "a");

        Serialize(option).TryGetProperty("description", out _).Should().BeFalse();
    }

    // ── SlackConfirmationDialog ───────────────────────────────────────

    [Fact]
    public void SlackConfirmationDialog_Serializes_AllMandatoryFields()
    {
        var dialog = new SlackConfirmationDialog(
            Title:   new SlackPlainText("Are you sure?"),
            Text:    new SlackMrkdwn("This cannot be undone."),
            Confirm: new SlackPlainText("Yes"),
            Deny:    new SlackPlainText("Cancel"));

        var el = Serialize(dialog);

        el.GetProperty("title").GetProperty("text").GetString().Should().Be("Are you sure?");
        el.GetProperty("confirm").GetProperty("text").GetString().Should().Be("Yes");
        el.GetProperty("deny").GetProperty("text").GetString().Should().Be("Cancel");
        el.GetProperty("text").GetProperty("text").GetString().Should().Be("This cannot be undone.");
    }
}
