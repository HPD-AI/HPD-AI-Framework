using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Models;
using HPD.TUI.Views;

namespace HPD.TUI.Flows;

public sealed class ConfirmPromptFlow : PromptFlow<bool>
{
    private readonly string _prompt;
    private bool? _defaultValue;

    internal ConfirmPromptFlow(string prompt)
    {
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
    }

    public ConfirmPromptFlow Default(bool value)
    {
        _defaultValue = value;
        return this;
    }

    protected override IComponent CreateComponent(PromptFlowContext<bool> context)
    {
        var model = new SelectionModel<bool>()
            .Add(true, "Yes")
            .Add(false, "No");
        var controller = new SelectionController<bool>(model)
        {
            Submitted = item => context.Submit(item.Value),
            Canceled = context.Cancel
        };

        if (_defaultValue == false)
        {
            controller.Move(1);
        }

        var view = new SelectionView<bool>(model, controller);
        var stack = new Stack().Add(new Text(_prompt)).Add(view);
        return new PromptFlowShell<bool>(new PromptFlowComponent<bool>(stack, context), view);
    }
}
