namespace HPD.TUI.Core;

public interface IComponent
{
    Measurement Measure(in RenderContext context, int maxWidth);

    void Render(in RenderContext context, int maxWidth, ref SegmentWriter output);

    void HandleInput(in KeyEvent key);

    void Invalidate();
}

public interface IFocusable : IComponent
{
    bool IsFocused { get; set; }
}
