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

    public string Value => Text.ToString();

    public void SetText(string value)
    {
        Text.Clear();
        Text.Append(value);
        Cursor = Text.Length;
    }

    public void AddPart(PromptPart part)
    {
        _parts.Add(part);
    }

    public void ClearParts()
    {
        _parts.Clear();
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
