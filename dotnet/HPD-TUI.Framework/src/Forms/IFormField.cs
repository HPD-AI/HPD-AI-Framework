using HPD.TUI.Core;
using HPD.TUI.Flows;

namespace HPD.TUI.Forms;

[Flags]
public enum FormFieldInteraction
{
    None = 0,
    Change = 1,
    Edit = 2,
    Activate = 4
}

public interface IFormField
{
    event Action<IFormField>? ValueChanged;

    string Key { get; }

    string Label { get; }

    string? Description { get; }

    string? Error { get; }

    bool IsVisible { get; }

    bool IsEnabled { get; }

    bool IsRequired { get; }

    bool IsDirty { get; }

    bool IsEditing { get; }

    FormFieldInteraction Interaction { get; }

    string DisplayValue { get; }

    PromptValidationResult Validate();

    bool BeginEdit();

    bool AcceptEdit();

    bool CancelEdit();

    ValueTask WaitForPendingChangeAsync();

    bool HandleInput(in KeyEvent input);
}

public abstract class FormField<T> : IFormField
{
    private Func<bool>? _visibleWhen;
    private Func<bool>? _enabledWhen;

    public event Action<IFormField>? ValueChanged;

    protected FormField(
        string key,
        string label,
        string? description = null,
        bool isRequired = false)
    {
        Key = string.IsNullOrWhiteSpace(key)
            ? throw new ArgumentException("Key is required.", nameof(key))
            : key;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Description = description;
        IsRequired = isRequired;
    }

    public string Key { get; }

    public string Label { get; }

    public virtual string? Description { get; protected set; }

    public string? Error { get; protected set; }

    public bool IsVisible => _visibleWhen?.Invoke() ?? true;

    public bool IsEnabled => (_enabledWhen?.Invoke() ?? true) && IsVisible;

    public bool IsRequired { get; }

    public abstract bool IsDirty { get; }

    public virtual bool IsEditing { get; protected set; }

    public abstract FormFieldInteraction Interaction { get; }

    public abstract T Value { get; }

    public abstract string DisplayValue { get; }

    public FormField<T> VisibleWhen(Func<bool> predicate)
    {
        _visibleWhen = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    public FormField<T> EnabledWhen(Func<bool> predicate)
    {
        _enabledWhen = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    public abstract PromptValidationResult Validate();

    public virtual bool BeginEdit() => false;

    public virtual bool AcceptEdit() => false;

    public virtual bool CancelEdit() => false;

    public virtual ValueTask WaitForPendingChangeAsync() => ValueTask.CompletedTask;

    public abstract bool HandleInput(in KeyEvent input);

    protected void NotifyValueChanged()
        => ValueChanged?.Invoke(this);
}
