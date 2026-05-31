namespace HPD.TUI.Core;

public sealed class Theme
{
    public static Theme Default { get; } = new();

    public Style Text { get; init; } = Style.Default;

    public Style Accent { get; init; } = new(Color.Cyan, Color.Default);

    public Style Blue { get; init; } = new(Color.Blue, Color.Default);

    public Style Border { get; init; } = new(Color.Gray, Color.Default);

    public Style Error { get; init; } = new(Color.Red, Color.Default);

    public Style Success { get; init; } = new(Color.Green, Color.Default);

    public Style Warning { get; init; } = new(Color.Yellow, Color.Default);
}
