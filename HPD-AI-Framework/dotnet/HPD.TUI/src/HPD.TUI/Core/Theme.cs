namespace HPD.TUI.Core;

public sealed class Theme
{
    public static Theme Default { get; } = new();

    public Style Text { get; init; } = Style.Default;

    public Style Accent { get; init; } = new(Color.Cyan, Color.Black);

    public Style Blue { get; init; } = new(Color.Blue, Color.Black);

    public Style Border { get; init; } = new(Color.Gray, Color.Black);

    public Style Error { get; init; } = new(Color.Red, Color.Black);

    public Style Success { get; init; } = new(Color.Green, Color.Black);

    public Style Warning { get; init; } = new(Color.Yellow, Color.Black);
}
