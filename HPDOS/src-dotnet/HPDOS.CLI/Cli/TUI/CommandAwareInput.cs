using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;
using HPDOS.Shell.Cli.TUI.Commands;

namespace HPDOS.Shell.Cli.TUI;

/// <summary>
/// Readline with slash command autocomplete and cursor navigation.
/// Uses Spectre's render hook pipeline so prompt redraw coexists with other
/// console writes while suggestions remain attached to the active input.
/// Returns null on Ctrl+C.
/// </summary>
public class CommandAwareInput
{
    private const string PromptInputGap = "   ";

    private readonly IConsoleSession _session;
    private readonly SuggestionManager _suggestions;

    public CommandAwareInput(CommandProcessor processor, IConsoleSession session)
    {
        _session = session;
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
        string? result = null;

        InputView BuildView() => new(
            prompt,
            input.ToString(),
            cursorPos,
            _suggestions.HasSuggestions ? _suggestions.Render() : null);

        var hook = new InputRenderHook(_session, BuildView);

        if (input.Length > 0 && input[0] == '/')
            _suggestions.UpdateQuery(input.ToString());
        else
            _suggestions.Clear();

        void UpdateSuggestions()
        {
            var text = input.ToString();
            if (text.StartsWith('/'))
                _suggestions.UpdateQuery(text);
            else
                _suggestions.Clear();
        }

        void Refresh()
        {
            hook.Refresh();
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

        try
        {
            using (new RenderHookScope(_session.Console, hook))
            {
                _session.ShowCursor();
                Refresh();

                while (true)
                {
                    var keyInfo = _session.Input
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
                            if (cursorPos > 0)
                            {
                                input.Remove(cursorPos - 1, 1);
                                cursorPos--;
                                UpdateSuggestions();
                                Refresh();
                            }
                            break;

                        case ConsoleKey.Delete:
                            if (cursorPos < input.Length)
                            {
                                input.Remove(cursorPos, 1);
                                UpdateSuggestions();
                                Refresh();
                            }
                            break;

                        case ConsoleKey.LeftArrow:
                            if (cursorPos > 0)
                            {
                                cursorPos--;
                                Refresh();
                            }
                            break;

                        case ConsoleKey.RightArrow:
                            if (cursorPos < input.Length)
                            {
                                cursorPos++;
                                Refresh();
                            }
                            break;

                        case ConsoleKey.Home:
                            cursorPos = 0;
                            Refresh();
                            break;

                        case ConsoleKey.End:
                            cursorPos = input.Length;
                            Refresh();
                            break;

                        case ConsoleKey.UpArrow:
                            if (_suggestions.HasSuggestions)
                            {
                                _suggestions.NavigateUp();
                                Refresh();
                            }
                            break;

                        case ConsoleKey.DownArrow:
                            if (_suggestions.HasSuggestions)
                            {
                                _suggestions.NavigateDown();
                                Refresh();
                            }
                            break;

                        case ConsoleKey.Tab:
                            if (_suggestions.HasSuggestions)
                                ApplyCompletion();
                            break;

                        case ConsoleKey.Escape:
                            if (_suggestions.HasSuggestions)
                            {
                                _suggestions.Clear();
                                Refresh();
                            }
                            break;

                        case ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                            result = null;
                            goto done;

                        default:
                            if (!char.IsControl(key.KeyChar))
                            {
                                input.Insert(cursorPos, key.KeyChar);
                                cursorPos++;
                                UpdateSuggestions();
                                Refresh();
                            }
                            break;
                    }
                }

                done:
                ;
            }

            hook.Clear();
        }
        finally
        {
            _session.ShowCursor();
            Console.TreatControlCAsInput = prevTreatCtrlC;
        }

        Console.WriteLine();
        return result;
    }

    private sealed record InputView(string Prompt, string Input, int CursorPos, IRenderable? Suggestions);

    private sealed class InputRenderHook(IConsoleSession session, Func<InputView> builder) : IRenderHook
    {
        private readonly LiveInputRenderable _live = new();
        private readonly object _lock = new();
        private bool _dirty = true;

        public void Refresh()
        {
            _dirty = true;
            session.Write(new ControlCode(string.Empty));
        }

        public void Clear()
        {
            session.Write(_live.RestoreCursor());
        }

        public IEnumerable<IRenderable> Process(RenderOptions options, IEnumerable<IRenderable> renderables)
        {
            lock (_lock)
            {
                if (!_live.HasRenderable || _dirty)
                {
                    _live.SetRenderable(new InputRenderable(builder()));
                    _dirty = false;
                }

                yield return _live.PositionCursor(options);

                foreach (var renderable in renderables)
                    yield return renderable;

                yield return _live;
            }
        }
    }

    private sealed class InputRenderable : Renderable
    {
        public InputView View { get; }

        public InputRenderable(InputView view)
        {
            View = view;
        }

        protected override Measurement Measure(RenderOptions options, int maxWidth)
        {
            var lines = Segment.SplitLines(Render(options, maxWidth), maxWidth);
            var width = lines.Count == 0 ? 0 : lines.Max(line => line.CellCount());
            return new Measurement(width, width);
        }

        protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            foreach (var segment in RenderPrompt(View, options, maxWidth))
                yield return segment;

            if (View.Suggestions is null)
                yield break;

            yield return Segment.LineBreak;

            foreach (var segment in View.Suggestions.Render(options, maxWidth))
                yield return segment;
        }

        public static IEnumerable<Segment> RenderPrompt(InputView view, RenderOptions options, int maxWidth)
        {
            return ((IRenderable)new Markup(BuildPromptMarkup(view, view.Input))).Render(options, maxWidth);
        }

        public static IEnumerable<Segment> RenderPromptPrefix(InputView view, RenderOptions options, int maxWidth)
        {
            var safeCursorPos = Math.Clamp(view.CursorPos, 0, view.Input.Length);
            return ((IRenderable)new Markup(BuildPromptMarkup(view, view.Input[..safeCursorPos]))).Render(options, maxWidth);
        }

        private static string BuildPromptMarkup(InputView view, string inputText)
        {
            return view.Prompt.TrimEnd() + PromptInputGap + Markup.Escape(inputText);
        }
    }

    private sealed class LiveInputRenderable : Renderable
    {
        private IRenderable? _renderable;
        private int _height;
        private int _cursorLineIndex;

        public bool HasRenderable => _renderable != null;

        public void SetRenderable(IRenderable renderable)
        {
            _renderable = renderable;
        }

        public IRenderable PositionCursor(RenderOptions options)
        {
            if (_height <= 0)
                return new ControlCode(string.Empty);

            var ansi = new StringBuilder("\r");
            if (_cursorLineIndex > 0)
                ansi.Append($"\x1b[{_cursorLineIndex}A");
            return new ControlCode(ansi.ToString());
        }

        public IRenderable RestoreCursor()
        {
            if (_height <= 0)
                return new ControlCode(string.Empty);

            var ansi = new StringBuilder("\r");

            var linesToMoveDown = Math.Max(0, _height - 1 - _cursorLineIndex);
            if (linesToMoveDown > 0)
                ansi.Append($"\x1b[{linesToMoveDown}B");

            ansi.Append("\r\x1b[2K");
            for (var lineIndex = 1; lineIndex < _height; lineIndex++)
                ansi.Append("\x1b[1A\r\x1b[2K");

            _height = 0;
            _cursorLineIndex = 0;
            return new ControlCode(ansi.ToString());
        }

        protected override Measurement Measure(RenderOptions options, int maxWidth)
        {
            if (_renderable is null)
                return new Measurement(0, 0);

            var lines = Segment.SplitLines(_renderable.Render(options, maxWidth), maxWidth);
            var width = lines.Count == 0 ? 0 : lines.Max(line => line.CellCount());
            return new Measurement(width, width);
        }

        protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            if (_renderable is null)
            {
                _height = 0;
                _cursorLineIndex = 0;
                yield break;
            }

            var lines = Segment.SplitLines(_renderable.Render(options, maxWidth), maxWidth);
            var currentHeight = lines.Count;
            var rowsToRender = Math.Max(_height, currentHeight);

            for (var lineIndex = 0; lineIndex < rowsToRender; lineIndex++)
            {
                if (lineIndex > 0)
                    yield return Segment.LineBreak;

                yield return Segment.Control("\r\x1b[2K");

                if (lineIndex >= currentHeight)
                    continue;

                foreach (var segment in lines[lineIndex])
                    yield return segment;
            }

            if (_renderable is InputRenderable inputRenderable)
            {
                var cursorControl = CreateRestoreCursorControl(inputRenderable.View, options, maxWidth, rowsToRender, out var cursorLineIndex);
                _cursorLineIndex = cursorLineIndex;
                if (cursorControl.Text.Length > 0)
                    yield return cursorControl;
            }
            else
            {
                _cursorLineIndex = 0;
            }

            _height = currentHeight;
        }

        private static Segment CreateRestoreCursorControl(InputView view, RenderOptions options, int maxWidth, int renderedHeight, out int cursorLineIndex)
        {
            var prefixLines = Segment.SplitLines(InputRenderable.RenderPromptPrefix(view, options, maxWidth), maxWidth);
            cursorLineIndex = Math.Max(0, prefixLines.Count - 1);
            var cursorColumn = prefixLines[cursorLineIndex].CellCount();
            var linesToMoveUp = Math.Max(0, renderedHeight - 1 - cursorLineIndex);

            var ansi = new StringBuilder("\r");
            if (linesToMoveUp > 0)
                ansi.Append($"\x1b[{linesToMoveUp}A");
            if (cursorColumn > 0)
                ansi.Append($"\x1b[{cursorColumn}C");

            return Segment.Control(ansi.ToString());
        }
    }
}
