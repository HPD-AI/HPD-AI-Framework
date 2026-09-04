namespace HPD.TUI.Core;

/// <summary>Defines a retained, revision-owned terminal user-interface component.</summary>
public interface IComponent
{
    internal IComponentLifecycle Lifecycle { get; }

    /// <summary>Gets the revision that identifies the component's current layout state.</summary>
    TuiRevision LayoutRevision { get; }

    /// <summary>Gets the revision that identifies the component's current painted state.</summary>
    TuiRevision PaintRevision { get; }

    /// <summary>Gets the render-context fields observed by this component.</summary>
    ComponentDependencies Dependencies { get; }

    /// <summary>Measures the component within two-dimensional layout constraints.</summary>
    Measurement Measure(in RenderContext context, Layout.LayoutConstraints constraints);

    /// <summary>Records paint commands into the bounded display-list builder.</summary>
    void Render(in RenderContext context, ref DisplayListBuilder output);

    bool HandleInput(in TuiInputEvent input);
}

/// <summary>Defines a component that can receive keyboard focus.</summary>
public interface IFocusable : IComponent
{
    /// <summary>Gets or sets whether the component currently owns focus.</summary>
    bool IsFocused { get; set; }
}
