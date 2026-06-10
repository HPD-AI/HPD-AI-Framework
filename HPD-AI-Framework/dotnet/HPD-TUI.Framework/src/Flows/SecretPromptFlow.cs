using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Views;

namespace HPD.TUI.Flows;

public sealed class SecretPromptFlow : PromptFlow<string>
{
    private readonly string _prompt;
    private char _mask = '*';
    private bool _allowEmpty;
    private PromptValidator<string>? _validator;

    internal SecretPromptFlow(string prompt)
    {
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
    }

    public SecretPromptFlow Mask(char mask)
    {
        _mask = mask;
        return this;
    }

    public SecretPromptFlow AllowEmpty(bool allowEmpty = true)
    {
        _allowEmpty = allowEmpty;
        return this;
    }

    public SecretPromptFlow Validate(PromptValidator<string> validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        return this;
    }

    protected override IComponent CreateComponent(PromptFlowContext<string> context)
    {
        var model = new PromptModel { Placeholder = _prompt, MaskCharacter = _mask };
        var controller = new PromptController(model)
        {
            Submitting = value =>
            {
                var text = value.ToString();
                if (!_allowEmpty && text.Length == 0)
                {
                    context.SetValidationMessage("Value is required.");
                    return false;
                }

                if (_validator is not null)
                {
                    var validation = _validator(text);
                    if (!validation.IsValid)
                    {
                        context.SetValidationMessage(validation.Message ?? "Value is invalid.");
                        return false;
                    }
                }

                context.Submit(text);
                return true;
            },
            Canceled = context.Cancel
        };

        return new PromptFlowComponent<string>(new PromptView(model, controller), context);
    }
}
