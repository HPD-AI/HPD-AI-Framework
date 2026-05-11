using HPD.TUI.Core;
using HPD.TUI.Flows;
using HPD.TUI.Models;
using HPD.TUI.Rendering;

namespace HPD.TUI.Tests;

public sealed class PromptFlowHelperTests
{
    [Fact]
    public void SecretFlow_MasksRenderedValueAndSubmitsPlainText()
    {
        PromptResult<string>? result = null;
        var component = PromptFlow.Secret("Token").CreateComponentForTesting(r => result = r);

        component.HandleInput(new KeyEvent(KeyCode.Character, new Rune('s')));
        component.HandleInput(new KeyEvent(KeyCode.Character, new Rune('3')));

        var rendered = TuiCapture.RenderToString(component, 8, 1);

        Assert.Contains("**", rendered);
        Assert.DoesNotContain("s3", rendered);

        component.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.True(result?.IsSubmitted);
        Assert.Equal("s3", result?.Value);
    }

    [Fact]
    public void ConfirmFlow_DefaultFalseSubmitsNo()
    {
        PromptResult<bool>? result = null;
        var component = PromptFlow.Confirm("Continue?").Default(false).CreateComponentForTesting(r => result = r);

        component.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.True(result?.IsSubmitted);
        Assert.False(result?.Value);
    }

    [Fact]
    public void SelectFlow_SubmitsSelectedValue()
    {
        PromptResult<string>? result = null;
        var component = PromptFlow
            .Select("Model", ["small", "large"], value => value)
            .CreateComponentForTesting(r => result = r);

        component.HandleInput(new KeyEvent(KeyCode.DownArrow));
        component.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.True(result?.IsSubmitted);
        Assert.Equal("large", result?.Value);
    }

    [Fact]
    public void MultiSelectFlow_TogglesAndValidatesMinimumSelection()
    {
        var model = new MultiSelectionModel<string> { MinSelected = 2 }
            .Add("a", "a")
            .Add("b", "b")
            .Add("c", "c");
        PromptResult<IReadOnlyList<string>>? result = null;
        var component = PromptFlow.MultiSelect("Tools", model).CreateComponentForTesting(r => result = r);

        component.HandleInput(new KeyEvent(KeyCode.Character, new Rune(' ')));
        component.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.Null(result);
        Assert.Contains("Select at least 2.", TuiCapture.RenderToString(component, 24, 6));

        component.HandleInput(new KeyEvent(KeyCode.DownArrow));
        component.HandleInput(new KeyEvent(KeyCode.Character, new Rune(' ')));
        component.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.True(result?.IsSubmitted);
        Assert.Equal(["a", "b"], result?.Value);
    }
}
