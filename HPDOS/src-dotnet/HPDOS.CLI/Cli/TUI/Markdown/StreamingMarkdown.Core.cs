using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace HPDOS.Shell.Cli.TUI.Markdown;

public interface IMarkdownRenderer<TRenderable>
{
    TRenderable Render(string markdown);
}

public interface ISyntaxHighlighter
{
    bool IsLanguageSupported(string? language);
    string Highlight(string code, string? language);
}

public static class MarkdownParser
{
    public static int FindLastSafeSplitPoint(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;
        if (IsInsideCodeBlock(content, content.Length))
        {
            var blockStart = FindLastCodeBlockStart(content);
            if (blockStart > 0) return FindLastSafeSplitPoint(content[..blockStart]);
            return 0;
        }
        var lastParagraphBreak = FindLastParagraphBreak(content);
        if (lastParagraphBreak > 0) return lastParagraphBreak;
        var lastNewline = FindLastSafeNewline(content);
        if (lastNewline > 0) return lastNewline;
        return content.Length;
    }

    public static bool IsInsideCodeBlock(string content, int position)
    {
        var searchContent = content[..Math.Min(position, content.Length)];
        return CountCodeFences(searchContent) % 2 == 1;
    }

    private static int CountCodeFences(string content)
    {
        int count = 0, index = 0;
        while (index < content.Length)
        {
            var fenceIndex = content.IndexOf("```", index, StringComparison.Ordinal);
            if (fenceIndex == -1) break;
            count++;
            index = fenceIndex + 3;
        }
        return count;
    }

    private static int FindLastCodeBlockStart(string content)
    {
        var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence == -1) return -1;
        if (CountCodeFences(content[..(lastFence + 3)]) % 2 == 1) return lastFence;
        var searchFrom = lastFence - 1;
        while (searchFrom >= 0)
        {
            var prevFence = content.LastIndexOf("```", searchFrom, StringComparison.Ordinal);
            if (prevFence == -1) break;
            if (CountCodeFences(content[..(prevFence + 3)]) % 2 == 1) return prevFence;
            searchFrom = prevFence - 1;
        }
        return -1;
    }

    private static int FindLastParagraphBreak(string content)
    {
        var searchFrom = content.Length - 1;
        while (searchFrom >= 1)
        {
            var breakIndex = content.LastIndexOf("\n\n", searchFrom, StringComparison.Ordinal);
            if (breakIndex == -1) break;
            var splitPoint = breakIndex + 2;
            if (!IsInsideCodeBlock(content, splitPoint)) return splitPoint;
            searchFrom = breakIndex - 1;
        }
        return -1;
    }

    private static int FindLastSafeNewline(string content)
    {
        if (content.Length < 50) return -1;
        var searchFrom = content.Length - 10;
        while (searchFrom >= 0)
        {
            var newlineIndex = content.LastIndexOf('\n', searchFrom);
            if (newlineIndex == -1) break;
            var splitPoint = newlineIndex + 1;
            if (!IsInsideCodeBlock(content, splitPoint) && !IsInsideList(content, splitPoint))
                return splitPoint;
            searchFrom = newlineIndex - 1;
        }
        return -1;
    }

    private static bool IsInsideList(string content, int position)
    {
        if (position >= content.Length) return false;
        var lastLine = GetLastLine(content[..position]);
        var nextLine = GetFirstLine(content[position..]);
        var lastIsListItem = IsListItem(lastLine);
        var nextIsListItem = IsListItem(nextLine);
        if (lastIsListItem && nextIsListItem) return true;
        if (lastIsListItem && nextLine.StartsWith("  ")) return true;
        return false;
    }

    private static bool IsListItem(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ ") ||
               Regex.IsMatch(trimmed, @"^\d+\.\s");
    }

    private static string GetLastLine(string content)
    {
        var i = content.LastIndexOf('\n');
        return i == -1 ? content : content[(i + 1)..];
    }

    private static string GetFirstLine(string content)
    {
        var i = content.IndexOf('\n');
        return i == -1 ? content : content[..i];
    }
}

public class AnimationController<TRenderable> : IDisposable
{
    private readonly StreamCollector<TRenderable> _collector;
    private readonly Action<TRenderable> _onLineReady;
    private readonly Action _onAnimationComplete;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();
    private bool _isRunning;

    public int TickIntervalMs { get; set; } = 50;

    public AnimationController(StreamCollector<TRenderable> collector, Action<TRenderable> onLineReady, Action? onAnimationComplete = null)
    {
        _collector = collector;
        _onLineReady = onLineReady;
        _onAnimationComplete = onAnimationComplete ?? (() => { });
    }

    public void StartAnimation()
    {
        lock (_lock)
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var line = _collector.DequeueNextLine();
                        if (line != null)
                            _onLineReady(line);
                        else if (!_collector.HasCompleteLines)
                            break;
                        await Task.Delay(TickIntervalMs, _cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    lock (_lock) { _isRunning = false; }
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
                if (line != null) _onLineReady(line);
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

public class StreamCollector<TRenderable>
{
    private readonly IMarkdownRenderer<TRenderable>? _renderer;
    private readonly StringBuilder _buffer = new();
    private readonly ConcurrentQueue<TRenderable> _pendingLines = new();
    private int _lastCommittedPosition;

    public StreamCollector(IMarkdownRenderer<TRenderable>? renderer = null) => _renderer = renderer;

    public void Push(string delta)
    {
        if (!string.IsNullOrEmpty(delta)) _buffer.Append(delta);
    }

    public bool HasCompleteLines
    {
        get
        {
            var content = _buffer.ToString();
            if (content.Length <= _lastCommittedPosition) return false;
            var uncommitted = content[_lastCommittedPosition..];
            if (!uncommitted.Contains('\n')) return false;
            if (MarkdownParser.IsInsideCodeBlock(content, content.Length)) return false;
            return MarkdownParser.FindLastSafeSplitPoint(content) > _lastCommittedPosition;
        }
    }

    public void CommitCompleteLines()
    {
        var content = _buffer.ToString();
        var lastSafePoint = MarkdownParser.FindLastSafeSplitPoint(content);
        if (lastSafePoint <= _lastCommittedPosition) return;
        var newContent = content[_lastCommittedPosition..lastSafePoint];
        if (string.IsNullOrWhiteSpace(newContent)) return;
        if (_renderer != null) _pendingLines.Enqueue(_renderer.Render(newContent));
        _lastCommittedPosition = lastSafePoint;
    }

    public IReadOnlyList<TRenderable> GetQueuedLines()
    {
        var lines = new List<TRenderable>();
        while (_pendingLines.TryDequeue(out var line)) lines.Add(line);
        return lines;
    }

    public TRenderable? DequeueNextLine() => _pendingLines.TryDequeue(out var line) ? line : default;

    public bool HasQueuedLines => !_pendingLines.IsEmpty;

    public IReadOnlyList<TRenderable> Finalize()
    {
        var content = _buffer.ToString();
        var remaining = new List<TRenderable>();
        while (_pendingLines.TryDequeue(out var queued)) remaining.Add(queued);
        if (_lastCommittedPosition < content.Length && _renderer != null)
        {
            var uncommitted = content[_lastCommittedPosition..];
            if (!string.IsNullOrWhiteSpace(uncommitted))
                remaining.Add(_renderer.Render(uncommitted));
        }
        return remaining;
    }

    public void Clear()
    {
        _buffer.Clear();
        _lastCommittedPosition = 0;
        while (_pendingLines.TryDequeue(out _)) { }
    }

    public bool IsInsideCodeBlock => MarkdownParser.IsInsideCodeBlock(_buffer.ToString(), _buffer.Length);
    public string Content => _buffer.ToString();
}
