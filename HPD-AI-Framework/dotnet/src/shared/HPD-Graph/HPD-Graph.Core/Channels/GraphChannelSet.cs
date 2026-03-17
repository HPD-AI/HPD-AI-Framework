using HPDAgent.Graph.Abstractions.Channels;
using System.Collections.Concurrent;

namespace HPDAgent.Graph.Core.Channels;

/// <summary>
/// Thread-safe implementation of IGraphChannelSet.
/// Uses ConcurrentDictionary for thread-safe channel management.
/// </summary>
public sealed class GraphChannelSet : IGraphChannelSet
{
    private readonly ConcurrentDictionary<string, IGraphChannel> _channels = new();

    public IGraphChannel this[string name]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Channel name cannot be null or whitespace.", nameof(name));
            }

            // Get or create a LastValueChannel by default
            return _channels.GetOrAdd(name, n => new LastValueChannel(n));
        }
    }

    public IReadOnlyList<string> ChannelNames =>
        _channels.Keys.OrderBy(k => k).ToList();

    public bool Contains(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return _channels.ContainsKey(name);
    }

    public bool Remove(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return _channels.TryRemove(name, out _);
    }

    public void Clear()
    {
        _channels.Clear();
    }

    /// <summary>
    /// Add a pre-configured channel to the set.
    /// Useful for adding channels with specific semantics (Append, Reducer, etc.).
    /// </summary>
    /// <param name="channel">Channel to add</param>
    /// <exception cref="ArgumentNullException">Thrown if channel is null</exception>
    /// <exception cref="ArgumentException">Thrown if channel with same name already exists</exception>
    public void Add(IGraphChannel channel)
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        if (!_channels.TryAdd(channel.Name, channel))
        {
            throw new ArgumentException(
                $"Channel with name '{channel.Name}' already exists.",
                nameof(channel));
        }
    }
}
