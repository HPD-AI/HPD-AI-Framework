using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Models;
using HPD.TUI.Views;

namespace HPD.TUI.Flows;

public sealed class MultiSelectPromptFlow<T> : PromptFlow<IReadOnlyList<T>>
{
    private readonly string _prompt;
    private readonly MultiSelectionModel<T> _model;

    internal MultiSelectPromptFlow(string prompt, MultiSelectionModel<T> model)
    {
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    protected override IComponent CreateComponent(PromptFlowContext<IReadOnlyList<T>> context)
    {
        var controller = new MultiSelectionController<T>(_model)
        {
            CanSubmit = () =>
            {
                var count = _model.SelectedIndexes.Count;
                if (count < _model.MinSelected)
                {
                    context.SetValidationMessage($"Select at least {_model.MinSelected}.");
                    return false;
                }

                context.SetValidationMessage(null);
                return true;
            },
            Submitted = context.Submit,
            Canceled = context.Cancel
        };
        var view = new MultiSelectionView<T>(_model, controller);
        var stack = new Stack().Add(new Text(_prompt)).Add(view);
        return new PromptFlowShell<IReadOnlyList<T>>(new PromptFlowComponent<IReadOnlyList<T>>(stack, context), view);
    }
}
