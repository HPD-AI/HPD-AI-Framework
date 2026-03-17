using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;
using HPDOS.Shell.Cli.TUI.Commands;

namespace HPDOS.Shell.Cli.TUI;

/// <summary>
/// Readline with slash command autocomplete and cursor navigation.
/// Uses RenderHookScope (same pattern as Spectre's SelectionPrompt) with a
/// self-contained cursor tracker — no dependency on internal LiveRenderable.
/// Returns null on Ctrl+C.
/// </summary>
public class CommandAwareInput
{
    private readonly CommandProcessor _processor;
    private readonly SuggestionManager _suggestions;

    public CommandAwareInput(CommandProcessor processor)
    {
        _processor = processor;
        _suggestions = processor.CreateSuggestionManager();
    }

    public string? ReadLine(string prompt = "> ", string? initialText = null)
    {
        var input = new StringBuilder();
        int cursorPos = 0;

        if (initialText != null)
        {
            input.Append(initialText);
            cursorPos = input.Length;
        }

        var prevTreatCtrlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;

        var hook = new SuggestionRenderHook(AnsiConsole.Console);
        string? result = null;

        using (new RenderHookScope(AnsiConsole.Console, hook))
        {
            AnsiConsole.Cursor.Hide();

            // Visual width of the prompt — strip Spectre markup tags so we measure
            // only printable characters (e.g. "[dim]experimental[/] > " → "experimental > ").
            var promptVisualWidth = Markup.Remove(prompt).Length;

            void Redraw()
            {
                var width = Math.Max(1, Console.WindowWidth);
                var totalChars = promptVisualWidth + input.Length;

                // How many terminal rows the prompt+input occupies (≥1).
                var promptRows = Math.Max(1, (totalChars + width - 1) / width);

                // Tell the hook the new prompt row count so it moves the cursor up
                // the correct total distance (prompt rows + dropdown rows).
                hook.SetPromptRows(promptRows);

                // Build the full prompt+input string, padding each row to the full
                // terminal width so stale content from previously longer input is erased.
                var sb = new StringBuilder();
                sb.Append('\r');
                var inputStr = input.ToString();

                // We emit one block of text; the terminal wraps it naturally.
                // Pad out the last row so no stale chars from a previously longer input remain.
                var paddingNeeded = promptRows * width - totalChars;
                sb.Append(prompt);
                sb.Append(Markup.Escape(inputStr));
                if (paddingNeeded > 0)
                    sb.Append(new string(' ', paddingNeeded));

                AnsiConsole.Write(new Markup(sb.ToString()));

                // Reposition cursor to the logical cursor position within the input.
                var cursorAbsCol = promptVisualWidth + cursorPos;
                var cursorRow = cursorAbsCol / width;       // 0-based row within prompt block
                var cursorCol = cursorAbsCol % width;       // column on that row

                // We're currently at the last row of the prompt block (promptRows-1).
                // Move up to cursorRow if needed.
                var rowsToMoveUp = (promptRows - 1) - cursorRow;
                if (rowsToMoveUp > 0)
                    Console.Write($"\x1b[{rowsToMoveUp}A");

                Console.SetCursorPosition(cursorCol, Console.CursorTop);
            }

            void Refresh()
            {
                if (_suggestions.HasSuggestions)
                    hook.SetContent(_suggestions.Render());
                else
                    hook.ClearContent();
                Redraw();
            }

            void UpdateSuggestions()
            {
                var text = input.ToString();
                if (text.StartsWith('/'))
                    _suggestions.UpdateQuery(text);
                else
                    _suggestions.Clear();
                Refresh();
            }

            void ApplyCompletion()
            {
                var completed = _suggestions.GetCompletedText();
                if (completed == null) return;
                input.Clear();
                input.Append(completed);
                input.Append(' ');
                cursorPos = input.Length;
                _suggestions.Clear();
                Refresh();
            }

            // Initial draw.
            Redraw();

            while (true)
            {
                var keyInfo = AnsiConsole.Console.Input
                    .ReadKeyAsync(intercept: true, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (keyInfo is not { } key) continue;

                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        if (_suggestions.HasSuggestions)
                        {
                            var selected = _suggestions.GetSelected();
                            if (selected != null)
                            {
                                var completed = _suggestions.GetCompletedText();
                                var current = input.ToString().TrimEnd();
                                if (completed != null && current.Equals(completed, StringComparison.OrdinalIgnoreCase))
                                {
                                    result = current;
                                    goto done;
                                }
                                if (selected.Command.AutoExecute)
                                {
                                    result = "/" + selected.DisplayName;
                                    goto done;
                                }
                                ApplyCompletion();
                                break;
                            }
                        }
                        result = input.ToString();
                        goto done;

                    case ConsoleKey.Backspace:
                        if (cursorPos > 0) { input.Remove(cursorPos - 1, 1); cursorPos--; UpdateSuggestions(); }
                        break;

                    case ConsoleKey.Delete:
                        if (cursorPos < input.Length) { input.Remove(cursorPos, 1); UpdateSuggestions(); }
                        break;

                    case ConsoleKey.LeftArrow:
                        if (cursorPos > 0) { cursorPos--; Redraw(); }
                        break;

                    case ConsoleKey.RightArrow:
                        if (cursorPos < input.Length) { cursorPos++; Redraw(); }
                        break;

                    case ConsoleKey.Home:
                        cursorPos = 0; Redraw();
                        break;

                    case ConsoleKey.End:
                        cursorPos = input.Length; Redraw();
                        break;

                    case ConsoleKey.UpArrow:
                        if (_suggestions.HasSuggestions) { _suggestions.NavigateUp(); Refresh(); }
                        break;

                    case ConsoleKey.DownArrow:
                        if (_suggestions.HasSuggestions) { _suggestions.NavigateDown(); Refresh(); }
                        break;

                    case ConsoleKey.Tab:
                        if (_suggestions.HasSuggestions) ApplyCompletion();
                        break;

                    case ConsoleKey.Escape:
                        if (_suggestions.HasSuggestions) { _suggestions.Clear(); Refresh(); }
                        break;

                    case ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                        result = null;
                        goto done;

                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            input.Insert(cursorPos, key.KeyChar);
                            cursorPos++;
                            if (input.Length > 0 && input[0] == '/')
                                UpdateSuggestions();
                            else if (_suggestions.HasSuggestions)
                            {
                                _suggestions.Clear();
                                Refresh();
                            }
                            else
                                Redraw();
                        }
                        break;
                }
            }

            done:
            hook.ClearContent();
            hook.Erase();
            AnsiConsole.Cursor.Show();
        }

        Console.WriteLine();
        Console.TreatControlCAsInput = prevTreatCtrlC;
        return result;
    }

    /// <summary>
    /// Render hook that repositions the cursor and appends the suggestion dropdown after every
    /// console write. Owns the full bounding box: prompt rows (which may wrap) + dropdown rows.
    ///
    /// Bounding-box strategy (mirrors LiveRenderable._shape inflation):
    ///   _lastHeight tracks total rows = prompt rows + dropdown rows, inflated (never shrinks
    ///   until cleared). On each Process() call:
    ///     1. Move cursor up by (_lastHeight - 1) to top of the previous bounding box.
    ///     2. Emit prompt + input renderables (may span multiple terminal rows if wrapping).
    ///     3a. If dropdown content: render it, padding to _lastWidth, emitting blank rows for
    ///         height-shrink gaps. Inflate _lastHeight/_lastWidth.
    ///     3b. If cleared: wipe stale dropdown rows, reset trackers to _promptRows.
    /// </summary>
    private sealed class SuggestionRenderHook : IRenderHook
    {
        private readonly IAnsiConsole _console;
        private IRenderable? _content;
        // Total bounding box rows = prompt rows + dropdown rows (inflated, never shrinks until cleared).
        private int _lastHeight;
        private int _lastWidth;
        // How many terminal rows the current prompt+input occupies (set by Redraw before each write).
        private int _promptRows = 1;
        private readonly object _lock = new();

        public SuggestionRenderHook(IAnsiConsole console)
        {
            _console = console;
        }

        public void SetContent(IRenderable content)
        {
            lock (_lock) _content = content;
        }

        public void ClearContent()
        {
            lock (_lock) _content = null;
        }

        /// <summary>
        /// Called by Redraw() before each AnsiConsole.Write so the hook knows how many rows
        /// the prompt+input block occupies this render cycle.
        /// </summary>
        public void SetPromptRows(int rows)
        {
            lock (_lock) _promptRows = Math.Max(1, rows);
        }

        /// <summary>
        /// Erase the full bounding box and restore cursor to the top-left of the prompt.
        /// Call before exiting RenderHookScope.
        /// </summary>
        public void Erase()
        {
            lock (_lock)
            {
                if (_lastHeight <= 0) return;
                var linesToClear = _lastHeight - 1;
                var sb = new StringBuilder();
                sb.Append("\r\x1b[2K"); // erase current (top) line
                for (var i = 0; i < linesToClear; i++)
                    sb.Append("\x1b[1B\r\x1b[2K"); // cursor down + erase each subsequent row
                if (linesToClear > 0)
                    sb.Append($"\x1b[{linesToClear}A"); // return to top row
                _console.Write(new ControlCode(sb.ToString()));
                _lastHeight = 0;
                _lastWidth = 0;
            }
        }

        public IEnumerable<IRenderable> Process(RenderOptions options, IEnumerable<IRenderable> renderables)
        {
            lock (_lock)
            {
                // Step 1: reposition to the top of the previous total bounding box.
                // _lastHeight covers prompt rows + dropdown rows; moving up (_lastHeight-1)
                // lands at row 0 of the prompt block regardless of how much it wrapped.
                if (_lastHeight > 1)
                    yield return new ControlCode($"\x1b[{_lastHeight - 1}A");

                // Step 2: emit prompt + input renderables.
                // Redraw() already computed the correct padding/erase for the prompt block.
                foreach (var r in renderables) yield return r;

                // Step 3a: render dropdown, inflating bounding box.
                if (_content != null)
                {
                    var segments = _content.Render(options, options.ConsoleSize.Width).ToList();
                    var lines = SplitIntoLines(segments);

                    var newDropdownRows = Math.Max(1, lines.Count);
                    var newWidth = lines.Count > 0 ? lines.Max(CellWidth) : 0;

                    // Previous dropdown row count = _lastHeight - previous prompt rows.
                    // We inflate only the dropdown portion, then add current prompt rows.
                    var prevDropdownRows = Math.Max(0, _lastHeight - _promptRows);
                    var boundDropdownRows = Math.Max(prevDropdownRows, newDropdownRows);
                    var boundWidth = Math.Max(_lastWidth, newWidth);

                    yield return new ControlCode("\n"); // move below last prompt row

                    for (var row = 0; row < boundDropdownRows; row++)
                    {
                        if (row < lines.Count)
                        {
                            var lineWidth = CellWidth(lines[row]);
                            foreach (var s in lines[row]) yield return new RenderedSegment(s);
                            var pad = boundWidth - lineWidth;
                            if (pad > 0) yield return new RenderedSegment(Segment.Padding(pad));
                        }
                        else
                        {
                            if (boundWidth > 0) yield return new RenderedSegment(Segment.Padding(boundWidth));
                        }

                        if (row < boundDropdownRows - 1) yield return new RenderedSegment(Segment.LineBreak);
                    }

                    _lastHeight = _promptRows + boundDropdownRows;
                    _lastWidth  = boundWidth;
                }
                else
                {
                    // Step 3b: no dropdown — wipe any stale dropdown rows.
                    var prevDropdownRows = Math.Max(0, _lastHeight - _promptRows);
                    if (prevDropdownRows > 0)
                    {
                        var sb = new StringBuilder();
                        for (var i = 0; i < prevDropdownRows; i++)
                            sb.Append("\n\r\x1b[2K");
                        sb.Append($"\x1b[{prevDropdownRows}A");
                        yield return new ControlCode(sb.ToString());
                    }
                    _lastHeight = _promptRows;
                    _lastWidth  = 0;
                }
            }
        }

        // Split a flat segment list into lines on LineBreak segments.
        private static List<List<Segment>> SplitIntoLines(List<Segment> segments)
        {
            var lines = new List<List<Segment>>();
            var current = new List<Segment>();
            foreach (var s in segments)
            {
                if (s.IsLineBreak) { lines.Add(current); current = []; }
                else current.Add(s);
            }
            if (current.Count > 0) lines.Add(current);
            return lines;
        }

        // Total cell width of a line (handles double-width chars via Segment.CellCount).
        private static int CellWidth(List<Segment> line) => Segment.CellCount(line);
    }

    /// <summary>Wraps a single Segment as an IRenderable for yielding from Process().</summary>
    private sealed class RenderedSegment : Renderable
    {
        private readonly Segment _segment;
        public RenderedSegment(Segment segment) => _segment = segment;
        protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            yield return _segment;
        }
    }
}
