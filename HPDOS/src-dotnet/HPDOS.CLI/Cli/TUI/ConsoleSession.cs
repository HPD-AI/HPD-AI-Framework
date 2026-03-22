using Spectre.Console;
using Spectre.Console.Rendering;

namespace HPDOS.Shell.Cli.TUI;

public interface IConsoleSession
{
    IAnsiConsole Console { get; }
    IAnsiConsoleInput Input { get; }
    void HideCursor();
    void ShowCursor();
    void Write(IRenderable renderable);
    void WriteLine();
    void Markup(string markup);
    void MarkupLine(string markup);
    T Prompt<T>(IPrompt<T> prompt);
}

public sealed class SpectreConsoleSession(IAnsiConsole console) : IConsoleSession
{
    public static SpectreConsoleSession CreateDefault()
    {
        return new SpectreConsoleSession(AnsiConsole.Console);
    }

    public IAnsiConsole Console { get; } = console;

    public IAnsiConsoleInput Input => Console.Input;

    public void HideCursor()
    {
        Console.Cursor.Hide();
    }

    public void ShowCursor()
    {
        Console.Cursor.Show();
    }

    public void Write(IRenderable renderable)
    {
        Console.Write(renderable);
    }

    public void WriteLine()
    {
        Console.WriteLine();
    }

    public void Markup(string markup)
    {
        Console.Markup(markup);
    }

    public void MarkupLine(string markup)
    {
        Console.MarkupLine(markup);
    }

    public T Prompt<T>(IPrompt<T> prompt)
    {
        return prompt.Show(Console);
    }
}