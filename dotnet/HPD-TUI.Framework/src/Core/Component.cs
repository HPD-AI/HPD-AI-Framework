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

/// <summary>Controls whether a component's measurements may be retained by an owning layout root.</summary>
public enum LayoutCachePolicy
{
    /// <summary>Do not retain measurements; invoke <see cref="IComponent.Measure"/> for every layout pass.</summary>
    None,
    /// <summary>Retain measurements under complete revision, constraint, and dependency keys.</summary>
    RevisionKeyed
}

/// <summary>Base class for attachable TUI components with framework-owned revision tracking.</summary>
public abstract class Component : IComponent
{
    private readonly ComponentLifecycle _lifecycle;
    private readonly List<IComponent> _ownedChildren = [];
    private readonly Dictionary<LayoutCacheKey, Measurement> _layoutCache = [];
    private ulong _layoutRevision = 1;
    private ulong _paintRevision = 1;

    /// <summary>Initializes a component and its stable lifecycle identity.</summary>
    protected Component() => _lifecycle = new ComponentLifecycle(this);

    IComponentLifecycle IComponent.Lifecycle => _lifecycle;

    internal IReadOnlyList<IComponent> OwnedChildren => _ownedChildren;

    /// <inheritdoc />
    public TuiRevision LayoutRevision => new(_layoutRevision);

    /// <inheritdoc />
    public TuiRevision PaintRevision => new(_paintRevision);

    /// <inheritdoc />
    public virtual ComponentDependencies Dependencies => ComponentDependencies.Conservative;

    /// <inheritdoc />
    public virtual LayoutCachePolicy LayoutCachePolicy => LayoutCachePolicy.RevisionKeyed;

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

    /// <summary>Transfers exclusive ownership of a child to this component.</summary>
    protected void AdoptChild(IComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (Contains(child, this))
            throw new InvalidOperationException("Adopting this component would create an ownership cycle.");
        child.Lifecycle.Adopt(_lifecycle.Id);
        _ownedChildren.Add(child);
        if (_lifecycle.Attachment is { } attachment)
            attachment.AttachChild(child, _lifecycle.Id);
    }

    /// <summary>Releases an owned child after detaching its subtree from the current surface.</summary>
    protected void ReleaseChild(IComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!_ownedChildren.Contains(child))
            throw new InvalidOperationException("The component is not owned by this parent.");
        if (_lifecycle.Attachment is { } attachment)
            attachment.DetachChild(child);
        child.Lifecycle.Release(_lifecycle.Id);
        _ownedChildren.Remove(child);
    }

    /// <summary>Measures a child through this layout root's revision-keyed cache.</summary>
    protected Measurement MeasureChild(IComponent child, in RenderContext context, int maxWidth)
        => MeasureChild(child, in context, Layout.LayoutConstraints.Loose(maxWidth, context.Height));

    /// <summary>Measures a child through this layout root's revision-keyed cache.</summary>
    protected Measurement MeasureChild(IComponent child, in RenderContext context, Layout.LayoutConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.LayoutCachePolicy == LayoutCachePolicy.None)
            return child.Measure(in context, constraints);
        var maxWidth = constraints.MaxWidth;
        var fields = child.Dependencies.Layout;
        var key = new LayoutCacheKey(
            child.Lifecycle.Id,
            child.LayoutRevision,
            maxWidth,
            context.Height,
            (fields & RenderContextFields.Theme) != 0 ? context.Theme.Key : default,
            (fields & RenderContextFields.ColorSystem) != 0 ? context.ColorSystem : default,
            (fields & RenderContextFields.Elapsed) != 0 ? context.Elapsed : default);
        if (_layoutCache.TryGetValue(key, out var cached)) return cached;
        if (_layoutCache.Count >= 256) _layoutCache.Clear();
        var measured = child.Measure(in context, constraints);
        _layoutCache.Add(key, measured);
        return measured;
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
        _layoutCache.Clear();
        _layoutRevision = Next(_layoutRevision);
        _paintRevision = Next(_paintRevision);
        _lifecycle.Invalidate(layout: true);
    }

    /// <inheritdoc />
    public abstract Measurement Measure(in RenderContext context, Layout.LayoutConstraints constraints);

    /// <inheritdoc />
    public abstract void Render(in RenderContext context, ref DisplayListBuilder output);

    /// <inheritdoc />
    public virtual bool HandleInput(in TuiInputEvent input) => false;

    private static ulong Next(ulong value) => value == ulong.MaxValue ? 1 : value + 1;

    private static bool Contains(IComponent root, IComponent sought)
    {
        if (ReferenceEquals(root, sought)) return true;
        if (root is not Component owner) return false;
        foreach (var child in owner._ownedChildren)
            if (Contains(child, sought)) return true;
        return false;
    }

    private readonly record struct LayoutCacheKey(
        ComponentId Component,
        TuiRevision Revision,
        int Width,
        int Height,
        ThemeKey Theme,
        ColorSystem ColorSystem,
        TimeSpan Elapsed);
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
    IMailboxAccessGuard Mailbox,
    Action<ComponentId, ulong, bool> Invalidate,
    Action<IComponent, ComponentId> AttachChild,
    Action<IComponent> DetachChild);

internal interface IMailboxAccessGuard
{
    bool CheckAccess();
    void AssertAccess();
}

internal sealed class MailboxAccessGuard(Func<bool> checkAccess) : IMailboxAccessGuard
{
    public bool CheckAccess() => checkAccess();

    public void AssertAccess()
    {
        if (!CheckAccess())
            throw new InvalidOperationException("Attached component state may be mutated only on its owning application mailbox.");
    }
}

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
        {
            attachment.Mailbox.AssertAccess();
            attachment.Invalidate(Id, attachment.AttachmentGeneration, layout);
        }
    }
}
