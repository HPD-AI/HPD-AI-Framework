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

    public void SetDraft(string value)
    {
        _model.SetText(value);
        _historyIndex = _history.Count;
        RefreshAutocomplete();
    }

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
            case KeyCode.Paste when key.Text is { Length: > 0 } text:
                InsertPaste(text);
                return true;
            case KeyCode.Backspace when _model.Cursor > 0:
                if (TryRemovePartBeforeCursor())
                {
                    RefreshAutocomplete();
                    return true;
                }

                var backspaceStart = _model.Cursor - 1;
                _model.RemoveText(backspaceStart, 1);
                _model.Cursor = backspaceStart;
                RefreshAutocomplete();
                return true;
            case KeyCode.Delete when _model.Cursor < _model.Text.Length:
                if (TryRemovePartAfterCursor())
                {
                    RefreshAutocomplete();
                    return true;
                }

                _model.RemoveText(_model.Cursor, 1);
                RefreshAutocomplete();
                return true;
            case KeyCode.LeftArrow when _model.Cursor > 0:
                _model.Cursor = GetPreviousCursor(_model.Cursor);
                RefreshAutocomplete();
                return true;
            case KeyCode.RightArrow when _model.Cursor < _model.Text.Length:
                _model.Cursor = GetNextCursor(_model.Cursor);
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
        var value = _model.SubmittedValue;
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
        _model.ClearParts();
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

        _model.InsertText(_model.Cursor, buffer[..written]);
        _historyIndex = _history.Count;
        RefreshAutocomplete();
    }

    private void InsertPaste(string text)
    {
        var wordCount = CountWords(text);
        var display = wordCount == 1
            ? "(pasted 1 word)"
            : $"(pasted {wordCount} words)";
        _model.InsertPart(_model.Cursor, display, PromptPartKind.PastedBlock, text);
        _historyIndex = _history.Count;
        RefreshAutocomplete();
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWord = false;
                continue;
            }

            if (inWord)
            {
                continue;
            }

            count++;
            inWord = true;
        }

        return count;
    }

    private bool TryRemovePartBeforeCursor()
    {
        foreach (var part in _model.Parts)
        {
            if (_model.Cursor == part.Start + part.Length)
            {
                _model.RemoveText(part.Start, part.Length);
                _model.Cursor = part.Start;
                return true;
            }
        }

        return false;
    }

    private bool TryRemovePartAfterCursor()
    {
        foreach (var part in _model.Parts)
        {
            if (_model.Cursor == part.Start)
            {
                _model.RemoveText(part.Start, part.Length);
                _model.Cursor = part.Start;
                return true;
            }
        }

        return false;
    }

    private int GetPreviousCursor(int cursor)
    {
        foreach (var part in _model.Parts)
        {
            if (cursor > part.Start && cursor <= part.Start + part.Length)
            {
                return part.Start;
            }
        }

        return cursor - 1;
    }

    private int GetNextCursor(int cursor)
    {
        foreach (var part in _model.Parts)
        {
            if (cursor >= part.Start && cursor < part.Start + part.Length)
            {
                return part.Start + part.Length;
            }
        }

        return cursor + 1;
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
