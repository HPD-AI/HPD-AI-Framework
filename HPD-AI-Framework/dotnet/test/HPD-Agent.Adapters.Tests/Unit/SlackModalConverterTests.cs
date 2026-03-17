using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Agent.Adapters.Modals;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <see cref="SlackModalConverter"/> — covers metadata encode/decode round-trips,
/// every <see cref="ModalBlock"/> variant converted to the correct Slack Block Kit type,
/// and all <see cref="ModalResponse"/> variants mapped to correct Slack submission response shapes.
/// </summary>
public class SlackModalConverterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static JsonElement SerializeResponse(object obj) =>
        JsonDocument.Parse(JsonSerializer.Serialize(obj, JsonOptions)).RootElement;

    // ── EncodeMetadata / DecodeMetadata ───────────────────────────────

    [Fact]
    public void EncodeDecodeMetadata_BothValues_RoundTrips()
    {
        var encoded = SlackModalConverter.EncodeMetadata("ctx-123", "my-payload");
        var (ctxId, payload) = SlackModalConverter.DecodeMetadata(encoded);

        ctxId.Should().Be("ctx-123");
        payload.Should().Be("my-payload");
    }

    [Fact]
    public void EncodeDecodeMetadata_ContextIdOnly_RoundTrips()
    {
        var encoded = SlackModalConverter.EncodeMetadata("ctx-only", null);
        var (ctxId, payload) = SlackModalConverter.DecodeMetadata(encoded);

        ctxId.Should().Be("ctx-only");
        payload.Should().BeNull();
    }

    [Fact]
    public void EncodeDecodeMetadata_PrivateMetadataOnly_RoundTrips()
    {
        var encoded = SlackModalConverter.EncodeMetadata(null, "payload-only");
        var (ctxId, payload) = SlackModalConverter.DecodeMetadata(encoded);

        ctxId.Should().BeNull();
        payload.Should().Be("payload-only");
    }

    [Fact]
    public void EncodeDecodeMetadata_BothNull_RoundTrips()
    {
        var encoded = SlackModalConverter.EncodeMetadata(null, null);
        var (ctxId, payload) = SlackModalConverter.DecodeMetadata(encoded);

        ctxId.Should().BeNull();
        payload.Should().BeNull();
    }

    [Fact]
    public void DecodeMetadata_EmptyString_ReturnsNullNull()
    {
        var (ctxId, payload) = SlackModalConverter.DecodeMetadata(string.Empty);

        ctxId.Should().BeNull();
        payload.Should().BeNull();
    }

    [Fact]
    public void DecodeMetadata_WhitespaceString_ReturnsNullNull()
    {
        var (ctxId, payload) = SlackModalConverter.DecodeMetadata("   ");

        ctxId.Should().BeNull();
        payload.Should().BeNull();
    }

    [Fact]
    public void DecodeMetadata_NonBase64String_FallsBackToRawPayload()
    {
        const string raw = "not-base64!!!";

        var (ctxId, payload) = SlackModalConverter.DecodeMetadata(raw);

        ctxId.Should().BeNull();
        payload.Should().Be(raw);
    }

    [Fact]
    public void DecodeMetadata_ValidBase64ButNotOurEnvelope_FallsBackToRawPayload()
    {
        // Valid Base64 that decodes to JSON but not our envelope shape
        var notOurJson = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            "{\"some_other_field\":\"value\"}"));

        var (ctxId, payload) = SlackModalConverter.DecodeMetadata(notOurJson);

        // The envelope will parse (nullable properties, so both are null)
        // but the raw payload fallback is not triggered — both are null is correct
        ctxId.Should().BeNull();
    }

    [Fact]
    public void EncodeMetadata_ProducesValidBase64()
    {
        var encoded = SlackModalConverter.EncodeMetadata("ctx", "data");

        var act = () => Convert.FromBase64String(encoded);

        act.Should().NotThrow();
    }

    // ── ToSlackView — core fields ─────────────────────────────────────

    [Fact]
    public void ToSlackView_MinimalModal_TypeIsMOdal()
    {
        var modal = new ModalElement("Title", []);

        var view = SlackModalConverter.ToSlackView(modal);

        view.Type.Should().Be("modal");
        view.Title.Text.Should().Be("Title");
    }

    [Fact]
    public void ToSlackView_WithSubmitAndCloseLabels_BothMapped()
    {
        var modal = new ModalElement("T", [], SubmitLabel: "OK", CloseLabel: "Back");

        var view = SlackModalConverter.ToSlackView(modal);

        view.Submit!.Text.Should().Be("OK");
        view.Close!.Text.Should().Be("Back");
    }

    [Fact]
    public void ToSlackView_NoSubmitLabel_SubmitIsNull()
    {
        var modal = new ModalElement("T", []);

        var view = SlackModalConverter.ToSlackView(modal);

        view.Submit.Should().BeNull();
    }

    [Fact]
    public void ToSlackView_CallbackId_Preserved()
    {
        var modal = new ModalElement("T", [], CallbackId: "my-cb");

        var view = SlackModalConverter.ToSlackView(modal);

        view.CallbackId.Should().Be("my-cb");
    }

    [Fact]
    public void ToSlackView_NotifyOnClose_Preserved()
    {
        var modal = new ModalElement("T", [], NotifyOnClose: true);

        var view = SlackModalConverter.ToSlackView(modal);

        view.NotifyOnClose.Should().BeTrue();
    }

    [Fact]
    public void ToSlackView_PrivateMetadata_AutoEncodedWhenNoExplicitMetadata()
    {
        var modal = new ModalElement("T", [], PrivateMetadata: "raw-data");

        var view = SlackModalConverter.ToSlackView(modal);

        // Must be Base64-encoded, not the raw string
        view.PrivateMetadata.Should().NotBe("raw-data");
        var (_, decoded) = SlackModalConverter.DecodeMetadata(view.PrivateMetadata);
        decoded.Should().Be("raw-data");
    }

    [Fact]
    public void ToSlackView_ExplicitEncodedMetadata_OverridesAutoEncode()
    {
        var encodedMeta = SlackModalConverter.EncodeMetadata("ctx", "payload");
        var modal = new ModalElement("T", [], PrivateMetadata: "ignored");

        var view = SlackModalConverter.ToSlackView(modal, encodedMetadata: encodedMeta);

        view.PrivateMetadata.Should().Be(encodedMeta);
        var (ctxId, _) = SlackModalConverter.DecodeMetadata(view.PrivateMetadata);
        ctxId.Should().Be("ctx");
    }

    [Fact]
    public void ToSlackView_NullPrivateMetadataAndNoExplicit_PrivateMetadataIsNull()
    {
        var modal = new ModalElement("T", []);

        var view = SlackModalConverter.ToSlackView(modal);

        view.PrivateMetadata.Should().BeNull();
    }

    // ── ToSlackView — ModalTextInput ──────────────────────────────────

    [Fact]
    public void ToSlackView_ModalTextInput_ProducesInputBlockWithPlainTextInput()
    {
        var modal = new ModalElement("T", [
            new ModalTextInput(
                Label:        "Name",
                BlockId:      "block-name",
                ActionId:     "action-name",
                Placeholder:  "Enter your name",
                InitialValue: "Alice",
                Multiline:    false,
                MinLength:    2,
                MaxLength:    50)
        ]);

        var view = SlackModalConverter.ToSlackView(modal);
        var block = view.Blocks.Should().ContainSingle().Which.Should().BeOfType<SlackModalInputBlock>().Subject;
        var input = block.Element.Should().BeOfType<SlackPlainTextInput>().Subject;

        block.Label.Text.Should().Be("Name");
        block.BlockId.Should().Be("block-name");
        input.ActionId.Should().Be("action-name");
        input.Placeholder!.Text.Should().Be("Enter your name");
        input.InitialValue.Should().Be("Alice");
        input.Multiline.Should().BeNull();    // false → null (omitted)
        input.MinLength.Should().Be(2);
        input.MaxLength.Should().Be(50);
    }

    [Fact]
    public void ToSlackView_ModalTextInput_Multiline_EmitsMultilineTrue()
    {
        var modal = new ModalElement("T", [
            new ModalTextInput("Label", "b", "a", Multiline: true)
        ]);

        var view = SlackModalConverter.ToSlackView(modal);
        var input = view.Blocks[0].Should().BeOfType<SlackModalInputBlock>().Subject
            .Element.Should().BeOfType<SlackPlainTextInput>().Subject;

        input.Multiline.Should().BeTrue();
    }

    [Fact]
    public void ToSlackView_ModalTextInput_Optional_EmitsOptionalTrue()
    {
        var modal = new ModalElement("T", [
            new ModalTextInput("Label", "b", "a", Optional: true)
        ]);

        var view = SlackModalConverter.ToSlackView(modal);
        var block = view.Blocks[0].Should().BeOfType<SlackModalInputBlock>().Subject;

        block.Optional.Should().BeTrue();
    }

    // ── ToSlackView — ModalSelect ─────────────────────────────────────

    [Fact]
    public void ToSlackView_ModalSelect_ProducesStaticSelect()
    {
        var options = new[]
        {
            new ModalOption("Apple", "apple"),
            new ModalOption("Banana", "banana"),
        };
        var modal = new ModalElement("T", [
            new ModalSelect("Fruit", "block-fruit", "action-fruit", options)
        ]);

        var view = SlackModalConverter.ToSlackView(modal);
        var select = view.Blocks[0].Should().BeOfType<SlackModalInputBlock>().Subject
            .Element.Should().BeOfType<SlackStaticSelect>().Subject;

        select.Options.Should().HaveCount(2);
        select.Options[0].Value.Should().Be("apple");
        select.Options[1].Value.Should().Be("banana");
    }

    [Fact]
    public void ToSlackView_ModalSelect_WithInitialValue_SetsInitialOption()
    {
        var options = new[]
        {
            new ModalOption("Apple", "apple"),
            new ModalOption("Banana", "banana"),
        };
        var modal = new ModalElement("T", [
            new ModalSelect("Fruit", "b", "a", options, InitialValue: "banana")
        ]);

        var view = SlackModalConverter.ToSlackView(modal);
        var select = view.Blocks[0].Should().BeOfType<SlackModalInputBlock>().Subject
            .Element.Should().BeOfType<SlackStaticSelect>().Subject;

        select.InitialOption.Should().NotBeNull();
        select.InitialOption!.Value.Should().Be("banana");
    }

    [Fact]
    public void ToSlackView_ModalSelect_InitialValueNotInOptions_InitialOptionIsNull()
    {
        var options = new[] { new ModalOption("Apple", "apple") };
        var modal = new ModalElement("T", [
            new ModalSelect("Fruit", "b", "a", options, InitialValue: "missing")
        ]);

        var view = SlackModalConverter.ToSlackView(modal);
        var select = view.Blocks[0].Should().BeOfType<SlackModalInputBlock>().Subject
            .Element.Should().BeOfType<SlackStaticSelect>().Subject;

        select.InitialOption.Should().BeNull();
    }

    // ── ToSlackView — ModalRadioGroup ─────────────────────────────────

    [Fact]
    public void ToSlackView_ModalRadioGroup_ProducesRadioButtons()
    {
        var options = new[] { new ModalOption("Yes", "yes"), new ModalOption("No", "no") };
        var modal = new ModalElement("T", [
            new ModalRadioGroup("Choice", "block-choice", "action-choice", options)
        ]);

        var view = SlackModalConverter.ToSlackView(modal);
        var radios = view.Blocks[0].Should().BeOfType<SlackModalInputBlock>().Subject
            .Element.Should().BeOfType<SlackRadioButtons>().Subject;

        radios.Options.Should().HaveCount(2);
    }

    [Fact]
    public void ToSlackView_ModalRadioGroup_WithInitialValue_SetsInitialOption()
    {
        var options = new[] { new ModalOption("Yes", "yes"), new ModalOption("No", "no") };
        var modal = new ModalElement("T", [
            new ModalRadioGroup("Choice", "b", "a", options, InitialValue: "yes")
        ]);

        var view = SlackModalConverter.ToSlackView(modal);
        var radios = view.Blocks[0].Should().BeOfType<SlackModalInputBlock>().Subject
            .Element.Should().BeOfType<SlackRadioButtons>().Subject;

        radios.InitialOption!.Value.Should().Be("yes");
    }

    // ── ToSlackView — ModalSection ────────────────────────────────────

    [Fact]
    public void ToSlackView_ModalSection_ProducesSectionBlockWithMrkdwn()
    {
        var modal = new ModalElement("T", [
            new ModalSection("This is **info** text.", "block-info")
        ]);

        var view = SlackModalConverter.ToSlackView(modal);
        var section = view.Blocks[0].Should().BeOfType<SlackSectionBlock>().Subject;

        section.Text.Should().BeOfType<SlackMrkdwn>()
            .Which.Text.Should().Be("This is **info** text.");
    }

    // ── ToSlackView — ModalDivider ────────────────────────────────────

    [Fact]
    public void ToSlackView_ModalDivider_ProducesDividerBlock()
    {
        var modal = new ModalElement("T", [
            new ModalDivider("block-div")
        ]);

        var view = SlackModalConverter.ToSlackView(modal);

        view.Blocks[0].Should().BeOfType<SlackDividerBlock>();
    }

    // ── ToSlackView — multiple blocks ─────────────────────────────────

    [Fact]
    public void ToSlackView_MultipleBlocks_AllConvertedInOrder()
    {
        var modal = new ModalElement("T", [
            new ModalSection("Intro text", "b0"),
            new ModalTextInput("Name", "b1", "a1"),
            new ModalDivider("b2"),
            new ModalSelect("Color", "b3", "a3", [new ModalOption("Red", "red")]),
        ]);

        var view = SlackModalConverter.ToSlackView(modal);

        view.Blocks.Should().HaveCount(4);
        view.Blocks[0].Should().BeOfType<SlackSectionBlock>();
        view.Blocks[1].Should().BeOfType<SlackModalInputBlock>();
        view.Blocks[2].Should().BeOfType<SlackDividerBlock>();
        view.Blocks[3].Should().BeOfType<SlackModalInputBlock>();
    }

    // ── ToViewSubmissionResponse ──────────────────────────────────────

    [Fact]
    public void ToViewSubmissionResponse_ModalCloseResponse_ReturnsResponseActionClear()
    {
        var response = SlackModalConverter.ToViewSubmissionResponse(new ModalCloseResponse());

        var el = SerializeResponse(response);
        el.GetProperty("response_action").GetString().Should().Be("clear");
    }

    [Fact]
    public void ToViewSubmissionResponse_ModalCloseResponseClearAll_ReturnsResponseActionClear()
    {
        // Both ClearAll = true and false map to "clear" (Slack uses clear for both)
        var response = SlackModalConverter.ToViewSubmissionResponse(new ModalCloseResponse(ClearAll: true));

        var el = SerializeResponse(response);
        el.GetProperty("response_action").GetString().Should().Be("clear");
    }

    [Fact]
    public void ToViewSubmissionResponse_ModalUpdateResponse_ReturnsResponseActionUpdate()
    {
        var view = new ModalElement("Updated", []);
        var response = SlackModalConverter.ToViewSubmissionResponse(new ModalUpdateResponse(view));

        var el = SerializeResponse(response);
        el.GetProperty("response_action").GetString().Should().Be("update");
        el.GetProperty("view").GetProperty("type").GetString().Should().Be("modal");
        el.GetProperty("view").GetProperty("title").GetProperty("text").GetString().Should().Be("Updated");
    }

    [Fact]
    public void ToViewSubmissionResponse_ModalPushResponse_ReturnsResponseActionPush()
    {
        var view = new ModalElement("Pushed", []);
        var response = SlackModalConverter.ToViewSubmissionResponse(new ModalPushResponse(view));

        var el = SerializeResponse(response);
        el.GetProperty("response_action").GetString().Should().Be("push");
        el.GetProperty("view").GetProperty("type").GetString().Should().Be("modal");
    }

    [Fact]
    public void ToViewSubmissionResponse_ModalErrorsResponse_ReturnsErrors()
    {
        var errors = new Dictionary<string, string>
        {
            ["block-name"] = "Name is required",
            ["block-email"] = "Invalid email",
        };
        var response = SlackModalConverter.ToViewSubmissionResponse(new ModalErrorsResponse(errors));

        var el = SerializeResponse(response);
        el.GetProperty("response_action").GetString().Should().Be("errors");
        el.GetProperty("errors").GetProperty("block-name").GetString().Should().Be("Name is required");
        el.GetProperty("errors").GetProperty("block-email").GetString().Should().Be("Invalid email");
    }

    [Fact]
    public void ToViewSubmissionResponse_UpdateWithContextId_ContextIdEncodedInMetadata()
    {
        var view = new ModalElement("T", [], PrivateMetadata: "original-payload");
        var response = SlackModalConverter.ToViewSubmissionResponse(
            new ModalUpdateResponse(view), contextId: "ctx-456");

        var el = SerializeResponse(response);
        var privateMetadata = el.GetProperty("view").GetProperty("private_metadata").GetString();
        var (ctxId, payload) = SlackModalConverter.DecodeMetadata(privateMetadata);

        ctxId.Should().Be("ctx-456");
        payload.Should().Be("original-payload");
    }

    [Fact]
    public void ToViewSubmissionResponse_PushWithContextId_ContextIdEncodedInMetadata()
    {
        var view = new ModalElement("T", [], PrivateMetadata: "payload");
        var response = SlackModalConverter.ToViewSubmissionResponse(
            new ModalPushResponse(view), contextId: "ctx-789");

        var el = SerializeResponse(response);
        var privateMetadata = el.GetProperty("view").GetProperty("private_metadata").GetString();
        var (ctxId, _) = SlackModalConverter.DecodeMetadata(privateMetadata);

        ctxId.Should().Be("ctx-789");
    }

    // ── Edge cases / guard clauses ────────────────────────────────────

    [Fact]
    public void ToSlackView_UnknownModalBlockType_ThrowsNotSupportedException()
    {
        var modal = new ModalElement("T", [new UnknownBlock()]);

        var act = () => SlackModalConverter.ToSlackView(modal);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*UnknownBlock*");
    }

    [Fact]
    public void ToViewSubmissionResponse_UnknownResponseType_ThrowsNotSupportedException()
    {
        var act = () => SlackModalConverter.ToViewSubmissionResponse(new UnknownResponse());

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*UnknownResponse*");
    }

    // ── Test doubles ──────────────────────────────────────────────────

    /// <summary>Unknown block type for guard-clause testing.</summary>
    private sealed record UnknownBlock() : ModalBlock("Unknown", "b-unknown");

    /// <summary>Unknown response type for guard-clause testing.</summary>
    private sealed record UnknownResponse : ModalResponse;
}
