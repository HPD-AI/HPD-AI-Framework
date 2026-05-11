using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;

namespace HPD.TUI.Views;

public sealed class PromptView : IFocusable
{
    private readonly PromptModel _model;
    private readonly PromptController _controller;

    public PromptView(PromptModel model, PromptController controller)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public PromptModel Model => _model;

    public PromptController Controller => _controller;

    public bool IsFocused { get; set; }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var width = _model.Text.Length == 0 ? _model.Placeholder.Length : _model.Text.Length;
        return new Measurement(Math.Min(width, maxWidth), Math.Min(width, maxWidth));
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var startX = output.CursorX;
        var startY = output.CursorY;
        var visibleStart = Math.Max(0, _model.Cursor - maxWidth + 1);
        var visibleLength = Math.Min(maxWidth, _model.Text.Length - visibleStart);

        if (_model.Text.Length == 0 && _model.Placeholder.Length > 0)
        {
            WriteClipped(_model.Placeholder, maxWidth, context.Theme.Border, ref output);
        }
        else if (visibleLength > 0 && _model.MaskCharacter is { } mask)
        {
            WriteRepeated(mask, visibleLength, context.Theme.Text, ref output);
        }
        else if (visibleLength > 0)
        {
            output.Write(_model.Text.ToString(visibleStart, visibleLength).AsSpan(), context.Theme.Text);
        }

        var used = _model.Text.Length == 0 && _model.Placeholder.Length > 0
            ? Math.Min(maxWidth, _model.Placeholder.Length)
            : visibleLength;
        WriteSpaces(maxWidth - used, context.Theme.Text, ref output);

        if (IsFocused)
        {
            output.SetTerminalCursor(startX + Math.Clamp(_model.Cursor - visibleStart, 0, Math.Max(0, maxWidth - 1)), startY);
        }

        if (_controller.Autocomplete is { Suggestions.Count: > 0 } autocomplete)
        {
            output.WriteLineBreak();
            for (var i = 0; i < autocomplete.Suggestions.Count; i++)
            {
                var suggestion = autocomplete.Suggestions[i];
                var selected = i == autocomplete.SelectedIndex;
                var style = selected ? context.Theme.Accent : context.Theme.Text;
                output.Write(selected ? "> " : "  ", style);
                output.Write(suggestion.Title.AsSpan(), style);

                if (!string.IsNullOrEmpty(suggestion.Description))
                {
                    output.Write(" ", context.Theme.Border);
                    output.Write(suggestion.Description.AsSpan(), context.Theme.Border);
                }

                if (i < autocomplete.Suggestions.Count - 1)
                {
                    output.WriteLineBreak();
                }
            }
        }
    }

    public void HandleInput(in KeyEvent key)
    {
        _controller.HandleInput(in key);
    }

    public void Invalidate()
    {
    }

    public static PromptView Create(
        string placeholder = "",
        Action<ReadOnlyMemory<char>>? submitted = null,
        AutocompleteController? autocomplete = null,
        bool multiline = false)
    {
        var model = new PromptModel
        {
            Placeholder = placeholder,
            IsMultiline = multiline
        };
        var controller = new PromptController(model)
        {
            Submitted = submitted,
            Autocomplete = autocomplete
        };

        autocomplete?.Refresh(model);
        return new PromptView(model, controller);
    }

    private static void WriteClipped(string value, int maxWidth, Style style, ref SegmentWriter output)
    {
        var length = Math.Min(value.Length, maxWidth);
        output.Write(value.AsSpan(0, length), style);
    }

    private static void WriteSpaces(int count, Style style, ref SegmentWriter output)
    {
        WriteRepeated(' ', count, style, ref output);
    }

    private static void WriteRepeated(char value, int count, Style style, ref SegmentWriter output)
    {
        if (count <= 0)
        {
            return;
        }

        Span<char> spaces = stackalloc char[Math.Min(count, 256)];
        spaces.Fill(value);
        while (count > 0)
        {
            var current = Math.Min(count, spaces.Length);
            output.Write(spaces[..current], style);
            count -= current;
        }
    }
}
