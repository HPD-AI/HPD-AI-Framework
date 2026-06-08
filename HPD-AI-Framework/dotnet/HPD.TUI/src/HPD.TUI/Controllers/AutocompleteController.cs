using System.Text;
using HPD.TUI.Models;

namespace HPD.TUI.Controllers;

public sealed class AutocompleteController
{
    private readonly List<IAutocompleteProvider> _providers = [];
    private readonly List<AutocompleteEntry> _entries = [];
    private readonly AutocompleteRequest _activeRequest = new();
    private readonly AutocompleteSuggestionSink _sink;
    private int _selectedIndex;

    public AutocompleteController()
    {
        _sink = new AutocompleteSuggestionSink(this);
    }

    public int SuggestionCount => _entries.Count;

    public int SelectedIndex => _selectedIndex;

    public AutocompleteSuggestion? SelectedSuggestion =>
        _entries.Count == 0 ? null : _entries[Math.Clamp(_selectedIndex, 0, _entries.Count - 1)].Suggestion;

    public AutocompleteSuggestion GetSuggestion(int index) => _entries[index].Suggestion;

    public AutocompleteController Register(IAutocompleteProvider provider)
    {
        _providers.Add(provider ?? throw new ArgumentNullException(nameof(provider)));
        return this;
    }

    public bool Refresh(PromptModel prompt)
        => RefreshAsync(prompt).AsTask().GetAwaiter().GetResult();

    public async ValueTask<bool> RefreshAsync(
        PromptModel prompt,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _entries.Clear();
        _activeRequest.Update(prompt, force, cancellationToken);
        _selectedIndex = 0;

        if (_activeRequest.Trigger is null &&
            !force &&
            !_activeRequest.TextStartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var provider in _providers)
        {
            _sink.Reset(provider);
            await provider.GetSuggestionsAsync(_activeRequest, _sink, cancellationToken).ConfigureAwait(false);
        }

        return _entries.Count > 0;
    }

    public bool Move(int delta)
    {
        if (_entries.Count == 0 || delta == 0)
        {
            return false;
        }

        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _entries.Count - 1);
        return true;
    }

    public bool Accept(PromptModel prompt)
        => AcceptAsync(prompt).AsTask().GetAwaiter().GetResult();

    public async ValueTask<bool> AcceptAsync(PromptModel prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        if (_entries.Count == 0)
        {
            return false;
        }

        var entry = _entries[Math.Clamp(_selectedIndex, 0, _entries.Count - 1)];
        var replacement = GetReplacement(_activeRequest, entry);
        var context = new AutocompleteCompletionContext(_activeRequest, entry.Suggestion, replacement);
        var edit = await entry.Provider.ApplyCompletionAsync(context, cancellationToken).ConfigureAwait(false);

        ApplyEdit(prompt, edit);
        await RefreshAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void AddSuggestion(
        IAutocompleteProvider provider,
        AutocompleteSuggestion suggestion,
        AutocompleteReplacement? replacement)
        => _entries.Add(new AutocompleteEntry(provider, suggestion, replacement));

    private static void ApplyEdit(PromptModel prompt, AutocompleteEdit edit)
    {
        var start = Math.Clamp(edit.Start, 0, prompt.Text.Length);
        var length = Math.Clamp(edit.Length, 0, prompt.Text.Length - start);
        prompt.Text.Remove(start, length);
        if (edit.InsertText.Length > 0)
        {
            prompt.Text.Insert(start, edit.InsertText);
        }

        prompt.Cursor = Math.Clamp(edit.Cursor, 0, prompt.Text.Length);
    }

    private static AutocompleteReplacement GetReplacement(
        AutocompleteRequest request,
        AutocompleteEntry entry)
    {
        if (entry.Suggestion.ReplacementStart is { } suggestionStart)
        {
            return new AutocompleteReplacement(
                suggestionStart,
                entry.Suggestion.ReplacementLength ?? Math.Max(0, request.Cursor - suggestionStart));
        }

        if (entry.Replacement is { } resultReplacement)
        {
            return resultReplacement;
        }

        if (request.Trigger is { } trigger)
        {
            return new AutocompleteReplacement(trigger.Start, trigger.Length);
        }

        return new AutocompleteReplacement(request.Cursor, 0);
    }

    private readonly record struct AutocompleteEntry(
        IAutocompleteProvider Provider,
        AutocompleteSuggestion Suggestion,
        AutocompleteReplacement? Replacement);

    public sealed class AutocompleteSuggestionSink : IAutocompleteSuggestionSink
    {
        private readonly AutocompleteController _controller;
        private IAutocompleteProvider? _provider;

        internal AutocompleteSuggestionSink(AutocompleteController controller)
        {
            _controller = controller;
        }

        internal void Reset(IAutocompleteProvider provider)
        {
            _provider = provider;
        }

        public void Add(AutocompleteSuggestion suggestion, AutocompleteReplacement? replacement = null)
        {
            if (_provider is null)
            {
                throw new InvalidOperationException("Autocomplete suggestion sink is not active.");
            }

            _controller.AddSuggestion(_provider, suggestion, replacement);
        }
    }
}

public interface IAutocompleteSuggestionSink
{
    void Add(AutocompleteSuggestion suggestion, AutocompleteReplacement? replacement = null);
}

public interface IAutocompleteProvider
{
    ValueTask GetSuggestionsAsync(
        AutocompleteRequest request,
        IAutocompleteSuggestionSink suggestions,
        CancellationToken cancellationToken = default);

    ValueTask<AutocompleteEdit> ApplyCompletionAsync(
        AutocompleteCompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var replacement = context.Replacement;
        var insertText = context.Suggestion.InsertText;
        return ValueTask.FromResult(new AutocompleteEdit(
            replacement.Start,
            replacement.Length,
            insertText,
            replacement.Start + insertText.Length));
    }
}

public sealed class AutocompleteRequest
{
    private StringBuilder _text = new();

    public AutocompleteRequest()
    {
    }

    public AutocompleteRequest(
        string text,
        int cursor,
        bool isForced = false,
        AutocompleteTrigger? trigger = null,
        CancellationToken cancellationToken = default)
    {
        _text = new StringBuilder(text ?? throw new ArgumentNullException(nameof(text)));
        Cursor = Math.Clamp(cursor, 0, _text.Length);
        IsForced = isForced;
        Trigger = trigger ?? FindTrigger(_text, Cursor);
        CancellationToken = cancellationToken;
        CalculateCursorPosition(_text, Cursor, out var cursorLine, out var cursorColumn);
        CursorLine = cursorLine;
        CursorColumn = cursorColumn;
    }

    public StringBuilder Text => _text;

    public int TextLength => _text.Length;

    public int Cursor { get; private set; }

    public int CursorLine { get; private set; }

    public int CursorColumn { get; private set; }

    public bool IsForced { get; private set; }

    public AutocompleteTrigger? Trigger { get; private set; }

    public CancellationToken CancellationToken { get; private set; }

    public char this[int index] => _text[index];

    internal void Update(PromptModel prompt, bool isForced, CancellationToken cancellationToken)
    {
        _text = prompt.Text;
        Cursor = Math.Clamp(prompt.Cursor, 0, _text.Length);
        IsForced = isForced;
        Trigger = FindTrigger(_text, Cursor);
        CancellationToken = cancellationToken;
        CalculateCursorPosition(_text, Cursor, out var cursorLine, out var cursorColumn);
        CursorLine = cursorLine;
        CursorColumn = cursorColumn;
    }

    public bool TextStartsWith(string value, StringComparison comparison)
        => SliceEquals(0, value.Length, value, comparison);

    public int IndexOf(char value, int start, int length)
    {
        var end = Math.Min(_text.Length, start + length);
        for (var i = Math.Max(0, start); i < end; i++)
        {
            if (_text[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    public bool SliceStartsWith(int start, int length, string value, StringComparison comparison)
    {
        if (value.Length > length || start < 0 || start + value.Length > _text.Length)
        {
            return false;
        }

        return SliceEquals(start, value.Length, value, comparison);
    }

    public bool SliceIsPrefixOf(int start, int length, string value, StringComparison comparison)
    {
        if (length > value.Length || start < 0 || start + length > _text.Length)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            if (!CharsEqual(_text[start + i], value[i], comparison))
            {
                return false;
            }
        }

        return true;
    }

    public bool SliceEquals(int start, int length, string value, StringComparison comparison)
    {
        if (value.Length != length || start < 0 || start + length > _text.Length)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            if (!CharsEqual(_text[start + i], value[i], comparison))
            {
                return false;
            }
        }

        return true;
    }

    public string GetText(int start, int length)
        => _text.ToString(start, length);

    public string GetTriggerQuery()
        => Trigger is { } trigger ? GetText(trigger.QueryStart, trigger.QueryLength) : string.Empty;

    public bool TriggerQueryEquals(string value, StringComparison comparison)
        => Trigger is { } trigger && SliceEquals(trigger.QueryStart, trigger.QueryLength, value, comparison);

    public bool TriggerQueryStartsWith(string value, StringComparison comparison)
        => Trigger is { } trigger && SliceStartsWith(trigger.QueryStart, trigger.QueryLength, value, comparison);

    private static void CalculateCursorPosition(StringBuilder text, int cursor, out int cursorLine, out int cursorColumn)
    {
        cursorLine = 0;
        cursorColumn = 0;
        for (var i = 0; i < cursor; i++)
        {
            if (text[i] == '\n')
            {
                cursorLine++;
                cursorColumn = 0;
                continue;
            }

            cursorColumn++;
        }
    }

    private static AutocompleteTrigger? FindTrigger(StringBuilder text, int cursor)
    {
        if (cursor <= 0 || cursor > text.Length)
        {
            return null;
        }

        var index = cursor - 1;
        while (index >= 0 && !char.IsWhiteSpace(text[index]))
        {
            if (text[index] is '/' or '@' or '#')
            {
                var queryStart = index + 1;
                return new AutocompleteTrigger(text[index], index, cursor - index, queryStart, cursor - queryStart);
            }

            index--;
        }

        return null;
    }

    private static bool CharsEqual(char left, char right, StringComparison comparison)
        => comparison switch
        {
            StringComparison.Ordinal => left == right,
            StringComparison.OrdinalIgnoreCase => char.ToUpperInvariant(left) == char.ToUpperInvariant(right),
            StringComparison.InvariantCultureIgnoreCase => char.ToUpperInvariant(left) == char.ToUpperInvariant(right),
            StringComparison.CurrentCultureIgnoreCase => char.ToUpper(left) == char.ToUpper(right),
            _ => left == right
        };
}

public readonly record struct AutocompleteTrigger(
    char Marker,
    int Start,
    int Length,
    int QueryStart,
    int QueryLength);

public readonly record struct AutocompleteReplacement(int Start, int Length);

public sealed class AutocompleteCompletionContext
{
    public AutocompleteCompletionContext(
        AutocompleteRequest request,
        AutocompleteSuggestion suggestion,
        AutocompleteReplacement replacement)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Suggestion = suggestion;
        Replacement = replacement;
    }

    public AutocompleteRequest Request { get; }

    public AutocompleteSuggestion Suggestion { get; }

    public AutocompleteReplacement Replacement { get; }
}

public readonly record struct AutocompleteEdit(int Start, int Length, string InsertText, int Cursor);

public readonly record struct AutocompleteSuggestion(
    string Title,
    string InsertText,
    string? Description = null,
    int? ReplacementStart = null,
    int? ReplacementLength = null,
    bool SubmitOnAccept = false);
