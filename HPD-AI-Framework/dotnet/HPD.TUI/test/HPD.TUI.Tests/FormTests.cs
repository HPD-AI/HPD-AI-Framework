using HPD.TUI.Core;
using HPD.TUI.Forms;
using HPD.TUI.Models;
using HPD.TUI.Rendering;

namespace HPD.TUI.Tests;

public sealed class FormTests
{
    [Fact]
    public void TextField_TracksDirtyStateAndValidation()
    {
        var field = new TextFormField("name", "Name", isRequired: true);

        var invalid = field.Validate();
        field.HandleInput(new KeyEvent(KeyCode.Character, new Rune('a')));
        var valid = field.Validate();

        Assert.False(invalid.IsValid);
        Assert.True(valid.IsValid);
        Assert.True(field.IsDirty);
    }

    [Fact]
    public void Controller_MovesFieldsAndSubmitsValidForm()
    {
        var model = new FormModel()
            .Add(new TextFormField("name", "Name", "hpd", isRequired: true))
            .Add(new BooleanFormField("enabled", "Enabled"));
        var controller = new FormController(model);
        var submitted = false;
        controller.Submitted = _ => submitted = true;

        controller.HandleInput(new KeyEvent(KeyCode.DownArrow));
        controller.HandleInput(new KeyEvent(KeyCode.Character, new Rune(' ')));
        controller.HandleInput(new KeyEvent(KeyCode.Enter, Modifiers: KeyModifiers.Ctrl));

        Assert.Equal(1, model.ActiveFieldIndex);
        Assert.True(((BooleanFormField)model.Fields[1]).Value);
        Assert.True(submitted);
    }

    [Fact]
    public void View_RendersActiveField()
    {
        var model = new FormModel()
            .Add(new TextFormField("name", "Name", "hpd"));
        var controller = new FormController(model);
        var view = new FormView(model, controller);

        var rendered = TuiCapture.RenderToString(view, 24, 1);

        Assert.Contains("> Name: hpd", rendered);
    }

    [Fact]
    public void NumericFields_ValidateNumbers()
    {
        var integer = new IntegerFormField("retries", "Retries");
        integer.HandleInput(new KeyEvent(KeyCode.Character, new Rune('x')));

        var number = new DecimalFormField("temp", "Temperature", 0.7m);

        Assert.False(integer.Validate().IsValid);
        Assert.Equal(0.7m, number.Value);
        Assert.True(number.Validate().IsValid);
    }

    [Fact]
    public void EnumField_CyclesValues()
    {
        var field = new EnumFormField<FormMode>("mode", "Mode", FormMode.Basic);

        field.HandleInput(new KeyEvent(KeyCode.RightArrow));

        Assert.Equal(FormMode.Advanced, field.Value);
        Assert.True(field.IsDirty);
    }

    [Fact]
    public void TextField_AllowsMultilineWithShiftEnter()
    {
        var field = new TextFormField("body", "Body", isMultiline: true);

        field.HandleInput(new KeyEvent(KeyCode.Character, new Rune('a')));
        field.HandleInput(new KeyEvent(KeyCode.Enter, Modifiers: KeyModifiers.Shift));
        field.HandleInput(new KeyEvent(KeyCode.Character, new Rune('b')));

        Assert.Equal("a\nb", field.Value);
    }

    [Fact]
    public void SelectField_UsesCollectionNavigation()
    {
        var model = new CollectionModel<string>()
            .Add("small", "Small")
            .Add("large", "Large");
        var field = new SelectFormField<string>("model", "Model", model);

        field.HandleInput(new KeyEvent(KeyCode.RightArrow));

        Assert.Equal("large", field.Value);
        Assert.Equal("Large", field.DisplayValue);
        Assert.True(field.IsDirty);
    }

    [Fact]
    public void MultiSelectField_TogglesCurrentItem()
    {
        var model = new CollectionModel<string>()
            .Add("read", "Read")
            .Add("write", "Write");
        var field = new MultiSelectFormField<string>("scopes", "Scopes", model, minSelected: 1);

        field.HandleInput(new KeyEvent(KeyCode.Character, new Rune(' ')));

        Assert.Equal(["read"], field.Value);
        Assert.True(field.Validate().IsValid);
        Assert.True(field.IsDirty);
    }

    private enum FormMode
    {
        Basic,
        Advanced
    }
}
