namespace HPD.TUI.Core;

/// <summary>Defines the immutable semantic styles used by an HPD terminal surface.</summary>
public sealed record Theme
{
    /// <summary>Gets the default terminal theme.</summary>
    public static Theme Default { get; } = new();

    /// <summary>Gets the ordinary text style.</summary>
    public Style Text { get; init; } = new(Color.Default, Color.Default);

    /// <summary>Gets the primary accent style.</summary>
    public Style Accent { get; init; } = new(new Color(120, 170, 255), Color.Default);

    /// <summary>Gets the secondary blue style.</summary>
    public Style Blue { get; init; } = new(new Color(120, 170, 255), Color.Default);

    /// <summary>Gets the border and muted style.</summary>
    public Style Border { get; init; } = new(new Color(105, 105, 105), Color.Default);

    /// <summary>Gets the error style.</summary>
    public Style Error { get; init; } = new(new Color(220, 120, 120), Color.Default);

    /// <summary>Gets the success style.</summary>
    public Style Success { get; init; } = new(new Color(120, 190, 140), Color.Default);

    /// <summary>Gets the warning style.</summary>
    public Style Warning { get; init; } = new(new Color(210, 175, 95), Color.Default);

    /// <summary>Gets the structural identity used by width- and appearance-dependent caches.</summary>
    public ThemeKey Key => new(Text, Accent, Blue, Border, Error, Success, Warning);
}

/// <summary>Provides an exact structural identity for a <see cref="Theme"/>.</summary>
public readonly record struct ThemeKey(
    Style Text,
    Style Accent,
    Style Blue,
    Style Border,
    Style Error,
    Style Success,
    Style Warning);
