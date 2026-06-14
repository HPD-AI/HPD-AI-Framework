using HPD.Agent.TUI.Models;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Utilities;

namespace HPD.Agent.TUI.Views;

public sealed class TranscriptView : IComponent
{
    private readonly TranscriptModel _model;
    private readonly List<TranscriptEntry> _entries = [];
    private readonly List<RenderedTranscriptEntry> _renderedEntries = [];
    private int _modelVersion = -1;
    private int _renderWidth;
    private Theme? _renderTheme;
    private ColorSystem _renderColorSystem;
    private int _totalRows;
    private bool _disposed;

    public TranscriptView(TranscriptModel model, int height = 15)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _model = model;
        Height = height;
    }

    public int Height { get; set; }

    public Measurement Measure(in RenderContext context, int maxWidth)
        => new(Math.Min(maxWidth, 20), maxWidth, Height);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        RenderRows(in context, maxWidth, ref output);
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    private void RenderRows(
        in RenderContext context,
        int maxWidth,
        ref SegmentWriter output)
    {
        if (Height <= 0 || maxWidth <= 0)
        {
            return;
        }

        RefreshCache(in context, maxWidth);
        if (_entries.Count == 0 || _totalRows <= 0)
        {
            return;
        }

        var maxOffset = Math.Max(0, _totalRows - Height);
        var offset = Math.Clamp(_model.ViewOffsetRowsFromBottom, 0, maxOffset);
        var startRow = Math.Max(0, _totalRows - Height - offset);
        var endRow = Math.Min(_totalRows, startRow + Height);
        var written = 0;
        for (var row = startRow; row < endRow && written < Height; row++)
        {
            if (written > 0)
            {
                output.WriteLineBreak();
            }

            WriteCachedRow(row, ref output);
            written++;
        }
    }

    private void RefreshCache(in RenderContext context, int maxWidth)
    {
        var modelVersion = _model.Version;
        if (modelVersion == _modelVersion &&
            maxWidth == _renderWidth &&
            ReferenceEquals(context.Theme, _renderTheme) &&
            context.ColorSystem == _renderColorSystem)
        {
            return;
        }

        _model.CopyTo(_entries);
        var renderIndex = 0;
        for (; renderIndex < _entries.Count; renderIndex++)
        {
            var entry = _entries[renderIndex];
            if (renderIndex < _renderedEntries.Count &&
                _renderedEntries[renderIndex].CanReuse(entry, maxWidth, context.Theme, context.ColorSystem))
            {
                continue;
            }

            if (renderIndex < _renderedEntries.Count)
            {
                _renderedEntries[renderIndex].Dispose();
                _renderedEntries[renderIndex] = RenderedTranscriptEntry.Create(entry, in context, maxWidth);
            }
            else
            {
                _renderedEntries.Add(RenderedTranscriptEntry.Create(entry, in context, maxWidth));
            }
        }

        while (_renderedEntries.Count > _entries.Count)
        {
            var last = _renderedEntries.Count - 1;
            _renderedEntries[last].Dispose();
            _renderedEntries.RemoveAt(last);
        }

        _totalRows = CalculateTotalRows(_renderedEntries);
        _modelVersion = modelVersion;
        _renderWidth = maxWidth;
        _renderTheme = context.Theme;
        _renderColorSystem = context.ColorSystem;
    }

    private void WriteCachedRow(int row, ref SegmentWriter output)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        if (row >= _totalRows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        var current = 0;
        for (var i = 0; i < _renderedEntries.Count; i++)
        {
            var entry = _renderedEntries[i];
            if (row < current + entry.LineCount)
            {
                entry.WriteLine(row - current, ref output);
                return;
            }

            current += entry.LineCount;
            if (i < _renderedEntries.Count - 1)
            {
                var spacing = _entries[i].VerticalSpacing;
                if (row < current + spacing)
                {
                    return;
                }

                current += spacing;
            }
        }
    }

    private int CalculateTotalRows(IReadOnlyList<RenderedTranscriptEntry> entries)
    {
        var total = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            total += entries[i].LineCount;
            if (i < entries.Count - 1)
            {
                total += _entries[i].VerticalSpacing;
            }
        }

        return total;
    }

    public void DisposeCache()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var i = 0; i < _renderedEntries.Count; i++)
        {
            _renderedEntries[i].Dispose();
        }

        _renderedEntries.Clear();
    }
}

internal sealed class RenderedTranscriptEntry : IDisposable
{
    private const int MaxEntryRenderHeight = 16_384;
    private readonly TerminalGrid _grid;

    private RenderedTranscriptEntry(
        TranscriptEntry source,
        int width,
        Theme theme,
        ColorSystem colorSystem,
        TerminalGrid grid,
        int lineCount)
    {
        Source = source;
        Width = width;
        Theme = theme;
        ColorSystem = colorSystem;
        _grid = grid;
        LineCount = lineCount;
    }

    public TranscriptEntry Source { get; }

    public int Width { get; }

    public Theme Theme { get; }

    public ColorSystem ColorSystem { get; }

    public int LineCount { get; }

    public static RenderedTranscriptEntry Create(
        TranscriptEntry entry,
        in RenderContext context,
        int maxWidth)
    {
        var grid = TuiCapture.RenderToGrid(
            new TranscriptCellView(entry),
            maxWidth,
            MaxEntryRenderHeight,
            context.Theme,
            context.ColorSystem,
            context.Elapsed);
        var lineCount = Math.Max(1, TuiCapture.GetUsedLineCount(grid));
        return new RenderedTranscriptEntry(entry, maxWidth, context.Theme, context.ColorSystem, grid, lineCount);
    }

    public bool CanReuse(
        TranscriptEntry source,
        int width,
        Theme theme,
        ColorSystem colorSystem)
        => ReferenceEquals(Source, source) &&
           Width == width &&
           ReferenceEquals(Theme, theme) &&
           ColorSystem == colorSystem;

    public void WriteLine(int line, ref SegmentWriter output)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        if (line >= LineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        TuiCapture.WriteLineTo(_grid, line, ref output);
    }

    public void Dispose()
        => _grid.Dispose();
}

internal sealed class TranscriptCellView : IComponent
{
    private readonly TranscriptEntry _entry;
    private readonly string _depthIndent;

    public TranscriptCellView(TranscriptEntry entry)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _depthIndent = new string(' ', Math.Max(0, _entry.Metadata.AgentDepth) * 2);
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
        => new(Math.Min(maxWidth, 20), maxWidth);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        switch (_entry.Cell)
        {
            case UserMessageCell cell:
                RenderPrefixed(cell.Body, "› ", "  ", useAccent: true, in context, maxWidth, ref output);
                break;
            case AssistantMessageCell cell:
                RenderAssistantMessage(cell, in context, maxWidth, ref output);
                break;
            case ReasoningMessageCell cell:
                RenderReasoningMessage(cell, in context, maxWidth, ref output);
                break;
            case NoticeCell cell:
                RenderNotice(cell, in context, maxWidth, ref output);
                break;
            case ToolCallCell cell:
                RenderToolCall(cell, in context, maxWidth, ref output);
                break;
            case CustomComponentCell cell:
                RenderCustom(cell, in context, maxWidth, ref output);
                break;
            default:
                output.Write(_entry.Cell.GetType().Name.AsSpan(), context.Theme.Text);
                break;
        }
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    private void RenderAssistantMessage(AssistantMessageCell cell, in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var name = string.IsNullOrWhiteSpace(cell.Name)
            ? _entry.Metadata.AgentName ?? "assistant"
            : cell.Name;
        output.Write(_depthIndent.AsSpan(), context.Theme.Text);
        output.Write(name.AsSpan(), new Style(Color.Default, Color.Default, TextAttributes.Bold));
        output.WriteLineBreak();
        RenderPrefixed(cell.Body, $"{_depthIndent}  ", $"{_depthIndent}  ", useAccent: false, in context, maxWidth, ref output);
    }

    private void RenderReasoningMessage(ReasoningMessageCell cell, in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        output.Write(_depthIndent.AsSpan(), context.Theme.Text);
        output.Write("reasoning", Muted);
        output.WriteLineBreak();
        var mutedContext = CreateMutedContext(in context);
        RenderPrefixed(cell.Body, $"{_depthIndent}  ", $"{_depthIndent}  ", useAccent: false, in mutedContext, maxWidth, ref output);
    }

    private void RenderNotice(NoticeCell cell, in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var prefix = cell.Severity switch
        {
            TranscriptSeverity.Error => "!! ",
            TranscriptSeverity.Warning => "! ",
            TranscriptSeverity.Success => "OK ",
            _ => "• "
        };
        var style = StyleForSeverity(cell.Severity);
        output.Write(_depthIndent.AsSpan(), context.Theme.Text);
        output.Write(prefix.AsSpan(), style);
        output.Write(cell.Title.AsSpan(), style);

        if (cell.Body is not null)
        {
            output.WriteLineBreak();
            RenderPrefixed(cell.Body, $"{_depthIndent}  ", $"{_depthIndent}  ", useAccent: false, in context, maxWidth, ref output);
        }
    }

    private void RenderToolCall(ToolCallCell cell, in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var stateStyle = StyleForRunState(cell.State);
        output.Write(_depthIndent.AsSpan(), context.Theme.Text);
        output.Write("• ", stateStyle);
        output.Write(cell.Name.AsSpan(), stateStyle);
        output.WriteLineBreak();

        if (cell.Summary is null)
        {
            RenderPrefixedText(FormatRunState(cell.State, cell.StateDetail), $"{_depthIndent}  └ ", $"{_depthIndent}    ", Muted, in context, maxWidth, ref output);
        }
        else
        {
            RenderPrefixed(cell.Summary, $"{_depthIndent}  └ ", $"{_depthIndent}    ", useAccent: false, in context, maxWidth, ref output);
        }

        if (cell.Detail is not null)
        {
            output.WriteLineBreak();
            RenderPrefixed(cell.Detail, $"{_depthIndent}  │ ", $"{_depthIndent}  │ ", useAccent: false, in context, maxWidth, ref output);
        }
    }

    private void RenderCustom(CustomComponentCell cell, in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var indent = $"{_depthIndent}{new string(' ', Math.Max(0, cell.Indent))}";
        output.Write(indent.AsSpan(), context.Theme.Text);
        output.Write(cell.Label.AsSpan(), new Style(Color.Default, Color.Default, TextAttributes.Bold));
        output.WriteLineBreak();
        RenderPrefixed(cell.Component, $"{indent}  ", $"{indent}  ", useAccent: false, in context, maxWidth, ref output);
    }

    private static void RenderPrefixed(
        IComponent body,
        string firstPrefix,
        string subsequentPrefix,
        bool useAccent,
        in RenderContext context,
        int maxWidth,
        ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var bodyWidth = Math.Max(1, maxWidth - Math.Max(UnicodeWidth.GetWidth(firstPrefix), UnicodeWidth.GetWidth(subsequentPrefix)));
        var style = useAccent ? context.Theme.Accent : context.Theme.Border;
        output.Write(firstPrefix.AsSpan(), style);

        var sink = new PrefixingSink(output.Sink, subsequentPrefix, style);
        var prefixedOutput = new SegmentWriter(sink);
        body.Render(in context, bodyWidth, ref prefixedOutput);
    }

    private static void RenderPrefixedText(
        string text,
        string firstPrefix,
        string subsequentPrefix,
        Style textStyle,
        in RenderContext context,
        int maxWidth,
        ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var bodyWidth = Math.Max(1, maxWidth - Math.Max(UnicodeWidth.GetWidth(firstPrefix), UnicodeWidth.GetWidth(subsequentPrefix)));
        output.Write(firstPrefix.AsSpan(), context.Theme.Border);
        var sink = new PrefixingSink(output.Sink, subsequentPrefix, context.Theme.Border);
        var prefixedOutput = new SegmentWriter(sink);
        WriteWrappedText(text, bodyWidth, textStyle, ref prefixedOutput);
    }

    private static void WriteWrappedText(string text, int maxWidth, Style style, ref SegmentWriter output)
    {
        if (maxWidth <= 0 || text.Length == 0)
        {
            return;
        }

        var lineStart = 0;
        var lineWidth = 0;
        var pos = 0;
        var enumerator = new RuneEnumerator(text);
        while (enumerator.MoveNext())
        {
            var rune = enumerator.Current;
            var runeLength = rune.Utf16SequenceLength;

            if (rune.Value is '\r')
            {
                pos += runeLength;
                continue;
            }

            if (rune.Value is '\n')
            {
                if (pos > lineStart)
                {
                    output.Write(text.AsSpan(lineStart, pos - lineStart), style);
                }

                output.WriteLineBreak();
                pos += runeLength;
                lineStart = pos;
                lineWidth = 0;
                continue;
            }

            var width = UnicodeWidth.GetWidth(rune);
            if (lineWidth > 0 && lineWidth + width > maxWidth)
            {
                output.Write(text.AsSpan(lineStart, pos - lineStart), style);
                output.WriteLineBreak();
                lineStart = pos;
                lineWidth = 0;
            }

            lineWidth += width;
            pos += runeLength;
        }

        if (pos > lineStart)
        {
            output.Write(text.AsSpan(lineStart, pos - lineStart), style);
        }
    }

    private static RenderContext CreateMutedContext(in RenderContext context)
        => new(
            context.Width,
            context.Height,
            new Theme
            {
                Text = Muted,
                Accent = Muted,
                Blue = Muted,
                Border = Muted,
                Error = context.Theme.Error,
                Success = context.Theme.Success,
                Warning = Muted
            },
            context.ColorSystem,
            context.Elapsed);

    private static string FormatRunState(TranscriptRunState state, string? detail)
    {
        var value = state switch
        {
            TranscriptRunState.Pending => "pending",
            TranscriptRunState.Running => "running",
            TranscriptRunState.Completed => "completed",
            TranscriptRunState.Failed => "failed",
            TranscriptRunState.Cancelled => "cancelled",
            TranscriptRunState.Backgrounded => "backgrounded",
            _ => state.ToString().ToLowerInvariant()
        };

        return string.IsNullOrWhiteSpace(detail) ? value : $"{value} {detail}";
    }

    private static Style StyleForRunState(TranscriptRunState state)
        => state switch
        {
            TranscriptRunState.Completed => Success,
            TranscriptRunState.Failed => Error,
            TranscriptRunState.Cancelled => Error,
            TranscriptRunState.Running => Accent,
            TranscriptRunState.Backgrounded => Accent,
            _ => Muted
        };

    private static Style StyleForSeverity(TranscriptSeverity severity)
        => severity switch
        {
            TranscriptSeverity.Success => Success,
            TranscriptSeverity.Warning => Accent,
            TranscriptSeverity.Error => Error,
            _ => Muted
        };

    private sealed class PrefixingSink : ISegmentSink
    {
        private readonly ISegmentSink _inner;
        private readonly string _prefix;
        private readonly Style _style;
        private bool _needsPrefix;

        public PrefixingSink(ISegmentSink inner, string prefix, Style style)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
            _style = style;
        }

        public int CursorX => _inner.CursorX;

        public int CursorY => _inner.CursorY;

        public bool Write(scoped ReadOnlySpan<char> text, Style style)
        {
            if (_needsPrefix)
            {
                _needsPrefix = false;
                if (!_inner.Write(_prefix.AsSpan(), _style))
                {
                    return false;
                }
            }

            return _inner.Write(text, style);
        }

        public bool WriteLineBreak()
        {
            _needsPrefix = true;
            return _inner.WriteLineBreak();
        }

        public void MoveTo(int x, int y)
        {
            _needsPrefix = x == 0;
            _inner.MoveTo(x, y);
        }

        public void SetTerminalCursor(int x, int y)
        {
            _inner.SetTerminalCursor(x, y);
        }
    }

    private static readonly Style Muted = new(Color.Gray, Color.Default);
    private static readonly Style Accent = new(Color.Cyan, Color.Default);
    private static readonly Style Success = new(Color.Green, Color.Default);
    private static readonly Style Error = new(Color.Red, Color.Default);
}
