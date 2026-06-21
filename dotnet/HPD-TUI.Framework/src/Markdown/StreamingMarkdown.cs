using System.Collections.Concurrent;
using System.Text;

namespace HPD.TUI.Markdown;

public interface IMarkdownRenderer<TRenderable>
{
    TRenderable Render(string markdown);
}

public static class MarkdownParser
{
    public static int FindLastSafeSplitPoint(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 0;
        }

        if (IsInsideCodeBlock(content, content.Length))
        {
            var blockStart = FindLastCodeBlockStart(content);
            return blockStart > 0 ? FindLastSafeSplitPoint(content[..blockStart]) : 0;
        }

        var paragraphBreak = FindLastParagraphBreak(content);
        if (paragraphBreak > 0)
        {
            return paragraphBreak;
        }

        var newline = FindLastSafeNewline(content);
        return newline > 0 ? newline : content.Length;
    }

    public static bool IsInsideCodeBlock(string content, int position)
    {
        var length = Math.Min(position, content.Length);
        return CountCodeFences(content.AsSpan(0, length)) % 2 == 1;
    }

    private static int CountCodeFences(ReadOnlySpan<char> content)
    {
        var count = 0;
        var index = 0;

        while (index < content.Length)
        {
            var fence = content[index..].IndexOf("```", StringComparison.Ordinal);
            if (fence < 0)
            {
                break;
            }

            count++;
            index += fence + 3;
        }

        return count;
    }

    private static int FindLastCodeBlockStart(string content)
    {
        var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence < 0)
        {
            return -1;
        }

        if (CountCodeFences(content.AsSpan(0, lastFence + 3)) % 2 == 1)
        {
            return lastFence;
        }

        var searchFrom = lastFence - 1;
        while (searchFrom >= 0)
        {
            var previous = content.LastIndexOf("```", searchFrom, StringComparison.Ordinal);
            if (previous < 0)
            {
                break;
            }

            if (CountCodeFences(content.AsSpan(0, previous + 3)) % 2 == 1)
            {
                return previous;
            }

            searchFrom = previous - 1;
        }

        return -1;
    }

    private static int FindLastParagraphBreak(string content)
    {
        var searchFrom = content.Length - 1;
        while (searchFrom >= 1)
        {
            var breakIndex = content.LastIndexOf("\n\n", searchFrom, StringComparison.Ordinal);
            if (breakIndex < 0)
            {
                break;
            }

            var splitPoint = breakIndex + 2;
            if (!IsInsideCodeBlock(content, splitPoint))
            {
                return splitPoint;
            }

            searchFrom = breakIndex - 1;
        }

        return -1;
    }

    private static int FindLastSafeNewline(string content)
    {
        if (content.Length < 50)
        {
            return -1;
        }

        var searchFrom = content.Length - 10;
        while (searchFrom >= 0)
        {
            var newline = content.LastIndexOf('\n', searchFrom);
            if (newline < 0)
            {
                break;
            }

            var splitPoint = newline + 1;
            if (!IsInsideCodeBlock(content, splitPoint) && !IsInsideList(content, splitPoint))
            {
                return splitPoint;
            }

            searchFrom = newline - 1;
        }

        return -1;
    }

    private static bool IsInsideList(string content, int position)
    {
        if (position >= content.Length)
        {
            return false;
        }

        var lastLine = GetLastLine(content.AsSpan(0, position));
        var nextLine = GetFirstLine(content.AsSpan(position));
        var lastIsListItem = IsListItem(lastLine);
        var nextIsListItem = IsListItem(nextLine);

        return (lastIsListItem && nextIsListItem) ||
               (lastIsListItem && nextLine.StartsWith("  ", StringComparison.Ordinal));
    }

    private static bool IsListItem(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("- ", StringComparison.Ordinal) ||
               trimmed.StartsWith("* ", StringComparison.Ordinal) ||
               trimmed.StartsWith("+ ", StringComparison.Ordinal) ||
               IsOrderedListItem(trimmed);
    }

    private static bool IsOrderedListItem(ReadOnlySpan<char> line)
    {
        var pos = 0;
        while (pos < line.Length && char.IsAsciiDigit(line[pos]))
        {
            pos++;
        }

        return pos > 0 &&
               pos + 1 < line.Length &&
               line[pos] == '.' &&
               char.IsWhiteSpace(line[pos + 1]);
    }

    private static ReadOnlySpan<char> GetLastLine(ReadOnlySpan<char> content)
    {
        var index = content.LastIndexOf('\n');
        return index < 0 ? content : content[(index + 1)..];
    }

    private static ReadOnlySpan<char> GetFirstLine(ReadOnlySpan<char> content)
    {
        var index = content.IndexOf('\n');
        return index < 0 ? content : content[..index];
    }
}

public sealed class StreamCollector<TRenderable>
{
    private readonly IMarkdownRenderer<TRenderable>? _renderer;
    private readonly StringBuilder _buffer = new();
    private readonly ConcurrentQueue<TRenderable> _pendingLines = new();
    private int _lastCommittedPosition;

    public StreamCollector(IMarkdownRenderer<TRenderable>? renderer = null)
    {
        _renderer = renderer;
    }

    public bool HasQueuedLines => !_pendingLines.IsEmpty;

    public bool IsInsideCodeBlock => MarkdownParser.IsInsideCodeBlock(_buffer.ToString(), _buffer.Length);

    public string Content => _buffer.ToString();

    public bool HasCompleteLines
    {
        get
        {
            var content = _buffer.ToString();
            if (content.Length <= _lastCommittedPosition)
            {
                return false;
            }

            var uncommitted = content.AsSpan(_lastCommittedPosition);
            if (uncommitted.IndexOf('\n') < 0)
            {
                return false;
            }

            return !MarkdownParser.IsInsideCodeBlock(content, content.Length) &&
                   MarkdownParser.FindLastSafeSplitPoint(content) > _lastCommittedPosition;
        }
    }

    public void Push(string delta)
    {
        if (!string.IsNullOrEmpty(delta))
        {
            _buffer.Append(delta);
        }
    }

    public void CommitCompleteLines()
    {
        var content = _buffer.ToString();
        var splitPoint = MarkdownParser.FindLastSafeSplitPoint(content);
        if (splitPoint <= _lastCommittedPosition)
        {
            return;
        }

        var newContent = content[_lastCommittedPosition..splitPoint];
        if (!string.IsNullOrWhiteSpace(newContent) && _renderer is not null)
        {
            _pendingLines.Enqueue(_renderer.Render(newContent));
        }

        _lastCommittedPosition = splitPoint;
    }

    public IReadOnlyList<TRenderable> GetQueuedLines()
    {
        var lines = new List<TRenderable>();
        while (_pendingLines.TryDequeue(out var line))
        {
            lines.Add(line);
        }

        return lines;
    }

    public TRenderable? DequeueNextLine() => _pendingLines.TryDequeue(out var line) ? line : default;

    public IReadOnlyList<TRenderable> Finalize()
    {
        var content = _buffer.ToString();
        var remaining = new List<TRenderable>();

        while (_pendingLines.TryDequeue(out var queued))
        {
            remaining.Add(queued);
        }

        if (_lastCommittedPosition < content.Length && _renderer is not null)
        {
            var uncommitted = content[_lastCommittedPosition..];
            if (!string.IsNullOrWhiteSpace(uncommitted))
            {
                remaining.Add(_renderer.Render(uncommitted));
            }
        }

        return remaining;
    }

    public void Clear()
    {
        _buffer.Clear();
        _lastCommittedPosition = 0;
        while (_pendingLines.TryDequeue(out _))
        {
        }
    }
}

public sealed class AnimationController<TRenderable> : IDisposable
{
    private readonly StreamCollector<TRenderable> _collector;
    private readonly Action<TRenderable> _onLineReady;
    private readonly Action _onAnimationComplete;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public AnimationController(
        StreamCollector<TRenderable> collector,
        Action<TRenderable> onLineReady,
        Action? onAnimationComplete = null)
    {
        _collector = collector;
        _onLineReady = onLineReady;
        _onAnimationComplete = onAnimationComplete ?? (() => { });
    }

    public int TickIntervalMs { get; set; } = 50;

    public void StartAnimation()
    {
        lock (_lock)
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var line = _collector.DequeueNextLine();
                        if (line is not null)
                        {
                            _onLineReady(line);
                        }
                        else if (!_collector.HasQueuedLines)
                        {
                            break;
                        }

                        await Task.Delay(TickIntervalMs, _cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    lock (_lock)
                    {
                        _isRunning = false;
                    }

                    _onAnimationComplete();
                }
            });
        }
    }

    public void StopAndDrain()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            while (_collector.HasQueuedLines)
            {
                var line = _collector.DequeueNextLine();
                if (line is not null)
                {
                    _onLineReady(line);
                }
            }

            _isRunning = false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
