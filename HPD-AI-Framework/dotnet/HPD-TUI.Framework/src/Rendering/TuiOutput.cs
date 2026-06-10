using HPD.TUI.Content;
using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

public static class TuiOutput
{
    public static string Render(IComponent component, TuiOutputOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(component);
        options ??= new TuiOutputOptions();
        Validate(options);

        return options.UseAnsi
            ? TuiCapture.RenderToAnsi(component, options.Width, options.Height, options.Theme, options.ColorSystem)
            : TuiCapture.RenderToString(component, options.Width, options.Height, options.Theme, options.TrimTrailingBlankLines, options.ColorSystem);
    }

    public static string Render(IContentBlock block, TuiOutputOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        return Render((IComponent)block, options);
    }

    public static void Write(ITerminal terminal, IComponent component, TuiOutputOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var rendered = Render(component, ResolveOptions(terminal, options));
        terminal.Write(rendered.AsSpan());
    }

    public static void Write(ITerminal terminal, IContentBlock block, TuiOutputOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        Write(terminal, (IComponent)block, options);
    }

    public static void Write(TextWriter writer, IComponent component, TuiOutputOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Render(component, options));
    }

    public static void Write(TextWriter writer, IContentBlock block, TuiOutputOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        Write(writer, (IComponent)block, options);
    }

    private static TuiOutputOptions ResolveOptions(ITerminal terminal, TuiOutputOptions? options)
    {
        var size = terminal.GetSize();
        if (options is null)
        {
            return new TuiOutputOptions { Width = size.Width, Height = size.Height };
        }

        return new TuiOutputOptions
        {
            Width = options.Width > 0 ? options.Width : size.Width,
            Height = options.Height > 0 ? options.Height : size.Height,
            UseAnsi = options.UseAnsi,
            TrimTrailingBlankLines = options.TrimTrailingBlankLines,
            Theme = options.Theme,
            ColorSystem = options.ColorSystem
        };
    }

    private static void Validate(TuiOutputOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Height);
    }
}
