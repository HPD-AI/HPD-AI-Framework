namespace HPD.TUI.Core;

public sealed class Theme
{
    public static Theme Default { get; } = new();

    public Style Text { get; init; } = new(Color.Default, Color.Default);

    public Style Accent { get; init; } = new(new Color(120, 170, 255), Color.Default);

    public Style Blue { get; init; } = new(new Color(120, 170, 255), Color.Default);

    public Style Border { get; init; } = new(new Color(105, 105, 105), Color.Default);

    public Style Error { get; init; } = new(new Color(220, 120, 120), Color.Default);

    public Style Success { get; init; } = new(new Color(120, 190, 140), Color.Default);

    public Style Warning { get; init; } = new(new Color(210, 175, 95), Color.Default);
}
