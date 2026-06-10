using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Forms;

public sealed class FormView : IFocusable
{
    private readonly FormModel _model;
    private readonly FormController _controller;

    public FormView(FormModel model, FormController controller)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public bool IsFocused { get; set; }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var width = 0;
        foreach (var field in _model.Fields)
        {
            width = Math.Max(width, UnicodeWidth.GetWidth(field.Label) + UnicodeWidth.GetWidth(field.DisplayValue) + 4);
        }

        width = Math.Min(width, maxWidth);
        return new Measurement(Math.Min(width, maxWidth), width);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        for (var i = 0; i < _model.Fields.Count; i++)
        {
            var field = _model.Fields[i];
            var active = i == _model.ActiveFieldIndex;
            var style = active ? context.Theme.Accent : context.Theme.Text;

            output.Write(active ? "> " : "  ", style);
            output.Write(field.Label.AsSpan(), style);
            output.Write(": ", context.Theme.Border);
            output.Write(field.DisplayValue.AsSpan(), style);

            if (!string.IsNullOrEmpty(field.Error))
            {
                output.Write(" ", context.Theme.Border);
                output.Write(field.Error.AsSpan(), context.Theme.Error);
            }
            else if (!string.IsNullOrEmpty(field.Help))
            {
                output.Write(" ", context.Theme.Border);
                output.Write(field.Help.AsSpan(), context.Theme.Border);
            }

            if (i < _model.Fields.Count - 1)
            {
                output.WriteLineBreak();
            }
        }
    }

    public void HandleInput(in KeyEvent key) => _controller.HandleInput(in key);

    public void Invalidate()
    {
    }
}
