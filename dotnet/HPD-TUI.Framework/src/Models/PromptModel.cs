namespace HPD.TUI.Models;

public sealed class PromptModel
{
    private readonly List<PromptPart> _parts = [];

    public StringBuilder Text { get; } = new();

    public int Cursor { get; set; }

    public int SelectionStart { get; set; } = -1;

    public int SelectionLength { get; set; }

    public IReadOnlyList<PromptPart> Parts => _parts;

    public string Placeholder { get; set; } = "";

    public bool IsMultiline { get; set; }

    public char? MaskCharacter { get; set; }

    public bool ShowVisualCursor { get; set; }

    public char VisualCursorCharacter { get; set; } = '|';

    public string Value => Text.ToString();

    public string SubmittedValue
    {
        get
        {
            if (_parts.Count == 0)
            {
                return Value;
            }

            var builder = new StringBuilder();
            var offset = 0;
            foreach (var part in _parts)
            {
                if (part.Start < offset || part.Start > Text.Length)
                {
                    continue;
                }

                builder.Append(Text, offset, part.Start - offset);
                builder.Append(part.Value ?? Text.ToString(part.Start, Math.Min(part.Length, Text.Length - part.Start)));
                offset = Math.Min(Text.Length, part.Start + part.Length);
            }

            builder.Append(Text, offset, Text.Length - offset);
            return builder.ToString();
        }
    }

    public void SetText(string value)
    {
        Text.Clear();
        Text.Append(value);
        Cursor = Text.Length;
        ClearParts();
    }

    public void AddPart(PromptPart part)
    {
        AddPartSorted(part);
    }

    public void InsertPart(int start, string displayText, PromptPartKind kind, string? value = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        if (start > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        Text.Insert(start, displayText);
        AdjustPartsForInsertion(start, displayText.Length);
        AddPartSorted(new PromptPart(kind, start, displayText.Length, value));
        Cursor = start + displayText.Length;
    }

    public void InsertText(int start, ReadOnlySpan<char> text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        if (start > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        Text.Insert(start, text);
        AdjustPartsForInsertion(start, text.Length);
        Cursor = start + text.Length;
    }

    public void RemoveText(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > Text.Length || length > Text.Length - start)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Text.Remove(start, length);
        AdjustPartsForRemoval(start, length);
        Cursor = Math.Min(Cursor, Text.Length);
    }

    public void ClearParts()
    {
        _parts.Clear();
    }

    private void AdjustPartsForInsertion(int start, int length)
    {
        if (length <= 0)
        {
            return;
        }

        for (var i = _parts.Count - 1; i >= 0; i--)
        {
            var part = _parts[i];
            if (start <= part.Start)
            {
                _parts[i] = part with { Start = part.Start + length };
            }
            else if (start < part.Start + part.Length)
            {
                _parts.RemoveAt(i);
            }
        }
    }

    private void AdjustPartsForRemoval(int start, int length)
    {
        if (length <= 0)
        {
            return;
        }

        var end = start + length;
        for (var i = _parts.Count - 1; i >= 0; i--)
        {
            var part = _parts[i];
            var partEnd = part.Start + part.Length;
            if (partEnd <= start)
            {
                continue;
            }

            if (part.Start >= end)
            {
                _parts[i] = part with { Start = part.Start - length };
                continue;
            }

            _parts.RemoveAt(i);
        }
    }

    private void AddPartSorted(PromptPart part)
    {
        var index = _parts.Count;
        while (index > 0 && _parts[index - 1].Start > part.Start)
        {
            index--;
        }

        _parts.Insert(index, part);
    }
}

public readonly record struct PromptPart(PromptPartKind Kind, int Start, int Length, string? Value = null);

public enum PromptPartKind
{
    Text = 0,
    FileMention = 1,
    AgentMention = 2,
    PastedBlock = 3,
    CodeBlock = 4,
    Attachment = 5
}
