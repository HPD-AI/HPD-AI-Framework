using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <c>SlackModalTypes.cs</c> — verifies JSON serialization shape for
/// <see cref="SlackModalView"/> and the polymorphic <see cref="SlackInputElement"/> hierarchy.
/// </summary>
public class SlackModalTypesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static JsonElement Serialize<T>(T obj) =>
        JsonDocument.Parse(JsonSerializer.Serialize(obj, JsonOptions)).RootElement;

    // ── SlackModalView ────────────────────────────────────────────────

    [Fact]
    public void SlackModalView_Serializes_AllMandatoryAndOptionalFields()
    {
        var view = new SlackModalView(
            Type:            "modal",
            Title:           new SlackPlainText("My Dialog"),
            Blocks:          [],
            Submit:          new SlackPlainText("Submit"),
            Close:           new SlackPlainText("Cancel"),
            CallbackId:      "my-callback",
            PrivateMetadata: "abc123",
            NotifyOnClose:   true);

        var el = Serialize(view);

        el.GetProperty("type").GetString().Should().Be("modal");
        el.GetProperty("title").GetProperty("text").GetString().Should().Be("My Dialog");
        el.GetProperty("submit").GetProperty("text").GetString().Should().Be("Submit");
        el.GetProperty("close").GetProperty("text").GetString().Should().Be("Cancel");
        el.GetProperty("callback_id").GetString().Should().Be("my-callback");
        el.GetProperty("private_metadata").GetString().Should().Be("abc123");
        el.GetProperty("notify_on_close").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void SlackModalView_OptionalFieldsAbsent_WhenNull()
    {
        var view = new SlackModalView(
            Type:   "modal",
            Title:  new SlackPlainText("Dialog"),
            Blocks: []);

        var el = Serialize(view);

        el.TryGetProperty("submit", out _).Should().BeFalse();
        el.TryGetProperty("close", out _).Should().BeFalse();
        el.TryGetProperty("callback_id", out _).Should().BeFalse();
        el.TryGetProperty("private_metadata", out _).Should().BeFalse();
    }

    // ── SlackModalInputBlock ──────────────────────────────────────────

    [Fact]
    public void SlackModalInputBlock_TypeIsInput()
    {
        var block = new SlackModalInputBlock(
            Label:   new SlackPlainText("Name"),
            Element: new SlackPlainTextInput(ActionId: "name-input"));

        var el = Serialize(block);

        el.GetProperty("type").GetString().Should().Be("input");
        el.GetProperty("label").GetProperty("text").GetString().Should().Be("Name");
    }

    [Fact]
    public void SlackModalInputBlock_OptionalTrue_EmitsOptional()
    {
        var block = new SlackModalInputBlock(
            Label:    new SlackPlainText("Note"),
            Element:  new SlackPlainTextInput(ActionId: "note"),
            Optional: true);

        Serialize(block).GetProperty("optional").GetBoolean().Should().BeTrue();
    }

    // ── SlackPlainTextInput ───────────────────────────────────────────

    [Fact]
    public void SlackPlainTextInput_TypeIsPlainTextInput()
    {
        var input = new SlackPlainTextInput(
            ActionId:     "name",
            Multiline:    true,
            InitialValue: "hello");

        var el = Serialize<SlackInputElement>(input);

        el.GetProperty("type").GetString().Should().Be("plain_text_input");
        el.GetProperty("multiline").GetBoolean().Should().BeTrue();
        el.GetProperty("initial_value").GetString().Should().Be("hello");
    }

    [Fact]
    public void SlackPlainTextInput_MultilineNull_OmitsMultiline()
    {
        var input = new SlackPlainTextInput(ActionId: "field");

        var el = Serialize<SlackInputElement>(input);

        el.TryGetProperty("multiline", out _).Should().BeFalse();
    }

    [Fact]
    public void SlackPlainTextInput_MinAndMaxLength_Serialized()
    {
        var input = new SlackPlainTextInput(ActionId: "field", MinLength: 3, MaxLength: 100);

        var el = Serialize<SlackInputElement>(input);

        el.GetProperty("min_length").GetInt32().Should().Be(3);
        el.GetProperty("max_length").GetInt32().Should().Be(100);
    }

    // ── SlackStaticSelect ─────────────────────────────────────────────

    [Fact]
    public void SlackStaticSelect_TypeIsStaticSelect()
    {
        var select = new SlackStaticSelect(
            ActionId: "color",
            Options:  [new SlackOption(new SlackPlainText("Red"), "red")]);

        var el = Serialize<SlackInputElement>(select);

        el.GetProperty("type").GetString().Should().Be("static_select");
        el.GetProperty("options").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void SlackStaticSelect_WithInitialOption_EmitsInitialOption()
    {
        var initialOption = new SlackOption(new SlackPlainText("Blue"), "blue");
        var select = new SlackStaticSelect(
            ActionId:      "color",
            Options:       [initialOption],
            InitialOption: initialOption);

        var el = Serialize<SlackInputElement>(select);

        el.GetProperty("initial_option").GetProperty("value").GetString().Should().Be("blue");
    }

    // ── SlackRadioButtons ─────────────────────────────────────────────

    [Fact]
    public void SlackRadioButtons_TypeIsRadioButtons()
    {
        var radios = new SlackRadioButtons(
            ActionId: "choice",
            Options:  [new SlackOption(new SlackPlainText("A"), "a")]);

        var el = Serialize<SlackInputElement>(radios);

        el.GetProperty("type").GetString().Should().Be("radio_buttons");
    }

    // ── Polymorphic SlackInputElement array ───────────────────────────

    [Fact]
    public void SlackInputElement_PolymorphicArray_DiscriminatesCorrectly()
    {
        var elements = new SlackInputElement[]
        {
            new SlackPlainTextInput(ActionId: "t"),
            new SlackStaticSelect(ActionId: "s", Options: []),
            new SlackRadioButtons(ActionId: "r", Options: []),
        };

        var arr = JsonDocument.Parse(JsonSerializer.Serialize(elements)).RootElement;

        arr[0].GetProperty("type").GetString().Should().Be("plain_text_input");
        arr[1].GetProperty("type").GetString().Should().Be("static_select");
        arr[2].GetProperty("type").GetString().Should().Be("radio_buttons");
    }
}
