using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Flows;
using HPD.TUI.Models;

namespace HPD.TUI.Forms;

public sealed class SelectFormField<T> : IFormField
{
    private readonly CollectionNavigationController<T> _navigation;
    private readonly string? _initialKey;

    public SelectFormField(string key, string label, CollectionModel<T> model, string? help = null, bool isRequired = false)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Key is required.", nameof(key)) : key;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Help = help;
        IsRequired = isRequired;
        _navigation = new CollectionNavigationController<T>(Model);
        _initialKey = _navigation.ActiveItem?.Key;
    }

    public string Key { get; }

    public string Label { get; }

    public string? Help { get; }

    public string? Error { get; private set; }

    public bool IsRequired { get; }

    public bool IsDirty => !StringComparer.Ordinal.Equals(_initialKey, SelectedItem?.Key);

    public CollectionModel<T> Model { get; }

    public CollectionItem<T>? SelectedItem => _navigation.ActiveItem;

    public T? Value => SelectedItem is null ? default : SelectedItem.Value;

    public string DisplayValue => SelectedItem?.Title ?? string.Empty;

    public PromptValidationResult Validate()
    {
        if (IsRequired && SelectedItem is null)
        {
            Error = "Required.";
            return PromptValidationResult.Invalid(Error);
        }

        Error = null;
        return PromptValidationResult.Valid;
    }

    public bool HandleInput(in KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.LeftArrow:
                _navigation.Move(-1);
                return true;
            case KeyCode.RightArrow:
                _navigation.Move(1);
                return true;
            case KeyCode.Character when Model.AllowFilter:
                Model.AppendQuery(key.Character);
                _navigation.MoveFirst();
                return true;
            case KeyCode.Backspace when Model.AllowFilter:
                if (Model.BackspaceQuery())
                {
                    _navigation.MoveFirst();
                    return true;
                }

                return false;
            default:
                return false;
        }
    }
}

public sealed class MultiSelectFormField<T> : IFormField
{
    private readonly CollectionNavigationController<T> _navigation;
    private readonly MultiSelectionState<T> _selection;
    private readonly HashSet<string> _initialKeys;

    public MultiSelectFormField(
        string key,
        string label,
        CollectionModel<T> model,
        string? help = null,
        int minSelected = 0,
        int? maxSelected = null)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Key is required.", nameof(key)) : key;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Help = help;
        _navigation = new CollectionNavigationController<T>(Model);
        _selection = new MultiSelectionState<T> { MinSelected = minSelected, MaxSelected = maxSelected };
        _initialKeys = new HashSet<string>(_selection.SelectedKeys, StringComparer.Ordinal);
    }

    public string Key { get; }

    public string Label { get; }

    public string? Help { get; }

    public string? Error { get; private set; }

    public bool IsRequired => _selection.MinSelected > 0;

    public bool IsDirty => !_initialKeys.SetEquals(_selection.SelectedKeys);

    public CollectionModel<T> Model { get; }

    public IReadOnlySet<string> SelectedKeys => _selection.SelectedKeys;

    public IReadOnlyList<T> Value => _selection.GetSelectedValues(Model);

    public string DisplayValue => Value.Count == 0 ? string.Empty : $"{Value.Count} selected";

    public PromptValidationResult Validate()
    {
        if (_selection.SelectedKeys.Count < _selection.MinSelected)
        {
            Error = $"Select at least {_selection.MinSelected}.";
            return PromptValidationResult.Invalid(Error);
        }

        Error = null;
        return PromptValidationResult.Valid;
    }

    public bool HandleInput(in KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.LeftArrow:
                _navigation.Move(-1);
                return true;
            case KeyCode.RightArrow:
                _navigation.Move(1);
                return true;
            case KeyCode.Character when key.Character.Value == ' ':
                if (_navigation.ActiveItem is { } item)
                {
                    _selection.Toggle(item);
                    return true;
                }

                return false;
            case KeyCode.Character when Model.AllowFilter:
                Model.AppendQuery(key.Character);
                _navigation.MoveFirst();
                return true;
            case KeyCode.Backspace when Model.AllowFilter:
                if (Model.BackspaceQuery())
                {
                    _navigation.MoveFirst();
                    return true;
                }

                return false;
            default:
                return false;
        }
    }
}
