using HPDAgent.Graph.Abstractions.Channels;
using HPDAgent.Graph.Abstractions.State;

namespace HPDAgent.Graph.Core.State;

/// <summary>
/// Implementation of IGraphStateScope that maps scope operations to namespaced channel keys.
/// Format: scope.Set("field", value) → channelSet["scope:{scopeName}:field"].Set(value)
/// </summary>
public sealed class GraphStateScope : IGraphStateScope
{
    private readonly IGraphChannelSet _channelSet;
    private const string ScopeSeparator = ":";

    public string? ScopeName { get; }

    public GraphStateScope(IGraphChannelSet channelSet, string? scopeName = null)
    {
        _channelSet = channelSet ?? throw new ArgumentNullException(nameof(channelSet));
        ScopeName = scopeName;
    }

    public T? Get<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        var channelName = BuildChannelName(key);
        if (!_channelSet.Contains(channelName))
        {
            return default;
        }

        return _channelSet[channelName].Get<T>();
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = default;
            return false;
        }

        var channelName = BuildChannelName(key);
        if (!_channelSet.Contains(channelName))
        {
            value = default;
            return false;
        }

        try
        {
            value = _channelSet[channelName].Get<T>();
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    public void Set<T>(string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        var channelName = BuildChannelName(key);
        _channelSet[channelName].Set(value);
    }

    public bool Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var channelName = BuildChannelName(key);
        return _channelSet.Remove(channelName);
    }

    public void Clear()
    {
        var prefix = BuildChannelPrefix();
        var channelsToRemove = _channelSet.ChannelNames
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        foreach (var channelName in channelsToRemove)
        {
            _channelSet.Remove(channelName);
        }
    }

    public IReadOnlyList<string> Keys
    {
        get
        {
            var prefix = BuildChannelPrefix();
            return _channelSet.ChannelNames
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .Select(name => name.Substring(prefix.Length))
                .ToList();
        }
    }

    public IReadOnlyDictionary<string, object> AsDictionary()
    {
        var result = new Dictionary<string, object>();
        var prefix = BuildChannelPrefix();

        foreach (var channelName in _channelSet.ChannelNames)
        {
            if (channelName.StartsWith(prefix, StringComparison.Ordinal))
            {
                var key = channelName.Substring(prefix.Length);
                try
                {
                    var value = _channelSet[channelName].Get<object>();
                    if (value != null)
                    {
                        result[key] = value;
                    }
                }
                catch
                {
                    // Skip channels that can't be read as object
                }
            }
        }

        return result;
    }

    private string BuildChannelName(string key)
    {
        if (string.IsNullOrEmpty(ScopeName))
        {
            return key;
        }

        return $"scope{ScopeSeparator}{ScopeName}{ScopeSeparator}{key}";
    }

    private string BuildChannelPrefix()
    {
        if (string.IsNullOrEmpty(ScopeName))
        {
            // Root scope matches channels that don't have "scope:" prefix
            return string.Empty;
        }

        return $"scope{ScopeSeparator}{ScopeName}{ScopeSeparator}";
    }
}
