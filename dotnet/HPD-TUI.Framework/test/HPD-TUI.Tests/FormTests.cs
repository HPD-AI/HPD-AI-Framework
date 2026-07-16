using HPD.TUI.Core;
using HPD.TUI.Forms;
using HPD.TUI.Rendering;

namespace HPD.TUI.Tests;

public sealed class FormTests
{
    [Fact]
    public void TextField_UsesExplicitEditingAndRestoresCanceledValue()
    {
        var field = new TextFormField("name", "Name", "hpd", isRequired: true);

        Assert.False(field.HandleInput(Character('x')));
        Assert.True(field.BeginEdit());
        Assert.True(field.HandleInput(Character('x')));
        Assert.Equal("hpdx", field.Value);
        Assert.True(field.CancelEdit());

        Assert.Equal("hpd", field.Value);
        Assert.False(field.IsDirty);
    }

    [Fact]
    public void Controller_NavigatesVisibleEnabledFieldsAndSubmits()
    {
        var showConditional = false;
        var name = new TextFormField("name", "Name", "hpd", isRequired: true);
        var hidden = new TextFormField("hidden", "Hidden", isRequired: true)
            .VisibleWhen(() => showConditional);
        var disabled = new TextFormField("disabled", "Disabled")
            .EnabledWhen(static () => false);
        var enabled = FormFields.Boolean("enabled", "Enabled");
        var model = new FormModel().Add(name).Add(hidden).Add(disabled).Add(enabled);
        var controller = new FormController(model);
        var submitted = false;
        controller.Submitted = _ => submitted = true;

        controller.HandleInput(Key(KeyCode.DownArrow));
        controller.HandleInput(Key(KeyCode.RightArrow));
        controller.HandleInput(new KeyEvent(KeyCode.Enter, Modifiers: KeyModifiers.Ctrl));

        Assert.Equal(3, model.ActiveFieldIndex);
        Assert.True(enabled.Value);
        Assert.True(submitted);
    }

    [Fact]
    public void ConditionalField_ReconcilesSelectionWhenItBecomesHidden()
    {
        var visible = true;
        var first = FormFields.Boolean("first", "First");
        var conditional = new TextFormField("conditional", "Conditional")
            .VisibleWhen(() => visible);
        var last = new TextFormField("last", "Last");
        var model = new FormModel().Add(first).Add(conditional).Add(last);
        model.ActiveFieldIndex = 1;

        visible = false;

        Assert.Same(last, model.ActiveField);
        Assert.Equal(2, model.ActiveFieldIndex);
    }

    [Fact]
    public void ChoiceField_CyclesTypedValuesAndSkipsDisabledChoices()
    {
        var field = new ChoiceFormField<FormMode>(
            "mode",
            "Mode",
            [
                new("basic", FormMode.Basic, "Basic"),
                new("blocked", FormMode.Blocked, "Blocked", Disabled: true),
                new("advanced", FormMode.Advanced, "Advanced")
            ],
            FormMode.Basic);

        field.HandleInput(Key(KeyCode.RightArrow));

        Assert.Equal(FormMode.Advanced, field.Value);
        Assert.True(field.IsDirty);
    }

    [Fact]
    public async Task ChoiceField_PickerSelectsTypedChoice()
    {
        var picked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var choices = new FormChoice<FormMode>[]
        {
            new("basic", FormMode.Basic, "Basic"),
            new("advanced", FormMode.Advanced, "Advanced")
        };
        var field = new ChoiceFormField<FormMode>(
            "mode",
            "Mode",
            choices,
            FormMode.Basic,
            presentation: FormChoicePresentation.Picker,
            picker: _ =>
            {
                picked.TrySetResult();
                return ValueTask.FromResult<FormChoice<FormMode>?>(choices[1]);
            });

        field.HandleInput(Key(KeyCode.Enter));
        await picked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Yield();

        Assert.Equal(FormMode.Advanced, field.Value);
    }

    [Fact]
    public void NumericField_StepsThenEditsTheCurrentValue()
    {
        var field = new IntegerFormField(
            "turns",
            "Turns",
            5,
            minimum: 0,
            maximum: 20,
            step: 2);

        field.HandleInput(Key(KeyCode.RightArrow));
        field.BeginEdit();
        field.HandleInput(Key(KeyCode.Backspace));
        field.HandleInput(Character('9'));
        field.AcceptEdit();

        Assert.Equal(9, field.Value);
    }

    [Fact]
    public void LongField_ClampsAtBounds()
    {
        var field = new LongFormField(
            "tokens",
            "Tokens",
            9_000_000_000,
            minimum: 0,
            maximum: 10_000_000_000,
            step: 2_000_000_000);

        field.HandleInput(Key(KeyCode.RightArrow));

        Assert.Equal(10_000_000_000, field.Value);
    }

    [Fact]
    public void MultiChoiceField_TogglesTypedSelections()
    {
        var field = new MultiChoiceFormField<string>(
            "scopes",
            "Scopes",
            [new("read", "read", "Read"), new("write", "write", "Write")],
            minimumSelected: 1);

        field.HandleInput(Character(' '));

        Assert.Equal(["read"], field.Value);
        Assert.True(field.Validate().IsValid);
    }

    [Fact]
    public void View_RendersOnlyBoundedWindowAndContextualHint()
    {
        var model = new FormModel();
        for (var index = 0; index < 20; index++)
        {
            model.Add(new TextFormField($"field-{index}", $"Field {index}", index.ToString()));
        }

        model.ActiveFieldIndex = 10;
        var view = new FormView(model, new FormController(model), maxVisibleRows: 4);

        var rendered = TuiCapture.RenderToString(view, 50, 12);

        Assert.Contains("(11/20)", rendered);
        Assert.Contains("Enter edit", rendered);
        Assert.DoesNotContain("Field 0", rendered);
        Assert.DoesNotContain("Field 19", rendered);
    }

    [Fact]
    public void View_LargeFormReadsDisplayValuesOnlyForTheBoundedWindow()
    {
        var displayReads = 0;
        var model = new FormModel();
        for (var index = 0; index < 1_000; index++)
        {
            model.Add(new ActionFormField<int>(
                $"field-{index}",
                $"Field {index}",
                () => index,
                () =>
                {
                    displayReads++;
                    return index.ToString();
                },
                static () => ValueTask.CompletedTask));
        }

        model.ActiveFieldIndex = 500;
        var view = new FormView(model, new FormController(model), maxVisibleRows: 10);

        _ = TuiCapture.RenderToString(view, 60, 18);

        Assert.InRange(displayReads, 1, 20);
    }

    [Fact]
    public void FormDefinition_BuildsTypedResult()
    {
        var enabled = FormFields.Boolean("enabled", "Enabled", true);
        var model = new FormModel().Add(enabled);
        var definition = new FormDefinition<SettingsResult>(
            model,
            () => new SettingsResult(enabled.Value));

        Assert.Equal(new SettingsResult(true), definition.BuildResult());
    }

    [Fact]
    public async Task LiveUpdateSession_SerializesAndCoalescesChanges()
    {
        var field = new ChoiceFormField<int>(
            "value",
            "Value",
            [new("zero", 0, "Zero"), new("one", 1, "One"), new("two", 2, "Two")],
            0);
        var model = new FormModel().Add(field);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updates = new List<int>();
        var definition = new FormDefinition<int>(
            model,
            () => field.Value,
            FormUpdateMode.Live,
            async (value, cancellationToken) =>
            {
                updates.Add(value);
                if (value == 1)
                {
                    firstStarted.TrySetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
            });
        using var session = new FormUpdateSession<int>(definition, new FormController(model));

        field.Select(1);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        field.Select(2);
        releaseFirst.TrySetResult();

        Assert.True(await session.FlushAsync());
        Assert.Equal([1, 2], updates);
    }

    [Fact]
    public async Task LiveUpdateSession_FlushWaitsForPendingActionChange()
    {
        var value = 0;
        var releaseAction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var field = new ActionFormField<int>(
            "nested",
            "Nested",
            () => value,
            () => value.ToString(),
            async () =>
            {
                await releaseAction.Task;
                value = 1;
            });
        var model = new FormModel().Add(field);
        var persisted = 0;
        var definition = new FormDefinition<int>(
            model,
            () => field.Value,
            FormUpdateMode.Live,
            (result, _) =>
            {
                persisted = result;
                return ValueTask.CompletedTask;
            });
        using var session = new FormUpdateSession<int>(definition, new FormController(model));

        field.HandleInput(Key(KeyCode.Enter));
        var flush = session.FlushAsync().AsTask();
        Assert.False(flush.IsCompleted);

        releaseAction.TrySetResult();
        Assert.True(await flush.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, persisted);
    }

    private static KeyEvent Key(KeyCode code) => new(code);

    private static KeyEvent Character(char value)
        => new(KeyCode.Character, new Rune(value));

    private sealed record SettingsResult(bool Enabled);

    private enum FormMode
    {
        Basic,
        Blocked,
        Advanced
    }
}
