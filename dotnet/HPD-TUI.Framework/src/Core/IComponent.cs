namespace HPD.TUI.Core;

public interface IComponent
{
    Measurement Measure(in RenderContext context, int maxWidth);

    void Render(in RenderContext context, int maxWidth, ref SegmentWriter output);

    bool HandleInput(in TuiInputEvent input);
}

public interface IFocusable : IComponent
{
    bool IsFocused { get; set; }
}
