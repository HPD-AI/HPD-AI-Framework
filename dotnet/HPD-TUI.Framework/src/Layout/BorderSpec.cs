using HPD.TUI.Core;

namespace HPD.TUI.Layout;

public readonly record struct BorderSpec(BorderGlyphs Glyphs, Style? Style = null)
{
    public static BorderSpec None => new(BorderGlyphs.None);

    public static BorderSpec Square => new(BorderGlyphs.Square);

    public static BorderSpec Rounded => new(BorderGlyphs.Rounded);

    public static BorderSpec Ascii => new(BorderGlyphs.Ascii);

    public bool IsVisible => Glyphs != BorderGlyphs.None;

    public Style ResolveStyle(in RenderContext context) => Style ?? context.Theme.Border;
}
