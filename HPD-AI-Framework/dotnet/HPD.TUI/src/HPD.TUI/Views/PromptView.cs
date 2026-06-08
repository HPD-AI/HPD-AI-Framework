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
        if (_model.ShowVisualCursor && IsFocused)
        {
            width++;
        }

        var height = 1;
        if (_controller.Autocomplete is { SuggestionCount: > 0 } autocomplete)
        {
            height += autocomplete.SuggestionCount;
        }

        return new Measurement(Math.Min(width, maxWidth), Math.Min(width, maxWidth), height);
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
        var visualCursor = _model.ShowVisualCursor && IsFocused;
        var availableWidth = visualCursor ? Math.Max(0, maxWidth - 1) : maxWidth;
        var visibleLength = Math.Min(availableWidth, _model.Text.Length - visibleStart);

        if (visualCursor)
        {
            RenderWithVisualCursor(in context, maxWidth, visibleStart, ref output);
        }
        else if (_model.Text.Length == 0 && _model.Placeholder.Length > 0)
        {
            WriteClipped(_model.Placeholder, maxWidth, context.Theme.Border, ref output);
        }
        else if (visibleLength > 0 && _model.MaskCharacter is { } mask)
        {
            WriteRepeated(mask, visibleLength, context.Theme.Text, ref output);
        }
        else if (visibleLength > 0)
        {
            output.Write(_model.Text, visibleStart, visibleLength, context.Theme.Text);
        }

        if (!visualCursor)
        {
            var used = _model.Text.Length == 0 && _model.Placeholder.Length > 0
                ? Math.Min(maxWidth, _model.Placeholder.Length)
                : visibleLength;
            WriteSpaces(maxWidth - used, context.Theme.Text, ref output);
        }

        if (IsFocused)
        {
            output.SetTerminalCursor(startX + Math.Clamp(_model.Cursor - visibleStart, 0, Math.Max(0, maxWidth - 1)), startY);
        }

        if (_controller.Autocomplete is { SuggestionCount: > 0 } autocomplete)
        {
            output.WriteLineBreak();
            for (var i = 0; i < autocomplete.SuggestionCount; i++)
            {
                var suggestion = autocomplete.GetSuggestion(i);
                var selected = i == autocomplete.SelectedIndex;
                var style = selected ? context.Theme.Accent : context.Theme.Text;
                output.Write(selected ? "> " : "  ", style);
                output.Write(suggestion.Title.AsSpan(), style);

                if (!string.IsNullOrEmpty(suggestion.Description))
                {
                    output.Write(" ", context.Theme.Border);
                    output.Write(suggestion.Description.AsSpan(), context.Theme.Border);
                }

                if (i < autocomplete.SuggestionCount - 1)
                {
                    output.WriteLineBreak();
                }
            }
        }
    }

    private void RenderWithVisualCursor(in RenderContext context, int maxWidth, int visibleStart, ref SegmentWriter output)
    {
        var used = 0;
        var cursorColumn = Math.Clamp(_model.Cursor - visibleStart, 0, Math.Max(0, maxWidth - 1));

        if (_model.Text.Length == 0)
        {
            output.Write(_model.VisualCursorCharacter, context.Theme.Accent);
            used++;
            if (_model.Placeholder.Length > 0 && used < maxWidth)
            {
                var length = Math.Min(maxWidth - used, _model.Placeholder.Length);
                output.Write(_model.Placeholder.AsSpan(0, length), context.Theme.Border);
                used += length;
            }

            WriteSpaces(maxWidth - used, context.Theme.Text, ref output);
            return;
        }

        var beforeLength = Math.Min(cursorColumn, Math.Max(0, _model.Text.Length - visibleStart));
        if (beforeLength > 0)
        {
            WritePromptText(visibleStart, beforeLength, in context, ref output);
            used += beforeLength;
        }

        if (used < maxWidth)
        {
            output.Write(_model.VisualCursorCharacter, context.Theme.Accent);
            used++;
        }

        var afterStart = visibleStart + beforeLength;
        var afterLength = Math.Min(maxWidth - used, Math.Max(0, _model.Text.Length - afterStart));
        if (afterLength > 0)
        {
            WritePromptText(afterStart, afterLength, in context, ref output);
            used += afterLength;
        }

        WriteSpaces(maxWidth - used, context.Theme.Text, ref output);
    }

    private void WritePromptText(int start, int length, in RenderContext context, ref SegmentWriter output)
    {
        if (_model.MaskCharacter is { } mask)
        {
            WriteRepeated(mask, length, context.Theme.Text, ref output);
            return;
        }

        output.Write(_model.Text, start, length, context.Theme.Text);
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
        bool multiline = false,
        bool visualCursor = false)
    {
        var model = new PromptModel
        {
            Placeholder = placeholder,
            IsMultiline = multiline,
            ShowVisualCursor = visualCursor
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
