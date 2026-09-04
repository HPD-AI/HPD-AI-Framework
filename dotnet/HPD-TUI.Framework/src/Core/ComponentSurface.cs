namespace HPD.TUI.Core;

internal sealed class ComponentSurface(Action requestRender, Func<bool>? checkAccess = null)
{
    private static long _nextSurfaceId;
    private readonly SurfaceId _surfaceId = new(Interlocked.Increment(ref _nextSurfaceId));
    private readonly Dictionary<ComponentId, AttachmentGeneration> _attachments = [];
    private readonly Dictionary<ComponentId, IComponent> _components = [];
    private SurfaceGeneration _surfaceGeneration;
    private AttachmentGeneration _attachmentGeneration;
    private readonly IMailboxAccessGuard _mailbox = new MailboxAccessGuard(checkAccess ?? (() => true));

    public IComponent? Root { get; private set; }

    public void ReplaceRoot(IComponent? root)
    {
        _mailbox.AssertAccess();
        if (ReferenceEquals(Root, root)) return;
        if (root is not null) ValidateSubtree(root, expectedParent: null);

        var previous = Root;
        if (previous is not null) DetachSubtree(previous);
        try
        {
            AdvanceSurfaceGeneration();
            if (root is not null) AttachTransactional(root, parent: null);
            Root = root;
        }
        catch
        {
            if (previous is not null) AttachTransactional(previous, parent: null);
            Root = previous;
            throw;
        }
        requestRender();
    }

    public void Detach() => ReplaceRoot(null);

    private void AttachTransactional(IComponent component, ComponentId? parent)
    {
        ValidateSubtree(component, parent);
        var attached = new List<IComponent>();
        try { AttachSubtree(component, parent, attached); }
        catch
        {
            for (var index = attached.Count - 1; index >= 0; index--) DetachOne(attached[index]);
            throw;
        }
    }

    private void AttachSubtree(IComponent component, ComponentId? parent, List<IComponent> attached)
    {
        var generation = new AttachmentGeneration(Next(_attachmentGeneration.Value));
        _attachmentGeneration = generation;
        _attachments.Add(component.Lifecycle.Id, generation);
        _components.Add(component.Lifecycle.Id, component);
        try
        {
            component.Lifecycle.Attach(new ComponentAttachment(_surfaceId, _surfaceGeneration, generation, parent,
                _mailbox, Invalidate, HandleRevisionOverflow, () => _surfaceGeneration, AttachChild, DetachChild));
            attached.Add(component);
        }
        catch
        {
            _attachments.Remove(component.Lifecycle.Id);
            _components.Remove(component.Lifecycle.Id);
            throw;
        }

        if (component is Component owned)
            foreach (var child in owned.OwnedChildren)
                AttachSubtree(child, component.Lifecycle.Id, attached);
    }

    private static void ValidateSubtree(IComponent root, ComponentId? expectedParent)
    {
        var seen = new HashSet<ComponentId>();
        Validate(root, expectedParent);
        void Validate(IComponent component, ComponentId? parent)
        {
            if (!seen.Add(component.Lifecycle.Id))
                throw new InvalidOperationException("The component ownership tree contains a cycle or duplicate child.");
            if (component.Lifecycle.Attachment is not null)
                throw new InvalidOperationException("A component can be attached to only one surface at a time.");
            if (component.Lifecycle.OwnerParent != parent)
                throw new InvalidOperationException("The component ownership tree does not match its attachment parent.");
            if (component is Component owner)
                foreach (var child in owner.OwnedChildren) Validate(child, component.Lifecycle.Id);
        }
    }

    private void DetachSubtree(IComponent component)
    {
        if (component is Component owned)
            for (var index = owned.OwnedChildren.Count - 1; index >= 0; index--)
                DetachSubtree(owned.OwnedChildren[index]);

        DetachOne(component);
    }

    private void DetachOne(IComponent component)
    {
        if (_attachments.Remove(component.Lifecycle.Id, out var generation))
        {
            component.Lifecycle.Detach(generation);
            _components.Remove(component.Lifecycle.Id);
        }
    }

    private void AttachChild(IComponent child, ComponentId parent)
    {
        _mailbox.AssertAccess();
        if (!_attachments.ContainsKey(parent)) return;
        AdvanceSurfaceGeneration();
        AttachTransactional(child, parent);
        requestRender();
    }

    private void DetachChild(IComponent child)
    {
        _mailbox.AssertAccess();
        DetachSubtree(child);
        AdvanceSurfaceGeneration();
        requestRender();
    }

    private void Invalidate(ComponentId id, AttachmentGeneration generation, bool layout)
    {
        if (!_attachments.TryGetValue(id, out var current) || current != generation) return;
        if (layout && _components.TryGetValue(id, out var component))
        {
            var parent = component.Lifecycle.Attachment?.Parent;
            while (parent is { } parentId && _components.TryGetValue(parentId, out var ancestor))
            {
                if (ancestor is Component owner) owner.PropagateDescendantLayoutInvalidation();
                parent = ancestor.Lifecycle.Attachment?.Parent;
            }
        }
        requestRender();
    }

    private void HandleRevisionOverflow()
    {
        _mailbox.AssertAccess();
        AdvanceSurfaceGeneration();
        requestRender();
    }

    private void AdvanceSurfaceGeneration() => _surfaceGeneration = new(Next(_surfaceGeneration.Value));

    private static ulong Next(ulong value) => value == ulong.MaxValue ? 1 : value + 1;
}
