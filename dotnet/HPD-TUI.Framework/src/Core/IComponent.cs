namespace HPD.TUI.Core;

public interface IComponent
{
    internal IComponentLifecycle Lifecycle { get; }

    /// <summary>Gets the revision that identifies the component's current layout state.</summary>
    TuiRevision LayoutRevision { get; }

    /// <summary>Gets the revision that identifies the component's current painted state.</summary>
    TuiRevision PaintRevision { get; }

    /// <summary>Gets the render-context fields observed by this component.</summary>
    ComponentDependencies Dependencies { get; }

    Measurement Measure(in RenderContext context, int maxWidth);

    void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output);

    bool HandleInput(in TuiInputEvent input);
}

public interface IFocusable : IComponent
{
    bool IsFocused { get; set; }
}
