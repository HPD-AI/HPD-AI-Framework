using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Views;

namespace HPD.TUI.Flows;

public sealed class TextPromptFlow : PromptFlow<string>
{
    private readonly string _prompt;
    private string _defaultValue = string.Empty;
    private bool _allowEmpty;
    private PromptValidator<string>? _validator;

    internal TextPromptFlow(string prompt)
    {
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
    }

    public TextPromptFlow Default(string value)
    {
        _defaultValue = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }

    public TextPromptFlow AllowEmpty(bool allowEmpty = true)
    {
        _allowEmpty = allowEmpty;
        return this;
    }

    public TextPromptFlow Validate(PromptValidator<string> validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        return this;
    }

    protected override IComponent CreateComponent(PromptFlowContext<string> context)
    {
        var model = new PromptModel { Placeholder = _prompt };
        if (_defaultValue.Length > 0)
        {
            model.SetText(_defaultValue);
        }

        var controller = new PromptController(model);
        controller.Submitting = value =>
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
        };
        controller.Canceled = context.Cancel;

        return new PromptFlowComponent<string>(new PromptView(model, controller), context);
    }
}
