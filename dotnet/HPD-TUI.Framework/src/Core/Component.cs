using System.Diagnostics;
using HPD.TUI.Observability;

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

    /// <summary>Gets the dependency policy for nonanimated components whose layout is constraint-driven and whose paint uses the theme.</summary>
    public static ComponentDependencies Static { get; } =
        new(RenderContextFields.Width | RenderContextFields.Height,
            RenderContextFields.Width | RenderContextFields.Height | RenderContextFields.Theme |
            RenderContextFields.ColorSystem | RenderContextFields.Capabilities);
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
    private readonly Dictionary<LayoutCacheKey, CachedChildLayout> _layoutCache = [];
    private ulong _layoutRevision = 1;
    private ulong _paintRevision = 1;
    private Component? _ownerComponent;

    /// <summary>Initializes a component and its stable lifecycle identity.</summary>
    protected Component() => _lifecycle = new ComponentLifecycle(this);

    IComponentLifecycle IComponent.Lifecycle => _lifecycle;

    internal IReadOnlyList<IComponent> OwnedChildren => _ownedChildren;

    /// <summary>Gets whether this owner is a propagation boundary for descendant layout invalidation.</summary>
    internal bool EstablishesLayoutRoot => _ownedChildren.Count != 0;

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
        _lifecycle.AssertMutationAccess();
        field = value;
        InvalidatePaint();
        return true;
    }

    /// <summary>Updates a layout field and invalidates both measurement and visible output when it changed.</summary>
    protected bool SetLayout<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        _lifecycle.AssertMutationAccess();
        field = value;
        InvalidateLayout();
        return true;
    }

    /// <summary>Transfers exclusive ownership of a child to this component.</summary>
    protected void AdoptChild(IComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _lifecycle.AssertMutationAccess();
        if (Contains(child, this))
            throw new InvalidOperationException("Adopting this component would create an ownership cycle.");
        child.Lifecycle.ValidateAdopt(_lifecycle.Id);
        child.Lifecycle.Adopt(_lifecycle.Id);
        try
        {
            if (_lifecycle.Attachment is { } attachment)
                attachment.AttachChild(child, _lifecycle.Id);
            _ownedChildren.Add(child);
            if (child is Component ownedChild) ownedChild._ownerComponent = this;
        }
        catch
        {
            if (child.Lifecycle.Attachment is not null && _lifecycle.Attachment is { } attached)
                attached.DetachChild(child);
            child.Lifecycle.Release(_lifecycle.Id);
            throw;
        }
    }

    /// <summary>Transfers a collection of children atomically, rolling back earlier transfers if a later child is invalid.</summary>
    protected void AdoptChildren(IEnumerable<IComponent> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        _lifecycle.AssertMutationAccess();
        var candidates = children.ToArray();
        var identities = new HashSet<ComponentId>();
        foreach (var child in candidates)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (!identities.Add(child.Lifecycle.Id))
                throw new InvalidOperationException("A child cannot occur more than once in an ownership operation.");
            if (Contains(child, this))
                throw new InvalidOperationException("Adopting this component would create an ownership cycle.");
            child.Lifecycle.ValidateAdopt(_lifecycle.Id);
        }
        var adopted = new List<IComponent>();
        try
        {
            foreach (var child in candidates)
            {
                AdoptChild(child);
                adopted.Add(child);
            }
        }
        catch
        {
            for (var index = adopted.Count - 1; index >= 0; index--) ReleaseChild(adopted[index]);
            throw;
        }
    }

    /// <summary>Releases an owned child after detaching its subtree from the current surface.</summary>
    protected void ReleaseChild(IComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _lifecycle.AssertMutationAccess();
        if (!_ownedChildren.Contains(child))
            throw new InvalidOperationException("The component is not owned by this parent.");
        if (_lifecycle.Attachment is { } attachment) attachment.DetachChild(child);
        child.Lifecycle.Release(_lifecycle.Id);
        _ownedChildren.Remove(child);
        if (child is Component ownedChild) ownedChild._ownerComponent = null;
    }

    /// <summary>Measures a child through this layout root's revision-keyed cache.</summary>
    protected Measurement MeasureChild(IComponent child, in RenderContext context, int maxWidth)
        => MeasureChild(child, in context, Layout.LayoutConstraints.Loose(maxWidth, context.Height));

    /// <summary>Measures a child through this layout root's revision-keyed cache.</summary>
    protected Measurement MeasureChild(IComponent child, in RenderContext context, Layout.LayoutConstraints constraints)
        => MeasureChild(child, in context, constraints, 0, 0);

    /// <summary>Measures a child and retains its resolved origin with the measurement in this layout root.</summary>
    protected Measurement MeasureChild(
        IComponent child,
        in RenderContext context,
        Layout.LayoutConstraints constraints,
        int x,
        int y)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (child.LayoutCachePolicy == LayoutCachePolicy.None)
            return MeasureAndRecord(child, in context, constraints);
        var key = CreateLayoutCacheKey(child, in context, constraints);
        if (_layoutCache.TryGetValue(key, out var cached) && cached.Bounds.X == x && cached.Bounds.Y == y)
        {
            TuiInstrumentationContext.RecordLayout(cacheHit: true, 0);
            return cached.Measurement;
        }
        if (_layoutCache.Count >= 256) _layoutCache.Clear();
        var measured = MeasureAndRecord(child, in context, constraints);
        var bounds = new Layout.LayoutRect(x, y, Math.Min(constraints.MaxWidth, measured.MaxWidth), measured.Height);
        _layoutCache[key] = new CachedChildLayout(measured, bounds);
        return measured;
    }

    private static Measurement MeasureAndRecord(
        IComponent child,
        in RenderContext context,
        Layout.LayoutConstraints constraints)
    {
        if (!TuiInstrumentationContext.IsEnabled)
            return child.Measure(in context, constraints);
        var start = Stopwatch.GetTimestamp();
        try { return child.Measure(in context, constraints); }
        finally
        {
            TuiInstrumentationContext.RecordLayout(
                cacheHit: false, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Reads the resolved bounds retained with a child's current measurement.</summary>
    protected bool TryGetResolvedChildBounds(
        IComponent child,
        in RenderContext context,
        Layout.LayoutConstraints constraints,
        out Layout.LayoutRect bounds)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_layoutCache.TryGetValue(CreateLayoutCacheKey(child, in context, constraints), out var cached))
        {
            bounds = cached.Bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    /// <summary>Advances the paint revision and reports paint damage to the attached surface.</summary>
    protected void InvalidatePaint()
    {
        _lifecycle.AssertMutationAccess();
        _paintRevision = NextRevision(_paintRevision);
        _ownerComponent?.PropagateDescendantPaintInvalidation();
        _lifecycle.Invalidate(layout: false);
    }

    /// <summary>Advances layout and paint revisions and invalidates the attached layout root.</summary>
    protected void InvalidateLayout()
    {
        _lifecycle.AssertMutationAccess();
        _layoutCache.Clear();
        _layoutRevision = NextRevision(_layoutRevision);
        _paintRevision = NextRevision(_paintRevision);
        _ownerComponent?.PropagateDescendantLayoutInvalidation();
        _lifecycle.Invalidate(layout: true);
    }

    /// <inheritdoc />
    public abstract Measurement Measure(in RenderContext context, Layout.LayoutConstraints constraints);

    /// <inheritdoc />
    public abstract void Render(in RenderContext context, ref DisplayListBuilder output);

    /// <inheritdoc />
    public virtual bool HandleInput(in TuiInputEvent input) => false;

    internal void PropagateDescendantLayoutInvalidation()
    {
        _layoutCache.Clear();
        _layoutRevision = NextRevision(_layoutRevision);
        _paintRevision = NextRevision(_paintRevision);
        _ownerComponent?.PropagateDescendantLayoutInvalidation();
    }

    /// <summary>Advances the retained subtree stamp along the owning dirty path.</summary>
    internal void PropagateDescendantPaintInvalidation()
    {
        _paintRevision = NextRevision(_paintRevision);
        _ownerComponent?.PropagateDescendantPaintInvalidation();
    }

    private ulong NextRevision(ulong value)
    {
        if (value != ulong.MaxValue) return value + 1;
        _lifecycle.HandleRevisionOverflow();
        _layoutCache.Clear();
        return 1;
    }

    private static bool Contains(IComponent root, IComponent sought)
    {
        if (ReferenceEquals(root, sought)) return true;
        if (root is not Component owner) return false;
        foreach (var child in owner._ownedChildren)
            if (Contains(child, sought)) return true;
        return false;
    }

    private static LayoutCacheKey CreateLayoutCacheKey(
        IComponent child,
        in RenderContext context,
        Layout.LayoutConstraints constraints)
    {
        var fields = child.Dependencies.Layout;
        return new LayoutCacheKey(
            child.Lifecycle.Id,
            child.LayoutRevision,
            constraints,
            (fields & RenderContextFields.Width) != 0 ? context.Width : default,
            (fields & RenderContextFields.Height) != 0 ? context.Height : default,
            (fields & RenderContextFields.Theme) != 0 ? context.Theme.Key : default,
            (fields & RenderContextFields.ColorSystem) != 0 ? context.ColorSystem : default,
            (fields & RenderContextFields.Capabilities) != 0 ? context.Capabilities : default,
            (fields & RenderContextFields.Elapsed) != 0 ? context.Elapsed : default);
    }

    private readonly record struct CachedChildLayout(Measurement Measurement, Layout.LayoutRect Bounds);

    private readonly record struct LayoutCacheKey(
        ComponentId Component,
        TuiRevision Revision,
        Layout.LayoutConstraints Constraints,
        int ContextWidth,
        int Height,
        ThemeKey Theme,
        ColorSystem ColorSystem,
        TerminalCapabilities Capabilities,
        TimeSpan Elapsed);
}

internal interface IComponentLifecycle
{
    ComponentId Id { get; }
    ComponentId? OwnerParent { get; }
    ComponentAttachment? Attachment { get; }
    void AssertMutationAccess();
    void ValidateAdopt(ComponentId parent);
    void Adopt(ComponentId parent);
    void ValidateRelease(ComponentId expectedParent);
    void Release(ComponentId expectedParent);
    void Attach(in ComponentAttachment attachment);
    void Detach(AttachmentGeneration expectedGeneration);
    void HandleRevisionOverflow();
}

internal readonly record struct ComponentId(long Value);
internal readonly record struct SurfaceId(long Value);
internal readonly record struct SurfaceGeneration(ulong Value);
internal readonly record struct AttachmentGeneration(ulong Value);

internal readonly record struct ComponentAttachment(
    SurfaceId SurfaceId,
    SurfaceGeneration SurfaceGeneration,
    AttachmentGeneration AttachmentGeneration,
    ComponentId? Parent,
    IMailboxAccessGuard Mailbox,
    Action<ComponentId, AttachmentGeneration, bool> Invalidate,
    Action RevisionOverflow,
    Func<SurfaceGeneration> CurrentSurfaceGeneration,
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

    public void AssertMutationAccess() => Attachment?.Mailbox.AssertAccess();

    public void ValidateAdopt(ComponentId parent)
    {
        AssertMutationAccess();
        if (parent == Id) throw new InvalidOperationException("A component cannot own itself.");
        if (OwnerParent is not null) throw new InvalidOperationException("A component can have only one owning parent.");
    }

    public void Adopt(ComponentId parent)
    {
        ValidateAdopt(parent);
        OwnerParent = parent;
    }

    public void ValidateRelease(ComponentId expectedParent)
    {
        AssertMutationAccess();
        if (Attachment is not null) throw new InvalidOperationException("An attached component must be detached before release.");
        if (OwnerParent != expectedParent) throw new InvalidOperationException("The component is not owned by the expected parent.");
    }

    public void Release(ComponentId expectedParent)
    {
        ValidateRelease(expectedParent);
        OwnerParent = null;
    }

    public void Attach(in ComponentAttachment attachment)
    {
        attachment.Mailbox.AssertAccess();
        if (Attachment is not null) throw new InvalidOperationException("The component is already attached.");
        Attachment = attachment;
        try
        {
            attachment.Invalidate(Id, attachment.AttachmentGeneration, true);
        }
        catch
        {
            Attachment = null;
            throw;
        }
    }

    public void Detach(AttachmentGeneration expectedGeneration)
    {
        Attachment?.Mailbox.AssertAccess();
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

    public void HandleRevisionOverflow() => Attachment?.RevisionOverflow();
}
