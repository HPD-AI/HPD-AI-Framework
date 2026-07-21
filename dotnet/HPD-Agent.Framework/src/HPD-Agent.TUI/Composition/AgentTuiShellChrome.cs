using HPD.TUI.Layout;

namespace HPD.Agent.TUI.Composition;

public sealed class AgentTuiShellChrome
{
    public bool ShowSectionTitles { get; set; }

    public int Gap { get; set; } = 1;

    public ShellSectionChrome Header { get; set; } = ShellSectionChrome.Bare();

    public ShellSectionChrome Transcript { get; set; } = ShellSectionChrome.Bare();

    /// <summary>Gets or sets chrome for transient runtime activities.</summary>
    public ShellSectionChrome Activity { get; set; } = ShellSectionChrome.Bare();

    public ShellSectionChrome AboveEditor { get; set; } = ShellSectionChrome.Bare();

    /// <summary>Gets or sets chrome for the status line immediately above the prompt.</summary>
    public ShellSectionChrome PromptStatus { get; set; } = ShellSectionChrome.Bare();

    public ShellSectionChrome Prompt { get; set; } = ShellSectionChrome.Bare();

    public ShellSectionChrome BelowEditor { get; set; } = ShellSectionChrome.Bare();

    public ShellSectionChrome Footer { get; set; } = ShellSectionChrome.Bare();

    public AgentTuiDialogChrome Dialog { get; set; } = new();

    public int MinimumTranscriptHeight { get; set; } = 6;

    public int DefaultTranscriptHeight { get; set; } = 17;

    public AgentTuiShellChrome Clone()
        => new()
        {
            ShowSectionTitles = ShowSectionTitles,
            Gap = Gap,
            Header = Header.Clone(),
            Transcript = Transcript.Clone(),
            Activity = Activity.Clone(),
            AboveEditor = AboveEditor.Clone(),
            PromptStatus = PromptStatus.Clone(),
            Prompt = Prompt.Clone(),
            BelowEditor = BelowEditor.Clone(),
            Footer = Footer.Clone(),
            Dialog = Dialog.Clone(),
            MinimumTranscriptHeight = MinimumTranscriptHeight,
            DefaultTranscriptHeight = DefaultTranscriptHeight
        };
}

public sealed class AgentTuiDialogChrome
{
    public int X { get; set; }

    public int Y { get; set; } = 6;

    public int Width { get; set; }

    public int Height { get; set; } = 14;

    public AgentTuiDialogChrome Clone()
        => new()
        {
            X = X,
            Y = Y,
            Width = Width,
            Height = Height
        };
}

public sealed class ShellSectionChrome
{
    public ShellSectionDisplay Display { get; set; }

    public string? Title { get; set; }

    public BorderSpec Border { get; set; } = BorderSpec.None;

    public Thickness Padding { get; set; } = Thickness.None;

    public static ShellSectionChrome Bare()
        => new() { Display = ShellSectionDisplay.Bare };

    public static ShellSectionChrome Hidden()
        => new() { Display = ShellSectionDisplay.Hidden };

    public static ShellSectionChrome Separator(string? title)
        => new() { Display = ShellSectionDisplay.Separator, Title = title };

    public static ShellSectionChrome Frame(string? title, BorderSpec border)
        => new() { Display = ShellSectionDisplay.Frame, Title = title, Border = border };

    public ShellSectionChrome WithPadding(Thickness padding)
    {
        Padding = padding;
        return this;
    }

    public ShellSectionChrome Clone()
        => new()
        {
            Display = Display,
            Title = Title,
            Border = Border,
            Padding = Padding
        };
}

public enum ShellSectionDisplay
{
    Bare,
    Separator,
    Frame,
    Hidden
}
