using HPD.Agent.TUI.Models;
using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.Agent.TUI.Composition;

public interface IAgentTuiTranscriptRenderer<TCell>
    where TCell : TranscriptCell
{
    IComponent Create(AgentTuiTranscriptRenderContext<TCell> context);
}

public sealed class DelegateAgentTuiTranscriptRenderer<TCell> : IAgentTuiTranscriptRenderer<TCell>
    where TCell : TranscriptCell
{
    private readonly Func<AgentTuiTranscriptRenderContext<TCell>, IComponent> _create;

    public DelegateAgentTuiTranscriptRenderer(
        Func<AgentTuiTranscriptRenderContext<TCell>, IComponent> create)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
    }

    public IComponent Create(AgentTuiTranscriptRenderContext<TCell> context)
        => _create(context) ?? throw new InvalidOperationException("Transcript renderer returned null.");
}

public sealed class AgentTuiTranscriptRenderContext<TCell>
    where TCell : TranscriptCell
{
    public AgentTuiTranscriptRenderContext(
        TranscriptEntry entry,
        TCell cell,
        AgentTuiTranscriptRenderServices services,
        int width,
        Theme theme,
        ColorSystem colorSystem)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Cell = cell ?? throw new ArgumentNullException(nameof(cell));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Width = width;
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        ColorSystem = colorSystem;
    }

    public TranscriptEntry Entry { get; }

    public TCell Cell { get; }

    public TranscriptEntryMetadata Metadata => Entry.Metadata;

    public string DepthIndent => Services.GetDepthIndent(Metadata);

    public AgentTuiTranscriptRenderServices Services { get; }
    public int Width { get; }
    public Theme Theme { get; }
    public ColorSystem ColorSystem { get; }
}

public sealed class AgentTuiTranscriptRenderServices
{
    public static readonly AgentTuiTranscriptRenderServices Default = new();

    public IComponent Prefix(
        IComponent body,
        string firstPrefix,
        string subsequentPrefix,
        AgentTuiTranscriptPrefixStyle style = AgentTuiTranscriptPrefixStyle.Border)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(firstPrefix);
        ArgumentNullException.ThrowIfNull(subsequentPrefix);
        return new PrefixedComponent(body, firstPrefix, subsequentPrefix, style);
    }

    public IComponent PrefixedText(
        string text,
        string firstPrefix,
        string subsequentPrefix,
        AgentTuiTranscriptTextStyle textStyle = AgentTuiTranscriptTextStyle.Muted)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(firstPrefix);
        ArgumentNullException.ThrowIfNull(subsequentPrefix);
        return new PrefixedTextComponent(text, firstPrefix, subsequentPrefix, textStyle);
    }

    public string GetDepthIndent(TranscriptEntryMetadata metadata)
        => new(' ', Math.Max(0, metadata.AgentDepth) * 2);

    public string FormatRunState(TranscriptRunState state)
        => state switch
        {
            TranscriptRunState.Pending => "pending",
            TranscriptRunState.Running => "running",
            TranscriptRunState.Completed => "completed",
            TranscriptRunState.Failed => "failed",
            TranscriptRunState.Backgrounded => "backgrounded",
            _ => state.ToString().ToLowerInvariant()
        };

    public string FormatRunState(TranscriptRunState state, string? detail)
    {
        var value = FormatRunState(state);
        return string.IsNullOrWhiteSpace(detail) ? value : $"{value} {detail}";
    }

    public string FormatDuration(TimeSpan duration)
        => duration.TotalSeconds < 1
            ? $"{duration.TotalMilliseconds:0}ms"
            : $"{duration.TotalSeconds:0.#}s";

    public Theme CreateMutedTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new Theme
        {
            Text = Muted,
            Accent = Muted,
            Blue = Muted,
            Border = Muted,
            Error = theme.Error,
            Success = theme.Success,
            Warning = Muted
        };
    }

    public static Style GetTextStyle(AgentTuiTranscriptTextStyle style, Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return style switch
        {
            AgentTuiTranscriptTextStyle.Accent => theme.Accent,
            AgentTuiTranscriptTextStyle.Success => Success,
            AgentTuiTranscriptTextStyle.Error => Error,
            AgentTuiTranscriptTextStyle.Warning => theme.Warning,
            AgentTuiTranscriptTextStyle.Text => theme.Text,
            _ => Muted
        };
    }

    public Style StyleForRunState(TranscriptRunState state)
        => state switch
        {
            TranscriptRunState.Completed => Muted,
            TranscriptRunState.Failed => Error,
            TranscriptRunState.Running => Accent,
            TranscriptRunState.Backgrounded => Accent,
            _ => Muted
        };

    public Style StyleForSeverity(TranscriptSeverity severity)
        => severity switch
        {
            TranscriptSeverity.Success => Success,
            TranscriptSeverity.Warning => Accent,
            TranscriptSeverity.Error => Error,
            _ => Muted
        };

    public static readonly Style Muted = new(Color.Gray, Color.Default);
    public static readonly Style Accent = new(Color.Cyan, Color.Default);
    public static readonly Style Success = new(Color.Green, Color.Default);
    public static readonly Style Error = new(Color.Red, Color.Default);

    private sealed class PrefixedComponent : Component
    {
        private readonly IComponent _body;
        private readonly string _firstPrefix;
        private readonly string _subsequentPrefix;
        private readonly AgentTuiTranscriptPrefixStyle _style;

        public PrefixedComponent(
            IComponent body,
            string firstPrefix,
            string subsequentPrefix,
            AgentTuiTranscriptPrefixStyle style)
        {
            _body = body;
            _firstPrefix = firstPrefix;
            _subsequentPrefix = subsequentPrefix;
            _style = style;
        }

        public override Measurement Measure(in RenderContext context, int maxWidth)
            => new(Math.Min(maxWidth, 1), maxWidth);

        public override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
        {
            if (maxWidth <= 0)
            {
                return;
            }

            var bodyWidth = Math.Max(1, maxWidth - Math.Max(
                UnicodeWidth.GetWidth(_firstPrefix),
                UnicodeWidth.GetWidth(_subsequentPrefix)));
            var style = _style == AgentTuiTranscriptPrefixStyle.Accent
                ? context.Theme.Accent
                : context.Theme.Border;
            output.Write(_firstPrefix.AsSpan(), style);

            var sink = new PrefixingSink(output.Sink, _subsequentPrefix, style);
            var prefixedOutput = new DisplayListBuilder(sink, output.MaxWidth);
            _body.Render(in context, bodyWidth, ref prefixedOutput);
        }

        public override bool HandleInput(in TuiInputEvent key)
        {
            return _body.HandleInput(in key);
        }
    }

    private sealed class PrefixedTextComponent : Component
    {
        private readonly string _text;
        private readonly string _firstPrefix;
        private readonly string _subsequentPrefix;
        private readonly AgentTuiTranscriptTextStyle _textStyle;

        public PrefixedTextComponent(
            string text,
            string firstPrefix,
            string subsequentPrefix,
            AgentTuiTranscriptTextStyle textStyle)
        {
            _text = text;
            _firstPrefix = firstPrefix;
            _subsequentPrefix = subsequentPrefix;
            _textStyle = textStyle;
        }

        public override Measurement Measure(in RenderContext context, int maxWidth)
            => new(Math.Min(maxWidth, 1), maxWidth);

        public override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
        {
            if (maxWidth <= 0)
            {
                return;
            }

            var bodyWidth = Math.Max(1, maxWidth - Math.Max(
                UnicodeWidth.GetWidth(_firstPrefix),
                UnicodeWidth.GetWidth(_subsequentPrefix)));
            output.Write(_firstPrefix.AsSpan(), context.Theme.Border);
            var sink = new PrefixingSink(output.Sink, _subsequentPrefix, context.Theme.Border);
            var prefixedOutput = new DisplayListBuilder(sink, output.MaxWidth);
            WriteWrappedText(_text, bodyWidth, GetTextStyle(_textStyle, context.Theme), ref prefixedOutput);
        }

        public override bool HandleInput(in TuiInputEvent key)
        {
            return false;
        }
    }

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

        public bool Write(scoped ReadOnlySpan<char> text, Style style, TerminalRunMetadata metadata = default)
        {
            if (_needsPrefix)
            {
                _needsPrefix = false;
                if (!_inner.Write(_prefix.AsSpan(), _style))
                {
                    return false;
                }
            }

            return _inner.Write(text, style, metadata);
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

    private static void WriteWrappedText(string text, int maxWidth, Style style, ref DisplayListBuilder output)
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
}

public enum AgentTuiTranscriptPrefixStyle
{
    Border,
    Accent
}

public enum AgentTuiTranscriptTextStyle
{
    Muted,
    Text,
    Accent,
    Success,
    Warning,
    Error
}

public static class AgentTuiTranscriptRendererKeys
{
    public const string UserMessage = "hpd.user-message";
    public const string AssistantMessage = "hpd.assistant-message";
    public const string ReasoningMessage = "hpd.reasoning-message";
    public const string Notice = "hpd.notice";
    public const string RunStatus = "hpd.run-status";
    public const string ToolCall = "hpd.tool-call";
    public const string CustomComponent = "hpd.custom-component";
}

internal interface IAgentTuiTranscriptRendererAdapter
{
    string Key { get; }

    Type CellType { get; }

    IComponent Create(TranscriptEntry entry, AgentTuiTranscriptRenderServices services, int width, Theme theme, ColorSystem colorSystem);
}

internal sealed class AgentTuiTranscriptRendererAdapter<TCell> : IAgentTuiTranscriptRendererAdapter
    where TCell : TranscriptCell
{
    public AgentTuiTranscriptRendererAdapter(
        string key,
        IAgentTuiTranscriptRenderer<TCell> renderer)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public string Key { get; }

    public Type CellType => typeof(TCell);

    public IAgentTuiTranscriptRenderer<TCell> Renderer { get; }

    public IComponent Create(TranscriptEntry entry, AgentTuiTranscriptRenderServices services, int width, Theme theme, ColorSystem colorSystem)
    {
        if (entry.Cell is not TCell cell)
        {
            throw new InvalidOperationException(
                $"Transcript renderer '{Key}' expected cell type '{typeof(TCell).Name}' but received '{entry.Cell.GetType().Name}'.");
        }

        return Renderer.Create(new AgentTuiTranscriptRenderContext<TCell>(entry, cell, services, width, theme, colorSystem));
    }
}
