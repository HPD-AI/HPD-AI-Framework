namespace HPD.TUI.Core;

internal sealed class ComponentSurface(Action requestRender)
{
    private static long _nextSurfaceId;
    private readonly long _surfaceId = Interlocked.Increment(ref _nextSurfaceId);
    private readonly Dictionary<ComponentId, ulong> _attachments = [];
    private ulong _surfaceGeneration;
    private ulong _attachmentGeneration;

    public IComponent? Root { get; private set; }

    public void ReplaceRoot(IComponent? root)
    {
        if (ReferenceEquals(Root, root)) return;
        if (root?.Lifecycle.OwnerParent is not null)
            throw new InvalidOperationException("A surface root cannot be owned by another component.");

        if (Root is not null) DetachSubtree(Root);
        Root = root;
        _surfaceGeneration = Next(_surfaceGeneration);
        if (root is not null) AttachSubtree(root, parent: null);
        requestRender();
    }

    public void Detach() => ReplaceRoot(null);

    private void AttachSubtree(IComponent component, ComponentId? parent)
    {
        var generation = Next(_attachmentGeneration);
        _attachmentGeneration = generation;
        _attachments.Add(component.Lifecycle.Id, generation);
        component.Lifecycle.Attach(new ComponentAttachment(
            _surfaceId,
            _surfaceGeneration,
            generation,
            parent,
            Invalidate,
            AttachChild,
            DetachChild));

        if (component is Component owned)
            foreach (var child in owned.OwnedChildren)
                AttachSubtree(child, component.Lifecycle.Id);
    }

    private void DetachSubtree(IComponent component)
    {
        if (component is Component owned)
            for (var index = owned.OwnedChildren.Count - 1; index >= 0; index--)
                DetachSubtree(owned.OwnedChildren[index]);

        if (_attachments.Remove(component.Lifecycle.Id, out var generation))
            component.Lifecycle.Detach(generation);
    }

    private void AttachChild(IComponent child, ComponentId parent)
    {
        if (!_attachments.ContainsKey(parent)) return;
        AttachSubtree(child, parent);
        requestRender();
    }

    private void DetachChild(IComponent child)
    {
        DetachSubtree(child);
        requestRender();
    }

    private void Invalidate(ComponentId id, ulong generation, bool layout)
    {
        if (_attachments.TryGetValue(id, out var current) && current == generation)
            requestRender();
    }

    private static ulong Next(ulong value) => value == ulong.MaxValue ? 1 : value + 1;
}
