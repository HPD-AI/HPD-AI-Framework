using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Models;
using HPD.TUI.Views;

namespace HPD.TUI.Flows;

public sealed class SelectPromptFlow<T> : PromptFlow<T>
{
    private readonly string _prompt;
    private readonly SelectionModel<T> _model;

    internal SelectPromptFlow(string prompt, SelectionModel<T> model)
    {
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    protected override IComponent CreateComponent(PromptFlowContext<T> context)
    {
        var controller = new SelectionController<T>(_model)
        {
            Submitted = item => context.Submit(item.Value),
            Canceled = context.Cancel
        };
        var view = new SelectionView<T>(_model, controller);
        var stack = new Stack().Add(new Text(_prompt)).Add(view);
        return new PromptFlowShell<T>(new PromptFlowComponent<T>(stack, context), view);
    }
}
