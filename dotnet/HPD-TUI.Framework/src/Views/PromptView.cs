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

    /// <summary>Gets or sets the maximum number of autocomplete suggestions shown at once.</summary>
    public int MaximumSuggestionRows { get; set; } = 8;

    public bool IsFocused { get; set; }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var prefixWidth = GetPrefixWidth();
        var width = _model.ExpandToWidth
            ? maxWidth
            : _model.Text.Length == 0
                ? prefixWidth + _model.Placeholder.Length
                : Math.Min(maxWidth, LongestRenderedLine(maxWidth));
        if (_model.ShowVisualCursor && IsFocused)
        {
            width = Math.Min(maxWidth, width + 1);
        }

        var height = GetPaddingTop() + Math.Max(1, CountRenderedLines(maxWidth)) + GetPaddingBottom();
        if (_controller.Autocomplete is { SuggestionCount: > 0 } autocomplete)
        {
            height += GetVisibleSuggestionCount(autocomplete);
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
        var visualCursor = _model.ShowVisualCursor && IsFocused;
        var cursorSet = false;
        var fillStyle = GetFillStyle(in context);

        WriteTopPaddingRows(GetPaddingTop(), in context, maxWidth, ref output);

        startX = output.CursorX;
        startY = output.CursorY;
        var prefixWidth = WritePrefix(in context, maxWidth, ref output);

        if (_model.Text.Length == 0)
        {
            var used = prefixWidth;
            if (visualCursor)
            {
                output.Write(_model.VisualCursorCharacter, GetVisualCursorStyle(in context));
                output.SetTerminalCursor(startX + used, startY);
                cursorSet = true;
                used++;
            }

            if (IsFocused && !visualCursor && !cursorSet)
            {
                output.SetTerminalCursor(startX + used, startY);
                cursorSet = true;
            }

            if (_model.Placeholder.Length > 0 && used < maxWidth)
            {
                var length = Math.Min(maxWidth - used, _model.Placeholder.Length);
                output.Write(_model.Placeholder.AsSpan(0, length), GetPlaceholderStyle(in context));
                used += length;
            }

            WriteSpaces(maxWidth - used, fillStyle, ref output);
        }
        else
        {
            RenderWrappedText(
                in context,
                maxWidth,
                startX,
                startY,
                visualCursor,
                prefixWidth,
                ref cursorSet,
                ref output);
        }

        if (IsFocused && !cursorSet)
        {
            var cursor = GetCursorPoint(maxWidth);
            output.SetTerminalCursor(startX + cursor.X, startY + cursor.Y);
        }

        WriteBottomPaddingRows(GetPaddingBottom(), in context, maxWidth, ref output);

        if (_controller.Autocomplete is { SuggestionCount: > 0 } autocomplete)
        {
            var visibleCount = GetVisibleSuggestionCount(autocomplete);
            var firstVisibleIndex = Math.Clamp(
                autocomplete.SelectedIndex - visibleCount + 1,
                0,
                autocomplete.SuggestionCount - visibleCount);
            output.WriteLineBreak();
            for (var row = 0; row < visibleCount; row++)
            {
                var i = firstVisibleIndex + row;
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

                if (row < visibleCount - 1)
                {
                    output.WriteLineBreak();
                }
            }
        }
    }

    private int GetVisibleSuggestionCount(AutocompleteController autocomplete)
        => Math.Min(autocomplete.SuggestionCount, Math.Max(1, MaximumSuggestionRows));

    private void RenderWrappedText(
        in RenderContext context,
        int maxWidth,
        int startX,
        int startY,
        bool visualCursor,
        int prefixWidth,
        ref bool cursorSet,
        ref SegmentWriter output)
    {
        var column = prefixWidth;
        var line = 0;
        var partIndex = 0;
        for (var i = 0; i <= _model.Text.Length; i++)
        {
            if (visualCursor && i == _model.Cursor)
            {
                if (column == maxWidth)
                {
                    WriteSpaces(0, GetFillStyle(in context), ref output);
                    output.WriteLineBreak();
                    column = 0;
                    line++;
                }

                output.Write(_model.VisualCursorCharacter, GetVisualCursorStyle(in context));
                output.SetTerminalCursor(startX + column, startY + line);
                cursorSet = true;
                column++;
            }

            if (i == _model.Text.Length)
            {
                break;
            }

            var ch = _model.Text[i];
            if (ch == '\n')
            {
                WriteSpaces(maxWidth - column, GetFillStyle(in context), ref output);
                output.WriteLineBreak();
                column = 0;
                line++;
                continue;
            }

            if (column == maxWidth)
            {
                output.WriteLineBreak();
                column = 0;
                line++;
            }

            WritePromptCharacter(i, ref partIndex, in context, ref output);
            column++;
        }

        WriteSpaces(maxWidth - column, GetFillStyle(in context), ref output);
    }

    private void WritePromptCharacter(int index, ref int partIndex, in RenderContext context, ref SegmentWriter output)
    {
        if (_model.MaskCharacter is { } mask)
        {
            output.Write(mask, GetTextStyle(in context));
            return;
        }

        var style = GetStyle(index, ref partIndex, in context);
        output.Write(_model.Text[index], style);
    }

    public bool HandleInput(in TuiInputEvent key)
    {
        var keyEvent = key.KeyEvent;
        return _controller.HandleInput(in keyEvent);
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

    private Style GetStyle(int index, ref int partIndex, in RenderContext context)
    {
        while (partIndex < _model.Parts.Count &&
               index >= _model.Parts[partIndex].Start + _model.Parts[partIndex].Length)
        {
            partIndex++;
        }

        if (partIndex < _model.Parts.Count)
        {
            var part = _model.Parts[partIndex];
            if (index >= part.Start && index < part.Start + part.Length)
                return part.Kind == PromptPartKind.PastedBlock
                    ? context.Theme.Border
                    : context.Theme.Accent;
        }

        return GetTextStyle(in context);
    }

    private Style GetTextStyle(in RenderContext context)
        => _model.TextStyle ?? context.Theme.Text;

    private Style GetPrefixStyle(in RenderContext context)
        => _model.PrefixStyle ?? context.Theme.Accent;

    private Style GetPlaceholderStyle(in RenderContext context)
        => _model.PlaceholderStyle ?? context.Theme.Border;

    private Style GetVisualCursorStyle(in RenderContext context)
        => _model.VisualCursorStyle ?? context.Theme.Accent;

    private Style GetFillStyle(in RenderContext context)
        => _model.FillStyle ?? context.Theme.Text;

    private int CountRenderedLines(int maxWidth)
    {
        if (maxWidth <= 0 || _model.Text.Length == 0)
        {
            return 1;
        }

        var lines = 1;
        var column = GetPrefixWidth();
        var extraCursor = _model.ShowVisualCursor && IsFocused ? 1 : 0;
        for (var i = 0; i < _model.Text.Length + extraCursor; i++)
        {
            var isCursor = extraCursor == 1 && i == _model.Cursor;
            var ch = isCursor ? _model.VisualCursorCharacter : _model.Text[i > _model.Cursor && extraCursor == 1 ? i - 1 : i];
            if (ch == '\n')
            {
                lines++;
                column = 0;
                continue;
            }

            if (column == maxWidth)
            {
                lines++;
                column = 0;
            }

            column++;
        }

        return lines;
    }

    private int LongestRenderedLine(int maxWidth)
    {
        if (maxWidth <= 0)
        {
            return 0;
        }

        var longest = 0;
        var column = GetPrefixWidth();
        for (var i = 0; i < _model.Text.Length; i++)
        {
            var ch = _model.Text[i];
            if (ch == '\n')
            {
                longest = Math.Max(longest, column);
                column = 0;
                continue;
            }

            if (column == maxWidth)
            {
                longest = Math.Max(longest, column);
                column = 0;
            }

            column++;
        }

        return Math.Max(longest, column);
    }

    private (int X, int Y) GetCursorPoint(int maxWidth)
    {
        var column = GetPrefixWidth();
        var line = 0;
        for (var i = 0; i < _model.Cursor && i < _model.Text.Length; i++)
        {
            if (_model.Text[i] == '\n')
            {
                column = 0;
                line++;
                continue;
            }

            if (column == maxWidth)
            {
                column = 0;
                line++;
            }

            column++;
        }

        if (column == maxWidth)
        {
            column = 0;
            line++;
        }

        return (column, line);
    }

    private int WritePrefix(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0 || _model.Prefix.Length == 0)
        {
            return 0;
        }

        var length = Math.Min(maxWidth, _model.Prefix.Length);
        output.Write(_model.Prefix.AsSpan(0, length), GetPrefixStyle(in context));
        return length;
    }

    private int GetPrefixWidth()
        => _model.Prefix.Length;

    private int GetPaddingTop()
        => Math.Max(0, _model.PaddingTop);

    private int GetPaddingBottom()
        => Math.Max(0, _model.PaddingBottom);

    private void WriteTopPaddingRows(
        int count,
        in RenderContext context,
        int maxWidth,
        ref SegmentWriter output)
    {
        for (var i = 0; i < count; i++)
        {
            WriteSpaces(maxWidth, GetFillStyle(in context), ref output);
            output.WriteLineBreak();
        }
    }

    private void WriteBottomPaddingRows(
        int count,
        in RenderContext context,
        int maxWidth,
        ref SegmentWriter output)
    {
        for (var i = 0; i < count; i++)
        {
            output.WriteLineBreak();
            WriteSpaces(maxWidth, GetFillStyle(in context), ref output);
        }
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
