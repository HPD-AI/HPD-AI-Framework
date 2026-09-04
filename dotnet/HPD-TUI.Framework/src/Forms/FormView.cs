using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Forms;

public sealed class FormView : Component, IFocusable
{
    private const int WideLayoutMinimum = 48;
    private readonly FormModel _model;
    private readonly FormController _controller;
    private readonly FormUpdateMode _updateMode;

    public FormView(
        FormModel model,
        FormController controller,
        int maxVisibleRows = 10,
        FormUpdateMode updateMode = FormUpdateMode.Staged)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _updateMode = updateMode;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxVisibleRows);
        MaxVisibleRows = maxVisibleRows;
    }

    public bool IsFocused { get; set; }

    public int MaxVisibleRows { get; }

    public override Measurement Measure(in RenderContext context, int maxWidth)
    {
        _model.ReconcileActiveField();
        var visibleCount = _model.VisibleFieldCount;
        if (visibleCount == 0)
        {
            return new Measurement(1, Math.Min(maxWidth, 21), 1);
        }

        var activeVisiblePosition = Math.Max(0, _model.GetVisiblePosition(_model.ActiveFieldIndex));
        var rowCount = Math.Min(visibleCount, ResolveWindowSize(context.Height, visibleCount));
        var start = ResolveWindowStart(activeVisiblePosition, rowCount, visibleCount);
        var width = 0;
        for (var position = start; position < start + rowCount; position++)
        {
            var index = _model.GetSourceIndexAtVisiblePosition(position);
            var field = _model.Fields[index];
            width = Math.Max(
                width,
                UnicodeWidth.GetWidth(field.Label) + UnicodeWidth.GetWidth(field.DisplayValue) + 7);
        }

        width = Math.Min(Math.Max(1, width), maxWidth);
        var extraRows = 1; // Hint.
        if (visibleCount > rowCount)
        {
            extraRows++;
        }

        if (!string.IsNullOrWhiteSpace(_model.ActiveField?.Error ?? _model.ActiveField?.Description))
        {
            extraRows += 2;
        }

        return new Measurement(width, width, Math.Max(1, rowCount + extraRows));
    }

    public override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        _model.ReconcileActiveField();
        var visibleCount = _model.VisibleFieldCount;
        if (visibleCount == 0)
        {
            output.Write("No settings available".AsSpan(), context.Theme.Border);
            return;
        }

        var activeSourceIndex = _model.ActiveFieldIndex;
        var activeVisiblePosition = Math.Max(0, _model.GetVisiblePosition(activeSourceIndex));
        var windowSize = ResolveWindowSize(context.Height, visibleCount);
        var start = ResolveWindowStart(activeVisiblePosition, windowSize, visibleCount);
        var end = Math.Min(visibleCount, start + windowSize);
        var wide = maxWidth >= WideLayoutMinimum;
        var labelWidth = wide ? ResolveLabelWidth(start, end, maxWidth) : 0;

        for (var position = start; position < end; position++)
        {
            var sourceIndex = _model.GetSourceIndexAtVisiblePosition(position);
            var field = _model.Fields[sourceIndex];
            RenderField(in context, maxWidth, ref output, field, sourceIndex == activeSourceIndex, wide, labelWidth);
            output.WriteLineBreak();
        }

        if (visibleCount > windowSize)
        {
            var indicator = $"  ({activeVisiblePosition + 1}/{visibleCount})";
            output.Write(Truncate(indicator, maxWidth).AsSpan(), context.Theme.Border);
            output.WriteLineBreak();
        }

        var active = _model.ActiveField;
        var detail = active?.Error ?? _model.UpdateError ?? active?.Description;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            output.WriteLineBreak();
            foreach (var line in Wrap(detail, Math.Max(1, maxWidth - 2)))
            {
                output.Write("  ", context.Theme.Border);
                output.Write(
                    line.AsSpan(),
                    active?.Error is null && _model.UpdateError is null
                        ? context.Theme.Border
                        : context.Theme.Error);
                output.WriteLineBreak();
            }
        }

        output.Write(Truncate(BuildHint(active, _updateMode), maxWidth).AsSpan(), context.Theme.Border);
    }

    public override bool HandleInput(in TuiInputEvent input)
    {
        var keyEvent = input.KeyEvent;
        return _controller.HandleInput(in keyEvent);
    }

    private static void RenderField(
        in RenderContext context,
        int maxWidth,
        ref DisplayListBuilder output,
        IFormField field,
        bool active,
        bool wide,
        int labelWidth)
    {
        var style = !field.IsEnabled
            ? context.Theme.Border
            : active
                ? context.Theme.Accent
                : context.Theme.Text;
        var prefix = active ? "> " : "  ";
        output.Write(prefix, style);

        if (!wide)
        {
            output.Write(Truncate(field.Label, Math.Max(1, maxWidth - 2)).AsSpan(), style);
            output.WriteLineBreak();
            output.Write("    ", context.Theme.Border);
            var marker = field.IsEditing ? "[" : string.Empty;
            var suffix = field.IsEditing ? "]" : InteractionSuffix(field);
            output.Write(
                Truncate(marker + field.DisplayValue + suffix, Math.Max(1, maxWidth - 4)).AsSpan(),
                style);
            return;
        }

        var label = Truncate(field.Label, labelWidth);
        output.Write(label.AsSpan(), style);
        output.Write(new string(' ', Math.Max(1, labelWidth - UnicodeWidth.GetWidth(label) + 2)), context.Theme.Border);
        var valueWidth = Math.Max(1, maxWidth - 2 - labelWidth - 3);
        var value = field.IsEditing
            ? $"[{field.DisplayValue}]"
            : field.DisplayValue + InteractionSuffix(field);
        output.Write(Truncate(value, valueWidth).AsSpan(), style);
    }

    private int ResolveWindowSize(int availableHeight, int visibleCount)
    {
        var reserved = 4;
        var available = availableHeight > reserved ? availableHeight - reserved : MaxVisibleRows;
        return Math.Max(1, Math.Min(visibleCount, Math.Min(MaxVisibleRows, available)));
    }

    private int ResolveLabelWidth(int start, int end, int maxWidth)
    {
        var widest = 1;
        for (var position = start; position < end; position++)
        {
            var index = _model.GetSourceIndexAtVisiblePosition(position);
            widest = Math.Max(widest, UnicodeWidth.GetWidth(_model.Fields[index].Label));
        }

        return Math.Clamp(widest, 1, Math.Max(1, Math.Min(30, maxWidth / 2)));
    }

    private static int ResolveWindowStart(int activePosition, int windowSize, int visibleCount)
        => Math.Max(0, Math.Min(activePosition - (windowSize / 2), visibleCount - windowSize));

    private static string InteractionSuffix(IFormField field)
    {
        if (!field.IsEnabled)
        {
            return "";
        }

        if (field.Interaction.HasFlag(FormFieldInteraction.Change))
        {
            return "  < >";
        }

        return field.Interaction.HasFlag(FormFieldInteraction.Edit)
            || field.Interaction.HasFlag(FormFieldInteraction.Activate)
            ? "  Enter"
            : string.Empty;
    }

    private static string BuildHint(IFormField? field, FormUpdateMode updateMode)
    {
        if (field?.IsEditing == true)
        {
            return "  Enter accept | Esc discard field edit";
        }

        var actions = new List<string> { "Up/Down move" };
        if (field?.Interaction.HasFlag(FormFieldInteraction.Change) == true)
        {
            actions.Add("Left/Right change");
        }

        if (field?.Interaction.HasFlag(FormFieldInteraction.Edit) == true)
        {
            actions.Add("Enter edit");
        }

        if (field?.Interaction.HasFlag(FormFieldInteraction.Activate) == true)
        {
            actions.Add("Enter open");
        }

        actions.Add(updateMode == FormUpdateMode.Live
            ? "Changes save automatically"
            : "Ctrl+Enter save");
        if (updateMode == FormUpdateMode.Live)
        {
            actions.Add("Ctrl+Enter done");
        }

        actions.Add(updateMode == FormUpdateMode.Live ? "Esc back/close" : "Esc cancel");
        return "  " + string.Join(" | ", actions);
    }

    private static IReadOnlyList<string> Wrap(string text, int width)
    {
        if (width <= 1)
        {
            return [Truncate(text, 1)];
        }

        var lines = new List<string>();
        var current = new StringBuilder();
        var currentWidth = 0;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var wordWidth = UnicodeWidth.GetWidth(word);
            if (current.Length > 0 && currentWidth + 1 + wordWidth > width)
            {
                lines.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
            }

            if (current.Length > 0)
            {
                current.Append(' ');
                currentWidth++;
            }

            var fitted = Truncate(word, width);
            current.Append(fitted);
            currentWidth += UnicodeWidth.GetWidth(fitted);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines.Count == 0 ? [string.Empty] : lines;
    }

    private static string Truncate(string text, int width)
    {
        if (width <= 0 || string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (UnicodeWidth.GetWidth(text) <= width)
        {
            return text;
        }

        var target = Math.Max(0, width - 1);
        var builder = new StringBuilder();
        var used = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeWidth = UnicodeWidth.GetWidth(rune);
            if (used + runeWidth > target)
            {
                break;
            }

            builder.Append(rune.ToString());
            used += runeWidth;
        }

        if (width > 0)
        {
            builder.Append('…');
        }

        return builder.ToString();
    }
}
