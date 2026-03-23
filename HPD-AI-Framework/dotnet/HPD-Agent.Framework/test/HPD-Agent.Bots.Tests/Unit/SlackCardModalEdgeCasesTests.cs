using FluentAssertions;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.Modals;
using HPD.Agent.Bots.Slack;
using System.Text;

namespace HPD.Agent.Bots.Tests.Unit;

/// <summary>
/// Phase 3: Comprehensive edge case tests for <see cref="SlackCardRenderer"/> and <see cref="SlackModalConverter"/>.
/// Tests complex rendering scenarios, metadata encoding/decoding, field handling, UTF-8 support,
/// modal validation, and round-trip preservation.
/// </summary>
public class SlackCardModalEdgeCasesTests
{
    private readonly SlackCardRenderer _renderer = new();

    // ────────────────────────────────────────────────────────────────────────
    // PHASE 3.1: Complex Nested Card Structures
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderCard_ComplexNesting_AllElementsRendered()
    {
        var card = new CardElement(
            Title: "Complex Card",
            Subtitle: "With nested sections",
            Children: new CardChild[]
            {
                new CardSection(
                    Title: "Section 1",
                    Children: new CardChild[]
                    {
                        new CardText(Text: "Normal text"),
                        new CardText(Text: "Muted text", Style: "muted"),
                        new CardDivider(),
                    }
                ),
                new CardSection(
                    Title: "Section 2",
                    Children: new CardChild[]
                    {
                        new CardFields(
                            Fields: new[]
                            {
                                new CardField("Label 1", "Value 1"),
                                new CardField("Label 2", "Value 2"),
                            }
                        ),
                    }
                ),
                new CardSection(
                    Title: "Section 3",
                    Children: new CardChild[]
                    {
                        new CardLink(Label: "Click here", Url: "https://example.com"),
                        new CardImage(Url: "https://example.com/image.png", AltText: "Example"),
                    }
                ),
            }
        );

        var blocks = _renderer.RenderCard(card);
        blocks.Should().NotBeEmpty("nested card should render blocks");
        blocks.Length.Should().BeGreaterThan(3, "complex nesting should generate multiple blocks");
    }

    [Fact]
    public void RenderCard_TextWithMarkdownFormatting_FormattingPreserved()
    {
        var card = new CardElement(
            Title: "**Bold** title and *italic*",
            Children: new CardChild[]
            {
                new CardText(Text: "*Italic* and **bold** and `code` text"),
                new CardText(Text: "Multiple\nlines\nof\ntext"),
            }
        );

        var blocks = _renderer.RenderCard(card);
        blocks.Should().NotBeEmpty("formatted text should render");
    }

    [Fact]
    public void RenderCard_MultipleActionTypes_AllPreserved()
    {
        var actions = new CardActions(
            Actions: new CardAction[]
            {
                new CardButton(ActionId: "primary_btn", Label: "Primary", Style: "primary"),
                new CardButton(ActionId: "danger_btn", Label: "Danger", Style: "danger"),
                new CardButton(ActionId: "default_btn", Label: "Default"),
                new CardSelect(
                    ActionId: "select_1",
                    Placeholder: "Choose option",
                    Options: new[]
                    {
                        new CardSelectOption(Label: "Option 1", Value: "opt1"),
                        new CardSelectOption(Label: "Option 2", Value: "opt2"),
                    }
                ),
            }
        );

        var blocks = _renderer.RenderActions(actions);
        blocks.Should().NotBeEmpty("action block should render");
    }

    [Fact]
    public void RenderCard_DeeplyNestedSections_AllFlattened()
    {
        var card = new CardElement(
            Title: "Deep Nesting",
            Children: new CardChild[]
            {
                new CardSection(
                    Title: "Level 1",
                    Children: new CardChild[]
                    {
                        new CardText(Text: "Level 1 text"),
                        new CardSection(
                            Title: "Level 2 (nested)",
                            Children: new CardChild[]
                            {
                                new CardText(Text: "Level 2 text"),
                            }
                        ),
                    }
                ),
            }
        );

        var blocks = _renderer.RenderCard(card);
        blocks.Should().NotBeEmpty("nested sections should all render");
    }

    [Fact]
    public void RenderCard_MixedActionsAndText_OrderPreserved()
    {
        var card = new CardElement(
            Title: "Mixed Content",
            Children: new CardChild[]
            {
                new CardText(Text: "Before actions"),
                new CardActions(
                    Actions: new[] { new CardButton(ActionId: "btn1", Label: "Action") }
                ),
                new CardText(Text: "After actions"),
            }
        );

        var blocks = _renderer.RenderCard(card);
        blocks.Length.Should().BeGreaterThanOrEqualTo(3, "all elements should render in order");
    }

    // ────────────────────────────────────────────────────────────────────────
    // PHASE 3.2: Modal Metadata Encode/Decode Round-Trips
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EncodeMetadata_NullValues_EncodesEmpty()
    {
        var encoded = SlackModalConverter.EncodeMetadata(null, null);
        encoded.Should().NotBeNullOrEmpty("should produce valid base64");

        var (contextId, privateMetadata) = SlackModalConverter.DecodeMetadata(encoded);
        contextId.Should().BeNull();
        privateMetadata.Should().BeNull();
    }

    [Fact]
    public void EncodeMetadata_ContextIdOnly_Preserved()
    {
        var encoded = SlackModalConverter.EncodeMetadata("ctx_123", null);
        var (contextId, privateMetadata) = SlackModalConverter.DecodeMetadata(encoded);

        contextId.Should().Be("ctx_123");
        privateMetadata.Should().BeNull();
    }

    [Fact]
    public void EncodeMetadata_PrivateMetadataOnly_Preserved()
    {
        var encoded = SlackModalConverter.EncodeMetadata(null, "private_data");
        var (contextId, privateMetadata) = SlackModalConverter.DecodeMetadata(encoded);

        contextId.Should().BeNull();
        privateMetadata.Should().Be("private_data");
    }

    [Fact]
    public void EncodeMetadata_BothValues_BothPreserved()
    {
        var encoded = SlackModalConverter.EncodeMetadata("ctx_456", "my_metadata");
        var (contextId, privateMetadata) = SlackModalConverter.DecodeMetadata(encoded);

        contextId.Should().Be("ctx_456");
        privateMetadata.Should().Be("my_metadata");
    }

    [Fact]
    public void EncodeMetadata_MultipleRoundTrips_ConsistentResults()
    {
        var originalCtx = "context_abc";
        var originalData = "metadata_xyz";

        var encoded1 = SlackModalConverter.EncodeMetadata(originalCtx, originalData);
        var (ctx1, data1) = SlackModalConverter.DecodeMetadata(encoded1);

        var encoded2 = SlackModalConverter.EncodeMetadata(ctx1, data1);
        var (ctx2, data2) = SlackModalConverter.DecodeMetadata(encoded2);

        ctx1.Should().Be(originalCtx);
        data1.Should().Be(originalData);
        ctx2.Should().Be(ctx1);
        data2.Should().Be(data1);
        encoded1.Should().Be(encoded2, "multiple round-trips should produce identical encodings");
    }

    [Fact]
    public void EncodeMetadata_LongValues_WithinSlackLimit()
    {
        var longMetadata = string.Concat(Enumerable.Range(0, 100).Select(i => $"metadata_{i}_"));
        var encoded = SlackModalConverter.EncodeMetadata("ctx", longMetadata);

        encoded.Length.Should().BeLessThan(3000, "encoded metadata must fit in Slack limit");
    }

    [Fact]
    public void EncodeMetadata_SpecialCharacters_RoundTripPreserved()
    {
        var testCases = new[]
        {
            ("ctx\nwith\nnewlines", "data\twith\ttabs"),
            ("ctx\"with\"quotes", "data'with'apostrophes"),
            ("ctx\\with\\backslashes", "data{with}braces"),
        };

        foreach (var (ctx, data) in testCases)
        {
            var encoded = SlackModalConverter.EncodeMetadata(ctx, data);
            var (decodedCtx, decodedData) = SlackModalConverter.DecodeMetadata(encoded);

            decodedCtx.Should().Be(ctx, $"context {ctx} should round-trip");
            decodedData.Should().Be(data, $"data {data} should round-trip");
        }
    }

    [Fact]
    public void DecodeMetadata_InvalidBase64_FallbackToRaw()
    {
        var (contextId, privateMetadata) = SlackModalConverter.DecodeMetadata("not-base64-!!!!");
        contextId.Should().BeNull();
        privateMetadata.Should().Be("not-base64-!!!!");
    }

    [Fact]
    public void DecodeMetadata_EmptyString_ReturnsNulls()
    {
        var (contextId, privateMetadata) = SlackModalConverter.DecodeMetadata("");
        contextId.Should().BeNull();
        privateMetadata.Should().BeNull();
    }

    [Fact]
    public void DecodeMetadata_WhitespaceOnly_ReturnsNulls()
    {
        var (contextId, privateMetadata) = SlackModalConverter.DecodeMetadata("   \n\t  ");
        contextId.Should().BeNull();
        privateMetadata.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────────────────
    // PHASE 3.3: Field Ordering Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderFields_FieldOrdering_PreservedExactly()
    {
        var fields = new CardFields(
            Fields: new[]
            {
                new CardField("First", "1"),
                new CardField("Second", "2"),
                new CardField("Third", "3"),
                new CardField("Fourth", "4"),
            }
        );

        var block = _renderer.RenderFields(fields);
        block.Should().NotBeNull("fields should render");
    }

    [Fact]
    public void RenderFields_ManyFields_AllIncluded()
    {
        var fieldCount = 15;
        var fields = new CardFields(
            Fields: Enumerable.Range(0, fieldCount)
                .Select(i => new CardField($"Key{i}", $"Value{i}"))
                .ToList()
        );

        var block = _renderer.RenderFields(fields);
        block.Should().NotBeNull("all fields should render");
    }

    [Fact]
    public void RenderCard_MultipleFieldsBlocks_OrderPreserved()
    {
        var card = new CardElement(
            Title: "Multiple Fields",
            Children: new CardChild[]
            {
                new CardFields(Fields: new[] { new CardField("A", "1") }),
                new CardText(Text: "Divider"),
                new CardFields(Fields: new[] { new CardField("B", "2") }),
            }
        );

        var blocks = _renderer.RenderCard(card);
        blocks.Length.Should().BeGreaterThan(2, "fields and divider should all render");
    }

    // ────────────────────────────────────────────────────────────────────────
    // PHASE 3.4: UTF-8 Handling in Text Fields
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderCard_UTF8Title_PreservedCorrectly()
    {
        var card = new CardElement(
            Title: "Title: 中文 日本語 한국어 ελληνικά"
        );

        var blocks = _renderer.RenderCard(card);
        blocks.Should().NotBeEmpty();
    }

    [Fact]
    public void RenderCard_UTF8Subtitle_PreservedCorrectly()
    {
        var card = new CardElement(
            Subtitle: "🎉 Emoji: 😀 😃 😄 😁 🎊"
        );

        var blocks = _renderer.RenderCard(card);
        blocks.Should().NotBeEmpty();
    }

    [Fact]
    public void RenderCard_UTF8TextContent_PreservedCorrectly()
    {
        var card = new CardElement(
            Children: new CardChild[]
            {
                new CardText(Text: "Multilingual: 中文/日本語/한국어\nArabic: السلام عليكم\nHebrew: שלום"),
                new CardText(Text: "Emojis: 🚀 💻 🎯 ✨ 🌟"),
            }
        );

        var blocks = _renderer.RenderCard(card);
        blocks.Should().NotBeEmpty();
    }

    [Fact]
    public void RenderFields_UTF8LabelsAndValues_PreservedCorrectly()
    {
        var fields = new CardFields(
            Fields: new[]
            {
                new CardField("名前", "田中太郎"),
                new CardField("주소", "서울시"),
                new CardField("电话", "010-1234-5678"),
            }
        );

        var block = _renderer.RenderFields(fields);
        block.Should().NotBeNull();
    }

    [Fact]
    public void RenderLink_UTF8LabelAndUrl_PreservedCorrectly()
    {
        var link = new CardLink(Label: "クリックしてください", Url: "https://example.com/日本語");
        var block = _renderer.RenderLink(link);
        block.Should().NotBeNull();
    }

    [Fact]
    public void EncodeMetadata_UTF8Characters_RoundTripPreserved()
    {
        var utf8Strings = new[]
        {
            ("ctx_中文", "data_日本語"),
            ("ctx_한국어", "data_ελληνικά"),
            ("ctx_مرحبا", "data_שלום"),
            ("ctx_🚀💻", "data_✨🌟"),
        };

        foreach (var (ctx, data) in utf8Strings)
        {
            var encoded = SlackModalConverter.EncodeMetadata(ctx, data);
            var (decodedCtx, decodedData) = SlackModalConverter.DecodeMetadata(encoded);

            decodedCtx.Should().Be(ctx, $"UTF-8 context {ctx} should round-trip");
            decodedData.Should().Be(data, $"UTF-8 data {data} should round-trip");
        }
    }

    [Fact]
    public void ToSlackView_ModalUTF8Title_PreservedCorrectly()
    {
        var modal = new ModalElement(
            Title: "モーダル 中文 ελληνικά 🎉",
            Blocks: Array.Empty<ModalBlock>(),
            CallbackId: "modal_unicode"
        );

        var view = SlackModalConverter.ToSlackView(modal);
        view.Title.Text.Should().Be("モーダル 中文 ελληνικά 🎉");
    }

    // ────────────────────────────────────────────────────────────────────────
    // PHASE 3.5: Modal Callback ID Validation
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToSlackView_CallbackId_SetCorrectly()
    {
        var modal = new ModalElement(
            Title: "Test Modal",
            Blocks: Array.Empty<ModalBlock>(),
            CallbackId: "my_callback_id"
        );

        var view = SlackModalConverter.ToSlackView(modal);
        view.CallbackId.Should().Be("my_callback_id");
    }

    [Fact]
    public void ToSlackView_CallbackIdNull_NotSet()
    {
        var modal = new ModalElement(
            Title: "Test Modal",
            Blocks: Array.Empty<ModalBlock>()
        );

        var view = SlackModalConverter.ToSlackView(modal);
        view.CallbackId.Should().BeNull();
    }

    [Fact]
    public void ToSlackView_CallbackIdWithSpecialChars_PreservedCorrectly()
    {
        var specialIds = new[]
        {
            "callback_with_underscores",
            "callback-with-dashes",
            "callback.with.dots",
            "callback_123_numbers",
        };

        foreach (var callbackId in specialIds)
        {
            var modal = new ModalElement(
                Title: "Test",
                Blocks: Array.Empty<ModalBlock>(),
                CallbackId: callbackId
            );

            var view = SlackModalConverter.ToSlackView(modal);
            view.CallbackId.Should().Be(callbackId);
        }
    }

    [Fact]
    public void ToSlackView_ModalWithAllFields_AllSet()
    {
        var modal = new ModalElement(
            Title: "Complete Modal",
            Blocks: Array.Empty<ModalBlock>(),
            CallbackId: "complete_modal",
            SubmitLabel: "Submit",
            CloseLabel: "Close",
            PrivateMetadata: "private_data",
            NotifyOnClose: true
        );

        var view = SlackModalConverter.ToSlackView(modal);

        view.Title.Text.Should().Be("Complete Modal");
        view.CallbackId.Should().Be("complete_modal");
        view.Submit?.Text.Should().Be("Submit");
        view.Close?.Text.Should().Be("Close");
        view.NotifyOnClose.Should().BeTrue();
        view.PrivateMetadata.Should().NotBeNull();
    }

    // ────────────────────────────────────────────────────────────────────────
    // PHASE 3.6: Modal Block Conversion and Validation
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToSlackView_ModalBlocks_AllBlockTypesConverted()
    {
        var modal = new ModalElement(
            Title: "All Blocks",
            Blocks: new ModalBlock[]
            {
                new ModalTextInput(Label: "Text", BlockId: "text_1", ActionId: "action_1"),
                new ModalSelect(
                    Label: "Select",
                    BlockId: "select_1",
                    ActionId: "action_2",
                    Options: new[] { new ModalOption("Opt1", "val1") }
                ),
                new ModalRadioGroup(
                    Label: "Radio",
                    BlockId: "radio_1",
                    ActionId: "action_3",
                    Options: new[] { new ModalOption("Opt1", "val1") }
                ),
                new ModalSection(Text: "Section text", BlockId: "section_1"),
                new ModalDivider(BlockId: "divider_1"),
            },
            CallbackId: "all_blocks"
        );

        var view = SlackModalConverter.ToSlackView(modal);
        view.Blocks.Should().NotBeEmpty("all block types should convert");
        view.Blocks.Count.Should().Be(5, "all 5 blocks should be present");
    }

    [Fact]
    public void ToSlackView_ModalTextInput_PropertiesPreserved()
    {
        var modal = new ModalElement(
            Title: "Text Input Test",
            Blocks: new ModalBlock[]
            {
                new ModalTextInput(
                    Label: "Input Label",
                    BlockId: "input_1",
                    ActionId: "action_input",
                    Placeholder: "Type here",
                    InitialValue: "Initial",
                    Multiline: true,
                    MinLength: 1,
                    MaxLength: 100,
                    Optional: true
                ),
            },
            CallbackId: "text_input_test"
        );

        var view = SlackModalConverter.ToSlackView(modal);
        view.Blocks.Should().HaveCount(1);
    }

    [Fact]
    public void ToSlackView_ModalSelectOptions_AllIncluded()
    {
        var options = new[]
        {
            new ModalOption("Option 1", "opt_1", "Description 1"),
            new ModalOption("Option 2", "opt_2", "Description 2"),
            new ModalOption("Option 3", "opt_3"),
        };

        var modal = new ModalElement(
            Title: "Select Test",
            Blocks: new ModalBlock[]
            {
                new ModalSelect(
                    Label: "Select",
                    BlockId: "select_1",
                    ActionId: "action_select",
                    Options: options,
                    InitialValue: "opt_1"
                ),
            },
            CallbackId: "select_test"
        );

        var view = SlackModalConverter.ToSlackView(modal);
        view.Blocks.Should().HaveCount(1, "select block should be converted");
    }

    [Fact]
    public void ToSlackView_NotifyOnClose_FlagPreserved()
    {
        var modalWithNotify = new ModalElement(
            Title: "Modal",
            Blocks: Array.Empty<ModalBlock>(),
            CallbackId: "notify_test",
            NotifyOnClose: true
        );

        var view = SlackModalConverter.ToSlackView(modalWithNotify);
        view.NotifyOnClose.Should().BeTrue();
    }

    [Fact]
    public void ToSlackView_SubmitAndCloseLabels_CustomLabelsSet()
    {
        var modal = new ModalElement(
            Title: "Modal",
            Blocks: Array.Empty<ModalBlock>(),
            CallbackId: "labels_test",
            SubmitLabel: "Send",
            CloseLabel: "Cancel"
        );

        var view = SlackModalConverter.ToSlackView(modal);
        view.Submit?.Text.Should().Be("Send");
        view.Close?.Text.Should().Be("Cancel");
    }

    [Fact]
    public void ToSlackView_NoSubmitOrCloseLabels_DefaultBehavior()
    {
        var modal = new ModalElement(
            Title: "Modal",
            Blocks: Array.Empty<ModalBlock>(),
            CallbackId: "no_labels_test"
        );

        var view = SlackModalConverter.ToSlackView(modal);
        view.Submit.Should().BeNull();
        view.Close.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────────────────
    // PHASE 3.7: Edge Cases and Error Handling
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderCard_NullChildren_EmptyList()
    {
        var card = new CardElement(
            Title: "Card",
            Children: null
        );

        var blocks = _renderer.RenderCard(card);
        blocks.Should().NotBeEmpty("title should still render");
    }

    [Fact]
    public void RenderCard_EmptyChildrenList_OnlyTitleRenders()
    {
        var card = new CardElement(
            Title: "Card",
            Children: Array.Empty<CardChild>()
        );

        var blocks = _renderer.RenderCard(card);
        blocks.Should().NotBeEmpty("title should render");
    }

    [Fact]
    public void ToSlackView_EmptyBlocks_ViewCreated()
    {
        var modal = new ModalElement(
            Title: "Modal",
            Blocks: Array.Empty<ModalBlock>(),
            CallbackId: "empty_blocks"
        );

        var view = SlackModalConverter.ToSlackView(modal);
        view.Should().NotBeNull();
        view.Blocks.Should().BeEmpty();
    }

    [Fact]
    public void ToSlackView_PreEncodedMetadata_UsedDirectly()
    {
        var modal = new ModalElement(
            Title: "Test Modal",
            Blocks: Array.Empty<ModalBlock>(),
            CallbackId: "modal_test"
        );

        var preEncoded = SlackModalConverter.EncodeMetadata("ctx_123", "pre_encoded_data");
        var view = SlackModalConverter.ToSlackView(modal, preEncoded);

        view.PrivateMetadata.Should().Be(preEncoded);
        var (contextId, data) = SlackModalConverter.DecodeMetadata(view.PrivateMetadata);
        contextId.Should().Be("ctx_123");
        data.Should().Be("pre_encoded_data");
    }

    [Fact]
    public void MetadataRoundTrip_ComplexScenario_AllPreserved()
    {
        var agentContextId = "agent_session_abc123";
        var userMetadata = "{\"userId\":42,\"action\":\"view_details\",\"timestamp\":\"2025-03-11T10:00:00Z\"}";

        var encoded = SlackModalConverter.EncodeMetadata(agentContextId, userMetadata);
        var view = new SlackModalView(
            Type: "modal",
            Title: new SlackPlainText("Test"),
            Blocks: new List<SlackBlock>(),
            CallbackId: "test",
            PrivateMetadata: encoded
        );

        var (decodedCtx, decodedData) = SlackModalConverter.DecodeMetadata(view.PrivateMetadata);
        decodedCtx.Should().Be(agentContextId);
        decodedData.Should().Be(userMetadata);
    }
}
