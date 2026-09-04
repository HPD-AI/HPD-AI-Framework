namespace HPD.TUI.Core;

/// <summary>Identifies a monotonically increasing component state revision.</summary>
/// <param name="Value">The nonzero revision value.</param>
public readonly record struct TuiRevision(ulong Value)
{
    /// <summary>Gets the first valid revision.</summary>
    public static TuiRevision Initial { get; } = new(1);
}

/// <summary>Identifies render-context values that can affect component output.</summary>
[Flags]
public enum RenderContextFields
{
    /// <summary>No contextual values are observed.</summary>
    None = 0,
    /// <summary>Available width is observed.</summary>
    Width = 1 << 0,
    /// <summary>Available height is observed.</summary>
    Height = 1 << 1,
    /// <summary>The theme is observed.</summary>
    Theme = 1 << 2,
    /// <summary>The terminal color system is observed.</summary>
    ColorSystem = 1 << 3,
    /// <summary>Terminal capabilities are observed.</summary>
    Capabilities = 1 << 4,
    /// <summary>Admitted animation time is observed.</summary>
    Elapsed = 1 << 5,
    /// <summary>Every contextual value is observed.</summary>
    All = Width | Height | Theme | ColorSystem | Capabilities | Elapsed
}

/// <summary>Declares the contextual dependencies of measurement and painting.</summary>
/// <param name="Layout">Fields that affect measurement or placement.</param>
/// <param name="Paint">Fields that affect visible output.</param>
public readonly record struct ComponentDependencies(RenderContextFields Layout, RenderContextFields Paint)
{
    /// <summary>Gets the safe dependency policy for components that have not opted into narrower caching.</summary>
    public static ComponentDependencies Conservative { get; } =
        new(RenderContextFields.All, RenderContextFields.All);
}

/// <summary>Base class for attachable TUI components with framework-owned revision tracking.</summary>
public abstract class Component : IComponent
{
    private readonly ComponentLifecycle _lifecycle;
    private ulong _layoutRevision = 1;
    private ulong _paintRevision = 1;

    /// <summary>Initializes a component and its stable lifecycle identity.</summary>
    protected Component() => _lifecycle = new ComponentLifecycle(this);

    IComponentLifecycle IComponent.Lifecycle => _lifecycle;

    /// <inheritdoc />
    public TuiRevision LayoutRevision => new(_layoutRevision);

    /// <inheritdoc />
    public TuiRevision PaintRevision => new(_paintRevision);

    /// <inheritdoc />
    public virtual ComponentDependencies Dependencies => ComponentDependencies.Conservative;

    /// <summary>Updates a paint-only field and invalidates visible output when its value changed.</summary>
    protected bool SetPaint<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        InvalidatePaint();
        return true;
    }

    /// <summary>Updates a layout field and invalidates both measurement and visible output when it changed.</summary>
    protected bool SetLayout<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        InvalidateLayout();
        return true;
    }

    /// <summary>Advances the paint revision and reports paint damage to the attached surface.</summary>
    protected void InvalidatePaint()
    {
        _paintRevision = Next(_paintRevision);
        _lifecycle.Invalidate(layout: false);
    }

    /// <summary>Advances layout and paint revisions and invalidates the attached layout root.</summary>
    protected void InvalidateLayout()
    {
        _layoutRevision = Next(_layoutRevision);
        _paintRevision = Next(_paintRevision);
        _lifecycle.Invalidate(layout: true);
    }

    /// <inheritdoc />
    public abstract Measurement Measure(in RenderContext context, int maxWidth);

    /// <inheritdoc />
    public abstract void Render(in RenderContext context, int maxWidth, ref SegmentWriter output);

    /// <inheritdoc />
    public virtual bool HandleInput(in TuiInputEvent input) => false;

    private static ulong Next(ulong value) => value == ulong.MaxValue ? 1 : value + 1;
}

internal interface IComponentLifecycle
{
    ComponentId Id { get; }
    ComponentId? OwnerParent { get; }
    ComponentAttachment? Attachment { get; }
    void Adopt(ComponentId parent);
    void Release(ComponentId expectedParent);
    void Attach(in ComponentAttachment attachment);
    void Detach(ulong expectedGeneration);
}

internal readonly record struct ComponentId(long Value);

internal readonly record struct ComponentAttachment(
    long SurfaceId,
    ulong SurfaceGeneration,
    ulong AttachmentGeneration,
    ComponentId? Parent,
    Action<ComponentId, ulong, bool> Invalidate);

internal sealed class ComponentLifecycle(Component owner) : IComponentLifecycle
{
    private static long _nextId;
    private readonly Component _owner = owner;
    public ComponentId Id { get; } = new(Interlocked.Increment(ref _nextId));
    public ComponentId? OwnerParent { get; private set; }
    public ComponentAttachment? Attachment { get; private set; }

    public void Adopt(ComponentId parent)
    {
        if (parent == Id) throw new InvalidOperationException("A component cannot own itself.");
        if (OwnerParent is not null) throw new InvalidOperationException("A component can have only one owning parent.");
        OwnerParent = parent;
    }

    public void Release(ComponentId expectedParent)
    {
        if (Attachment is not null) throw new InvalidOperationException("An attached component must be detached before release.");
        if (OwnerParent != expectedParent) throw new InvalidOperationException("The component is not owned by the expected parent.");
        OwnerParent = null;
    }

    public void Attach(in ComponentAttachment attachment)
    {
        if (Attachment is not null) throw new InvalidOperationException("The component is already attached.");
        Attachment = attachment;
        attachment.Invalidate(Id, attachment.AttachmentGeneration, true);
    }

    public void Detach(ulong expectedGeneration)
    {
        if (Attachment is not { } current || current.AttachmentGeneration != expectedGeneration)
            throw new InvalidOperationException("The component is not attached with the expected generation.");
        Attachment = null;
        current.Invalidate(Id, current.AttachmentGeneration, true);
    }

    public void Invalidate(bool layout)
    {
        if (Attachment is { } attachment)
            attachment.Invalidate(Id, attachment.AttachmentGeneration, layout);
    }
}
