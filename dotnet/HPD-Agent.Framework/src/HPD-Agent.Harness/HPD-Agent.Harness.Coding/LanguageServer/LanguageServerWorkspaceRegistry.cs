using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace HPDOS.ToolHarnesses.Middleware;

/// <summary>Agent-owned registry for language-server processes shared by concurrent workspace executions.</summary>
public sealed class LanguageServerWorkspaceRegistry : ILanguageServerWorkspaceRegistry
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<LanguageServerOptions, ILanguageServerService> _createService;
    private int _disposed;

    /// <summary>Creates an Agent-owned registry that constructs language-server services lazily by workspace key.</summary>
    public LanguageServerWorkspaceRegistry() : this(static options => new LanguageServerService(options)) { }

    internal LanguageServerWorkspaceRegistry(Func<LanguageServerOptions, ILanguageServerService> createService) =>
        _createService = createService ?? throw new ArgumentNullException(nameof(createService));

    public ILanguageServerWorkspaceLease Acquire(string canonicalWorkspaceIdentity, LanguageServerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalWorkspaceIdentity);
        ArgumentNullException.ThrowIfNull(options);
        var configuredWorkspaces = options.WorkspaceFolders
            .Select(static path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var workspace = configuredWorkspaces.Length switch
        {
            0 => Path.TrimEndingDirectorySeparator(Path.GetFullPath(canonicalWorkspaceIdentity)),
            1 => configuredWorkspaces[0],
            _ => string.Join("\n", configuredWorkspaces)
        };
        var effectiveOptions = options.WorkspaceFolders.Count == 0
            ? options with { WorkspaceFolders = [workspace] }
            : options;
        var key = $"{workspace}\n{GetConfigurationIdentity(effectiveOptions)}";
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var entry = _entries.GetOrAdd(key, _ => new Entry(_createService(effectiveOptions)));
            entry.AddReference();
            return new Lease(entry);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Entry[] entries;
        lock (_gate)
        {
            if (_disposed != 0) return;
            _disposed = 1;
            entries = _entries.Values.ToArray();
            _entries.Clear();
        }
        List<Exception>? failures = null;
        foreach (var entry in entries)
        {
            try { await entry.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (failures is { Count: > 0 })
            throw new AggregateException("Language-server workspace shutdown failed.", failures);
    }

    private static string GetConfigurationIdentity(LanguageServerOptions options)
    {
        if (options.WorkspaceConfiguration.Count > 0 &&
            string.IsNullOrWhiteSpace(options.WorkspaceConfigurationIdentity))
        {
            throw new InvalidOperationException(
                "LanguageServerOptions.WorkspaceConfigurationIdentity is required when WorkspaceConfiguration is non-empty.");
        }

        var identity = new StringBuilder();
        Append(identity, options.Enabled ? "1" : "0");
        foreach (var server in options.Servers.OrderBy(static server => server.Id, StringComparer.Ordinal))
        {
            Append(identity, server.Id);
            foreach (var extension in server.Extensions.Order(StringComparer.Ordinal)) Append(identity, extension);
            foreach (var pair in server.LanguageIds.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                Append(identity, pair.Key);
                Append(identity, pair.Value);
            }
            Append(identity, server.Provider.GetType().AssemblyQualifiedName ?? server.Provider.GetType().FullName ?? server.Provider.GetType().Name);
            Append(identity, server.Provider.ConfigurationIdentity);
            Append(identity, server.EnabledByDefault ? "1" : "0");
            Append(identity, server.Experimental ? "1" : "0");
        }
        foreach (var value in options.EnabledServers.Order(StringComparer.Ordinal)) Append(identity, value);
        foreach (var value in options.DisabledServers.Order(StringComparer.Ordinal)) Append(identity, value);
        foreach (var value in options.EnabledExperimentalServers.Order(StringComparer.Ordinal)) Append(identity, value);
        foreach (var value in options.WorkspaceFolders.Select(Path.GetFullPath).Order(StringComparer.Ordinal)) Append(identity, value);
        Append(identity, options.WorkspaceConfigurationIdentity ?? string.Empty);
        Append(identity, options.ConfigVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())));
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value);

    private sealed class Entry(ILanguageServerService service) : IAsyncDisposable
    {
        private int _references;
        private int _disposed;
        internal ILanguageServerService Service { get; } = service;
        internal void AddReference()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            Interlocked.Increment(ref _references);
        }
        internal void Release()
        {
            if (Interlocked.Decrement(ref _references) < 0)
                throw new InvalidOperationException("Language-server workspace lease was released more than once.");
        }
        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _disposed, 1) == 0 ? Service.DisposeAsync() : ValueTask.CompletedTask;
    }

    private sealed class Lease(Entry entry) : ILanguageServerWorkspaceLease
    {
        private Entry? _entry = entry;
        public ILanguageServerService Service => (_entry ?? throw new ObjectDisposedException(nameof(Lease))).Service;
        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _entry, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
