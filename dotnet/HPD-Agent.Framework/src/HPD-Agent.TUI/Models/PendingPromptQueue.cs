namespace HPD.Agent.TUI.Models;

internal sealed record PendingPrompt(string ClientInputId, string Text, DateTimeOffset QueuedAt);

internal sealed class PendingPromptQueue
{
    private readonly LinkedList<PendingPrompt> _items = new();

    public int Count => _items.Count;

    public PendingPrompt Enqueue(string text)
    {
        var item = new PendingPrompt(Guid.NewGuid().ToString("N"), text, DateTimeOffset.UtcNow);
        _items.AddLast(item);
        return item;
    }

    public PendingPrompt? PeekOldest() => _items.First?.Value;

    public PendingPrompt? PopNewest()
    {
        if (_items.Last is not { } node) return null;
        _items.RemoveLast();
        return node.Value;
    }

    public bool Remove(string clientInputId)
    {
        for (var node = _items.First; node is not null; node = node.Next)
        {
            if (!string.Equals(node.Value.ClientInputId, clientInputId, StringComparison.Ordinal)) continue;
            _items.Remove(node);
            return true;
        }

        return false;
    }

    public IReadOnlyList<PendingPrompt> Snapshot() => _items.ToArray();
}
