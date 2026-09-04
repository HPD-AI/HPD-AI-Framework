using HPD.TUI.Core;

namespace HPD.TUI.Flows;

public sealed class PromptFlowContext<T>
{
    private readonly Action<PromptResult<T>> _complete;

    internal PromptFlowContext(Action<PromptResult<T>> complete)
    {
        _complete = complete;
    }

    public string? ValidationMessage { get; private set; }

    public void Submit(T value)
    {
        ValidationMessage = null;
        _complete(PromptResult<T>.Submitted(value));
    }

    public void Cancel()
    {
        _complete(PromptResult<T>.Canceled());
    }

    public void Fail(string error)
    {
        _complete(PromptResult<T>.Failed(error));
    }

    public void SetValidationMessage(string? message)
    {
        ValidationMessage = message;
    }
}

internal sealed class PromptFlowComponent<T> : Component
{
    private readonly IComponent _inner;
    private readonly PromptFlowContext<T> _context;

    public PromptFlowComponent(IComponent inner, PromptFlowContext<T> context)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IComponent Inner => _inner;

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
    {
        var maxWidth = constraints.MaxWidth;
        var inner = _inner.Measure(in context, constraints);
        if (string.IsNullOrEmpty(_context.ValidationMessage))
        {
            return inner;
        }

        var messageWidth = Math.Min(maxWidth, _context.ValidationMessage.Length);
        return new Measurement(Math.Max(inner.MinWidth, messageWidth), Math.Max(inner.MaxWidth, messageWidth));
    }

    public override void Render(in RenderContext context, ref DisplayListBuilder output)
    {
        var maxWidth = output.MaxWidth;
        output.Render(_inner, in context, maxWidth);
        if (!string.IsNullOrEmpty(_context.ValidationMessage))
        {
            output.WriteLineBreak();
            output.Write(_context.ValidationMessage.AsSpan(0, Math.Min(maxWidth, _context.ValidationMessage.Length)), context.Theme.Error);
        }
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        return _inner.HandleInput(in key);
    }
}

internal sealed class PromptFlowShell<T> : Component, IPromptFlowFocusProvider
{
    private readonly IComponent _inner;
    private readonly IComponent _focus;

    public PromptFlowShell(IComponent inner, IComponent focus)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
    }

    public IComponent InitialFocus => _focus;

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) => _inner.Measure(in context, constraints);

    public override void Render(in RenderContext context, ref DisplayListBuilder output) => output.Render(_inner, in context, output.MaxWidth);

    public override bool HandleInput(in TuiInputEvent key) => _inner.HandleInput(in key);

}

internal interface IPromptFlowFocusProvider
{
    IComponent InitialFocus { get; }
}
