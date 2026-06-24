using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Rendering;

namespace HPD.TUI.Flows;

public abstract class PromptFlow<T>
{
    public async Task<PromptResult<T>> RunAsync(
        TuiApplication app,
        TuiRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        var session = new ModalSession<T>();
        var context = new PromptFlowContext<T>(session.Complete);
        var component = CreateComponent(context);
        var focus = GetInitialFocus(component);
        return await session.RunAsync(app, component, focus, options, cancellationToken).ConfigureAwait(false);
    }

    public IComponent CreateComponentForTesting(Action<PromptResult<T>> complete)
    {
        var context = new PromptFlowContext<T>(complete);
        return CreateComponent(context);
    }

    protected abstract IComponent CreateComponent(PromptFlowContext<T> context);

    protected virtual IComponent GetInitialFocus(IComponent component)
    {
        if (component is IPromptFlowFocusProvider provider)
        {
            return provider.InitialFocus;
        }

        return component is PromptFlowComponent<T> wrapped ? wrapped.Inner : component;
    }
}

public static class PromptFlow
{
    public static TextPromptFlow Text(string prompt) => new(prompt);

    public static SecretPromptFlow Secret(string prompt) => new(prompt);

    public static ConfirmPromptFlow Confirm(string prompt) => new(prompt);

    public static SelectPromptFlow<T> Select<T>(string prompt, IEnumerable<T> values, Func<T, string> titleSelector)
    {
        return new SelectPromptFlow<T>(prompt, SelectionModel<T>.From(values, titleSelector));
    }

    public static SelectPromptFlow<T> Select<T>(string prompt, SelectionModel<T> model) => new(prompt, model);

    public static MultiSelectPromptFlow<T> MultiSelect<T>(string prompt, IEnumerable<T> values, Func<T, string> titleSelector)
    {
        return new MultiSelectPromptFlow<T>(prompt, MultiSelectionModel<T>.From(values, titleSelector));
    }

    public static MultiSelectPromptFlow<T> MultiSelect<T>(string prompt, MultiSelectionModel<T> model) => new(prompt, model);
}
