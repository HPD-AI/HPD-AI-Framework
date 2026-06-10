using HPD.TUI.Core;
using HPD.TUI.Models;

namespace HPD.TUI.Controllers;

public sealed class PromptController
{
    private readonly PromptModel _model;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;

    public PromptController(PromptModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public AutocompleteController? Autocomplete { get; set; }

    public Func<ReadOnlyMemory<char>, bool>? Submitting { get; set; }

    public Action<ReadOnlyMemory<char>>? Submitted { get; set; }

    public Action? Canceled { get; set; }

    public bool HandleInput(in KeyEvent key)
    {
        if (Autocomplete is { SuggestionCount: > 0 })
        {
            switch (key.Key)
            {
                case KeyCode.DownArrow:
                    Autocomplete.Move(1);
                    return true;
                case KeyCode.UpArrow:
                    Autocomplete.Move(-1);
                    return true;
                case KeyCode.Tab:
                    Autocomplete.Accept(_model);
                    return true;
                case KeyCode.Enter:
                    var submitOnAccept = Autocomplete.SelectedSuggestion?.SubmitOnAccept == true;
                    if (Autocomplete.Accept(_model) && submitOnAccept)
                    {
                        Submit();
                    }

                    return true;
            }
        }

        switch (key.Key)
        {
            case KeyCode.Character:
                Insert(key.Character);
                return true;
            case KeyCode.Backspace when _model.Cursor > 0:
                _model.Text.Remove(_model.Cursor - 1, 1);
                _model.Cursor--;
                RefreshAutocomplete();
                return true;
            case KeyCode.Delete when _model.Cursor < _model.Text.Length:
                _model.Text.Remove(_model.Cursor, 1);
                RefreshAutocomplete();
                return true;
            case KeyCode.LeftArrow when _model.Cursor > 0:
                _model.Cursor--;
                RefreshAutocomplete();
                return true;
            case KeyCode.RightArrow when _model.Cursor < _model.Text.Length:
                _model.Cursor++;
                RefreshAutocomplete();
                return true;
            case KeyCode.Home:
                _model.Cursor = 0;
                RefreshAutocomplete();
                return true;
            case KeyCode.End:
                _model.Cursor = _model.Text.Length;
                RefreshAutocomplete();
                return true;
            case KeyCode.Enter when _model.IsMultiline && key.Modifiers.HasFlag(KeyModifiers.Shift):
                Insert(new Rune('\n'));
                return true;
            case KeyCode.Enter:
                Submit();
                return true;
            case KeyCode.Escape:
                Canceled?.Invoke();
                return true;
            case KeyCode.UpArrow:
                NavigateHistory(-1);
                return true;
            case KeyCode.DownArrow:
                NavigateHistory(1);
                return true;
            default:
                return false;
        }
    }

    public void Submit()
    {
        var value = _model.Value;
        if (value.Length > 0)
        {
            _history.Add(value);
            _historyIndex = _history.Count;
        }

        var memory = value.AsMemory();
        if (Submitting?.Invoke(memory) == false)
        {
            return;
        }

        Submitted?.Invoke(memory);
        _model.Text.Clear();
        _model.Cursor = 0;
        RefreshAutocomplete();
    }

    private void Insert(Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        if (!rune.TryEncodeToUtf16(buffer, out var written))
        {
            return;
        }

        _model.Text.Insert(_model.Cursor, buffer[..written]);
        _model.Cursor += written;
        _historyIndex = _history.Count;
        RefreshAutocomplete();
    }

    private void NavigateHistory(int delta)
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (_historyIndex < 0)
        {
            _historyIndex = _history.Count;
        }

        _historyIndex = Math.Clamp(_historyIndex + delta, 0, _history.Count);
        if (_historyIndex == _history.Count)
        {
            _model.SetText("");
            return;
        }

        _model.SetText(_history[_historyIndex]);
    }

    private void RefreshAutocomplete()
    {
        Autocomplete?.Refresh(_model);
    }
}
