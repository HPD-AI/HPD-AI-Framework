using FluentAssertions;
using HPD.Agent.Adapters.Modals;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for cross-platform modal types in <c>ModalTypes.cs</c>.
/// Verifies record construction, defaults, and that all ModalBlock subtypes
/// are correctly derived from the abstract base.
/// </summary>
public class ModalTypesTests
{
    // ── ModalElement ──────────────────────────────────────────────────

    [Fact]
    public void ModalElement_DefaultsAllOptionalFieldsToNull()
    {
        var modal = new ModalElement("Title", []);

        modal.Title.Should().Be("Title");
        modal.SubmitLabel.Should().BeNull();
        modal.CloseLabel.Should().BeNull();
        modal.CallbackId.Should().BeNull();
        modal.PrivateMetadata.Should().BeNull();
        modal.NotifyOnClose.Should().BeFalse();
    }

    [Fact]
    public void ModalElement_AllFieldsSet_RoundTrips()
    {
        var blocks = new ModalBlock[]
        {
            new ModalTextInput("Name", "block-name", "action-name"),
        };
        var modal = new ModalElement(
            Title:           "My Dialog",
            Blocks:          blocks,
            SubmitLabel:     "Submit",
            CloseLabel:      "Cancel",
            CallbackId:      "my-callback",
            PrivateMetadata: "payload-data",
            NotifyOnClose:   true);

        modal.Title.Should().Be("My Dialog");
        modal.SubmitLabel.Should().Be("Submit");
        modal.CloseLabel.Should().Be("Cancel");
        modal.CallbackId.Should().Be("my-callback");
        modal.PrivateMetadata.Should().Be("payload-data");
        modal.NotifyOnClose.Should().BeTrue();
        modal.Blocks.Should().HaveCount(1);
    }

    // ── ModalBlock subtypes ───────────────────────────────────────────

    [Fact]
    public void ModalTextInput_DefaultsAreCorrect()
    {
        var input = new ModalTextInput("Label", "block-1", "action-1");

        input.Placeholder.Should().BeNull();
        input.InitialValue.Should().BeNull();
        input.Multiline.Should().BeFalse();
        input.MinLength.Should().BeNull();
        input.MaxLength.Should().BeNull();
        input.Optional.Should().BeFalse();
    }

    [Fact]
    public void ModalSelect_WithOptions_OptionsPreserved()
    {
        var options = new[]
        {
            new ModalOption("Apple", "apple"),
            new ModalOption("Banana", "banana"),
        };
        var select = new ModalSelect("Fruit", "block-2", "action-2", options);

        select.Options.Should().HaveCount(2);
        select.Options[0].Value.Should().Be("apple");
        select.InitialValue.Should().BeNull();
    }

    [Fact]
    public void ModalRadioGroup_DefaultsAreCorrect()
    {
        var options = new[] { new ModalOption("Yes", "yes"), new ModalOption("No", "no") };
        var radio = new ModalRadioGroup("Choice", "block-3", "action-3", options);

        radio.InitialValue.Should().BeNull();
        radio.Optional.Should().BeFalse();
    }

    [Fact]
    public void ModalSection_TextAndBlockIdAccessible()
    {
        var section = new ModalSection("This is explanatory text.", "block-info");

        section.Text.Should().Be("This is explanatory text.");
        section.BlockId.Should().Be("block-info");
    }

    [Fact]
    public void ModalDivider_BlockIdAccessible()
    {
        var divider = new ModalDivider("block-div");

        divider.BlockId.Should().Be("block-div");
    }

    [Fact]
    public void AllBlockSubtypes_AreModalBlock()
    {
        var blocks = new ModalBlock[]
        {
            new ModalTextInput("L", "b1", "a1"),
            new ModalSelect("L", "b2", "a2", []),
            new ModalRadioGroup("L", "b3", "a3", []),
            new ModalSection("text", "b4"),
            new ModalDivider("b5"),
        };

        blocks.Should().AllBeAssignableTo<ModalBlock>();
    }

    // ── ModalOption ───────────────────────────────────────────────────

    [Fact]
    public void ModalOption_WithDescription_Preserved()
    {
        var option = new ModalOption("Label", "val", Description: "hint");

        option.Label.Should().Be("Label");
        option.Value.Should().Be("val");
        option.Description.Should().Be("hint");
    }

    [Fact]
    public void ModalOption_WithoutDescription_DefaultsToNull()
    {
        var option = new ModalOption("Label", "val");

        option.Description.Should().BeNull();
    }

    // ── ModalResponse subtypes ────────────────────────────────────────

    [Fact]
    public void ModalCloseResponse_ClearAllFlagPreserved()
    {
        var withClear    = new ModalCloseResponse(ClearAll: true);
        var withoutClear = new ModalCloseResponse();

        withClear.ClearAll.Should().BeTrue();
        withoutClear.ClearAll.Should().BeFalse();
    }

    [Fact]
    public void ModalUpdateResponse_ViewPreserved()
    {
        var view = new ModalElement("New View", []);
        var response = new ModalUpdateResponse(view);

        response.View.Should().BeSameAs(view);
        response.Should().BeAssignableTo<ModalResponse>();
    }

    [Fact]
    public void ModalPushResponse_ViewPreserved()
    {
        var view = new ModalElement("Pushed View", []);
        var response = new ModalPushResponse(view);

        response.View.Should().BeSameAs(view);
        response.Should().BeAssignableTo<ModalResponse>();
    }

    [Fact]
    public void ModalErrorsResponse_ErrorsDictPreserved()
    {
        var errors = new Dictionary<string, string>
        {
            ["block-1"] = "Required field",
            ["block-2"] = "Too long",
        };
        var response = new ModalErrorsResponse(errors);

        response.Errors.Should().HaveCount(2);
        response.Errors["block-1"].Should().Be("Required field");
        response.Errors["block-2"].Should().Be("Too long");
        response.Should().BeAssignableTo<ModalResponse>();
    }

    [Fact]
    public void AllResponseSubtypes_AreModalResponse()
    {
        var responses = new ModalResponse[]
        {
            new ModalCloseResponse(),
            new ModalUpdateResponse(new ModalElement("T", [])),
            new ModalPushResponse(new ModalElement("T", [])),
            new ModalErrorsResponse(new Dictionary<string, string>()),
        };

        responses.Should().AllBeAssignableTo<ModalResponse>();
    }
}
