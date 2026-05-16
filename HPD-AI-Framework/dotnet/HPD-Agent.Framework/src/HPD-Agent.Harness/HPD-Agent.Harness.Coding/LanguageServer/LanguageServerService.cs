using System.Collections.Concurrent;
using System.Diagnostics;
using HPDOS.Harneses.Middleware.Generated;

namespace HPDOS.Harneses.Middleware;

public sealed class LanguageServerService : ILanguageServerService
{
    private readonly LanguageServerOptions _options;
    private readonly IReadOnlyList<ILanguageServerRegistryProvider> _registryProviders;
    private readonly ILanguageServerToolResolver _toolResolver;
    private readonly ConcurrentDictionary<string, ClientSession> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<ClientSession?>> _startupTasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LanguageServerUnavailableServer> _unavailable = new(StringComparer.Ordinal);

    public LanguageServerService()
        : this(new LanguageServerOptions())
    {
    }

    public LanguageServerService(
        LanguageServerOptions options,
        IEnumerable<ILanguageServerRegistryProvider>? registryProviders = null,
        ILanguageServerToolResolver? toolResolver = null)
    {
        _options = options;
        _registryProviders = registryProviders?.Concat([new GeneratedLanguageServerRegistryProvider()]).ToArray()
            ?? [new GeneratedLanguageServerRegistryProvider()];
        _toolResolver = toolResolver ?? new LanguageServerToolResolver();
    }

    public ValueTask<IReadOnlyList<LanguageServerStatus>> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var statuses = new List<LanguageServerStatus>();
        foreach (var client in _clients.Values)
        {
            statuses.Add(new LanguageServerStatus
            {
                ServerId = client.Definition.Id,
                Root = client.Root,
                Status = client.Client.IsRunning ? LanguageServerStatusKind.Running : LanguageServerStatusKind.Stopped
            });
        }

        foreach (var unavailable in _unavailable.Values)
        {
            statuses.Add(new LanguageServerStatus
            {
                ServerId = unavailable.ServerId,
                Root = unavailable.Root,
                Status = LanguageServerStatusKind.Unavailable,
                Message = unavailable.Reason
            });
        }

        return ValueTask.FromResult<IReadOnlyList<LanguageServerStatus>>(statuses);
    }

    public async ValueTask<bool> HasServerForFileAsync(
        string path,
        CancellationToken cancellationToken = default)
        => (await ResolveDocumentAsync(path, cancellationToken).ConfigureAwait(false)).HasServers;

    public async ValueTask<LanguageServerDocumentResolution> ResolveDocumentAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var candidates = await ResolveCandidatesAsync(path, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            var normalizedPath = Path.GetFullPath(path, Directory.GetCurrentDirectory());
            return new LanguageServerDocumentResolution
            {
                Path = normalizedPath,
                Uri = new Uri(normalizedPath).AbsoluteUri
            };
        }

        return new LanguageServerDocumentResolution
        {
            Path = candidates[0].Path,
            Uri = candidates[0].Uri,
            Servers = candidates.Select(candidate => new LanguageServerResolvedServer
            {
                ServerId = candidate.Definition.Id,
                Root = candidate.Root,
                LanguageId = candidate.LanguageId,
                ConfigVersion = _options.ConfigVersion
            }).ToArray()
        };
    }

    public async ValueTask<LanguageServerOpenResult> OpenDocumentAsync(
        LanguageServerDocumentOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessions = await EnsureClientsForDocumentAsync(request.Path, cancellationToken).ConfigureAwait(false);
        var opened = false;
        var diagnostics = new List<LanguageServerDiagnosticSet>();
        var positionEncoding = request.PositionEncoding;

        foreach (var session in sessions)
        {
            positionEncoding = session.Client.Capabilities.PositionEncoding;
            if (session.OpenDocuments.TryGetValue(request.Uri, out var existing) &&
                existing.LanguageId == request.LanguageId)
            {
                diagnostics.AddRange(session.Client.CurrentDiagnostics.Where(set => set.Path == request.Path));
                opened = true;
                continue;
            }

            if (session.OpenDocuments.TryRemove(request.Uri, out existing))
            {
                await session.Client.DidCloseAsync(
                    new LanguageServerDocumentCloseRequest { Path = request.Path, Uri = request.Uri },
                    cancellationToken).ConfigureAwait(false);
            }

            var document = new OpenDocumentState(request.LanguageId, request.Version, request.Text);
            await session.Client.DidOpenAsync(
                request with { PositionEncoding = positionEncoding },
                cancellationToken).ConfigureAwait(false);
            session.OpenDocuments[request.Uri] = document;
            opened = true;
            diagnostics.AddRange(session.Client.CurrentDiagnostics.Where(set => set.Path == request.Path));
        }

        return new LanguageServerOpenResult
        {
            Path = request.Path,
            Uri = request.Uri,
            LanguageId = request.LanguageId,
            Version = request.Version,
            Opened = opened,
            PositionEncoding = positionEncoding,
            Diagnostics = diagnostics
        };
    }

    public async ValueTask<LanguageServerChangeResult> ChangeDocumentAsync(
        LanguageServerDocumentChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessions = await EnsureClientsForDocumentAsync(request.Path, cancellationToken).ConfigureAwait(false);
        var diagnostics = new List<LanguageServerDiagnosticSet>();

        foreach (var session in sessions)
        {
            if (!session.OpenDocuments.TryGetValue(request.Uri, out var existing))
                continue;

            await session.Client.DidChangeAsync(request, existing.Text, cancellationToken).ConfigureAwait(false);
            session.OpenDocuments[request.Uri] = existing with
            {
                Version = request.Version,
                Text = request.Text
            };
            diagnostics.AddRange(session.Client.CurrentDiagnostics.Where(set => set.Path == request.Path));
        }

        return new LanguageServerChangeResult
        {
            Path = request.Path,
            Version = request.Version,
            Diagnostics = diagnostics
        };
    }

    public async ValueTask SaveDocumentAsync(
        LanguageServerDocumentSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessions = await EnsureClientsForDocumentAsync(request.Path, cancellationToken).ConfigureAwait(false);
        foreach (var session in sessions)
        {
            if (session.OpenDocuments.ContainsKey(request.Uri))
                await session.Client.DidSaveAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask CloseDocumentAsync(
        LanguageServerDocumentCloseRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessions = await EnsureClientsForDocumentAsync(request.Path, cancellationToken).ConfigureAwait(false);
        foreach (var session in sessions)
        {
            if (session.OpenDocuments.TryRemove(request.Uri, out _))
                await session.Client.DidCloseAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask NotifyWatchedFileChangedAsync(
        LanguageServerWatchedFileChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessions = await EnsureClientsForDocumentAsync(request.Path, cancellationToken).ConfigureAwait(false);
        foreach (var session in sessions)
            await session.Client.DidChangeWatchedFilesAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<LanguageServerDiagnosticSet>> GetDiagnosticsAsync(
        LanguageServerDiagnosticRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessions = await EnsureClientsForDocumentAsync(request.Path, cancellationToken).ConfigureAwait(false);
        var diagnostics = new List<LanguageServerDiagnosticSet>();
        foreach (var session in sessions)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var pulled = await session.Client.PullDiagnosticsAsync(request, cancellationToken).ConfigureAwait(false);
                diagnostics.AddRange(pulled.Where(set => set.Path == request.Path));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                diagnostics.Add(new LanguageServerDiagnosticSet
                {
                    Path = request.Path,
                    ServerId = session.Definition.Id,
                    Source = LanguageServerDiagnosticSource.DocumentPull,
                    ReceivedAt = DateTimeOffset.UtcNow,
                    Partial = true
                });
            }

            stopwatch.Stop();
        }

        return diagnostics;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
            await client.Client.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<ClientSession>> EnsureClientsForDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var candidates = await ResolveCandidatesAsync(path, cancellationToken).ConfigureAwait(false);
        var sessions = new List<ClientSession>();
        foreach (var candidate in candidates)
        {
            var session = await EnsureClientAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (session is not null)
                sessions.Add(session);
        }

        return sessions;
    }

    private async Task<ClientSession?> EnsureClientAsync(
        ResolvedLanguageServerCandidate candidate,
        CancellationToken cancellationToken)
    {
        var key = CreateClientKey(candidate.Definition.Id, candidate.Root);
        if (_clients.TryGetValue(key, out var existing))
            return existing;

        var unavailableKey = CreateUnavailableKey(candidate.Definition.Id, candidate.Root);
        if (_unavailable.ContainsKey(unavailableKey))
            return null;

        var startup = _startupTasks.GetOrAdd(key, _ => StartClientAsync(candidate, key, unavailableKey, cancellationToken));
        try
        {
            return await startup.ConfigureAwait(false);
        }
        finally
        {
            _startupTasks.TryRemove(key, out _);
        }
    }

    private async Task<ClientSession?> StartClientAsync(
        ResolvedLanguageServerCandidate candidate,
        string key,
        string unavailableKey,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var launch = await candidate.Definition.Provider.ResolveLaunchAsync(
                new LanguageServerLaunchContext
                {
                    Root = candidate.Root,
                    WorkspaceRoot = candidate.WorkspaceRoot,
                    Definition = candidate.Definition,
                    Options = _options,
                    ToolResolver = _toolResolver
                },
                cancellationToken).ConfigureAwait(false);

            if (launch is null)
            {
                RecordUnavailable(candidate, unavailableKey, "Launcher did not return a launch descriptor.");
                return null;
            }

            var initialization = await candidate.Definition.Provider.CreateInitializationAsync(
                new LanguageServerInitializationContext
                {
                    Root = candidate.Root,
                    WorkspaceRoot = candidate.WorkspaceRoot,
                    Definition = candidate.Definition,
                    Options = _options,
                    ToolResolver = _toolResolver
                },
                cancellationToken).ConfigureAwait(false);

            if (initialization.InitializationOptions.Count > 0)
                launch = launch with { InitializationOptions = MergeInitializationOptions(launch, initialization) };

            var client = new LanguageServerProtocolClient(
                candidate.Definition.Id,
                candidate.Root,
                launch,
                _options,
                initialization);
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
            var session = new ClientSession(candidate.Definition, candidate.Root, client);
            _clients[key] = session;
            stopwatch.Stop();
            return session;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or OperationCanceledException)
        {
            RecordUnavailable(candidate, unavailableKey, ex.Message);
            return null;
        }
    }

    private void RecordUnavailable(ResolvedLanguageServerCandidate candidate, string unavailableKey, string reason)
    {
        _unavailable[unavailableKey] = new LanguageServerUnavailableServer
        {
            ServerId = candidate.Definition.Id,
            Root = candidate.Root,
            ConfigVersion = _options.ConfigVersion,
            Reason = reason,
            LastAttemptedAt = DateTimeOffset.UtcNow
        };
    }

    private async ValueTask<IReadOnlyList<ResolvedLanguageServerCandidate>> ResolveCandidatesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var normalizedPath = Path.GetFullPath(path, Directory.GetCurrentDirectory());
        var uri = new Uri(normalizedPath).AbsoluteUri;
        if (!_options.Enabled)
            return [];

        var extension = Path.GetExtension(normalizedPath);
        if (string.IsNullOrEmpty(extension))
            return [];

        var workspaceRoot = ResolveWorkspaceRoot(normalizedPath);
        if (!IsInsideWorkspace(normalizedPath, workspaceRoot))
            return [];

        var resolved = new List<ResolvedLanguageServerCandidate>();
        var resolvedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in EnumerateDefinitions())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsEnabled(definition) || !MatchesExtension(definition, extension))
                continue;

            var root = await definition.Provider.ResolveRootAsync(
                new LanguageServerRootContext
                {
                    Path = normalizedPath,
                    WorkspaceRoot = workspaceRoot,
                    Definition = definition,
                    Options = _options
                },
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(root) || !IsInsideWorkspace(root, workspaceRoot))
                continue;

            var normalizedRoot = Path.GetFullPath(root, Directory.GetCurrentDirectory());
            var key = CreateClientKey(definition.Id, normalizedRoot);
            if (!resolvedKeys.Add(key))
                continue;

            resolved.Add(new ResolvedLanguageServerCandidate(
                normalizedPath,
                uri,
                definition,
                workspaceRoot,
                normalizedRoot,
                GetLanguageId(definition, extension)));
        }

        return resolved;
    }

    private IEnumerable<LanguageServerDefinition> EnumerateDefinitions()
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in _options.Servers)
        {
            if (seenIds.Add(server.Id))
                yield return server;
        }

        foreach (var provider in _registryProviders)
        {
            foreach (var server in provider.GetAll())
            {
                if (seenIds.Add(server.Id))
                    yield return server;
            }
        }

        if (!_options.AllowWellKnownLocalServers)
            yield break;

        foreach (var server in WellKnownLanguageServerRegistryProvider.Instance.GetAll())
        {
            if (seenIds.Add(server.Id))
                yield return server;
        }
    }

    private bool IsEnabled(LanguageServerDefinition definition)
        => definition.EnabledByDefault &&
           (!definition.Experimental || _options.EnabledExperimentalServers.Contains(definition.Id));

    private static bool MatchesExtension(LanguageServerDefinition definition, string extension)
        => definition.Extensions.Any(candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));

    private static string GetLanguageId(LanguageServerDefinition definition, string extension)
    {
        foreach (var pair in definition.LanguageIds)
        {
            if (string.Equals(pair.Key, extension, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return extension.TrimStart('.').ToLowerInvariant();
    }

    private string ResolveWorkspaceRoot(string normalizedPath)
    {
        foreach (var workspaceFolder in _options.WorkspaceFolders)
        {
            if (string.IsNullOrWhiteSpace(workspaceFolder))
                continue;

            var normalizedWorkspace = Path.GetFullPath(workspaceFolder, Directory.GetCurrentDirectory());
            if (IsInsideWorkspace(normalizedPath, normalizedWorkspace))
                return normalizedWorkspace;
        }

        return Directory.GetCurrentDirectory();
    }

    private static bool IsInsideWorkspace(string path, string workspaceRoot)
    {
        var relative = Path.GetRelativePath(workspaceRoot, path);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    private static string CreateClientKey(string serverId, string root)
        => $"{serverId}\u001f{root}";

    private string CreateUnavailableKey(string serverId, string root)
        => $"{serverId}\u001f{root}\u001f{_options.ConfigVersion}";

    private static IReadOnlyDictionary<string, object?> MergeInitializationOptions(
        LanguageServerLaunchDescriptor launch,
        LanguageServerInitialization initialization)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in launch.InitializationOptions)
            merged[pair.Key] = pair.Value;

        foreach (var pair in initialization.InitializationOptions)
            merged[pair.Key] = pair.Value;

        return merged;
    }

    private sealed record ResolvedLanguageServerCandidate(
        string Path,
        string Uri,
        LanguageServerDefinition Definition,
        string WorkspaceRoot,
        string Root,
        string LanguageId);

    private sealed record OpenDocumentState(string LanguageId, int Version, string Text);

    private sealed record ClientSession(
        LanguageServerDefinition Definition,
        string Root,
        LanguageServerProtocolClient Client)
    {
        public ConcurrentDictionary<string, OpenDocumentState> OpenDocuments { get; } = new(StringComparer.Ordinal);
    }
}
