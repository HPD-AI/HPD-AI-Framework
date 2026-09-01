using System.Collections.Immutable;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Threading.Channels;

namespace HPD.Agent.MCP;

/// <summary>Creates an independently owned MCP capability source from a final manifest.</summary>
public sealed class McpCapabilitySourceFactory : IAgentCapabilitySourceFactory
{
    private readonly string? _manifestPath;
    private readonly string? _manifestContent;
    private readonly McpOptions _options;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ISecretResolver? _secretResolver;
    private readonly McpServerConfig? _serverConfig;
    private readonly string? _selectedServerName;
    private readonly string? _parentToolHarness;
    private readonly bool _collapseWithinToolHarness;
    private readonly bool? _requiresPermissionOverride;
    private readonly McpCatalogPageCache _catalogPages;

    internal McpCapabilitySourceFactory(
        CapabilitySourceId id,
        string? manifestPath,
        string? manifestContent,
        McpOptions options,
        ILoggerFactory? loggerFactory,
        ISecretResolver? secretResolver,
        McpServerConfig? serverConfig = null,
        string? selectedServerName = null,
        string? parentToolHarness = null,
        bool collapseWithinToolHarness = false,
        bool? requiresPermissionOverride = null)
    {
        Id = id;
        _manifestPath = manifestPath;
        _manifestContent = manifestContent;
        _options = options;
        _loggerFactory = loggerFactory;
        _secretResolver = secretResolver;
        _serverConfig = serverConfig;
        _selectedServerName = selectedServerName;
        _parentToolHarness = parentToolHarness;
        _collapseWithinToolHarness = collapseWithinToolHarness;
        _requiresPermissionOverride = requiresPermissionOverride;
        _catalogPages = new McpCatalogPageCache(options.Catalog);
    }

    /// <summary>Creates a source factory for one generated inline server registration.</summary>
    public static McpCapabilitySourceFactory FromServer(
        string sourceId,
        McpServerConfig server,
        string parentToolHarness,
        bool collapseWithinToolHarness,
        bool? requiresPermissionOverride = null,
        McpOptions? options = null) =>
        new(
            CapabilitySourceId.Create(sourceId),
            null,
            null,
            options ?? new McpOptions(),
            null,
            null,
            PrepareServer(server, parentToolHarness, collapseWithinToolHarness, requiresPermissionOverride));

    /// <summary>Creates a source factory selecting one server from a final manifest file.</summary>
    public static McpCapabilitySourceFactory FromManifestServer(
        string sourceId,
        string manifestPath,
        string serverName,
        string parentToolHarness,
        bool collapseWithinToolHarness,
        bool? requiresPermissionOverride = null,
        McpOptions? options = null) =>
        new(
            CapabilitySourceId.Create(sourceId),
            Path.GetFullPath(manifestPath),
            null,
            options ?? new McpOptions(),
            null,
            null,
            selectedServerName: serverName,
            parentToolHarness: parentToolHarness,
            collapseWithinToolHarness: collapseWithinToolHarness,
            requiresPermissionOverride: requiresPermissionOverride);

    private static McpServerConfig PrepareServer(
        McpServerConfig server,
        string parentToolHarness,
        bool collapseWithinToolHarness,
        bool? requiresPermissionOverride)
    {
        ArgumentNullException.ThrowIfNull(server);
        server.ParentToolHarness = parentToolHarness;
        server.CollapseWithinToolHarness = collapseWithinToolHarness;
        if (requiresPermissionOverride is { } requiresPermission)
            server.RequiresPermission = requiresPermission;
        return server;
    }

    /// <inheritdoc />
    public CapabilitySourceId Id { get; }

    /// <inheritdoc />
    public ValueTask<IAgentCapabilitySource> CreateAsync(
        IServiceProvider? services,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IAgentCapabilitySource>(new McpCapabilitySource(
            Id,
            _manifestPath,
            _manifestContent,
            _options,
            _loggerFactory?.CreateLogger<McpRuntime>() ??
                services?.GetService<ILoggerFactory>()?.CreateLogger<McpRuntime>() ??
                NullLogger<McpRuntime>.Instance,
            _secretResolver ?? services?.GetService<ISecretResolver>(),
            _serverConfig,
            _selectedServerName,
            _parentToolHarness,
            _collapseWithinToolHarness,
            _requiresPermissionOverride,
            _catalogPages));
}

/// <summary>Loads MCP functions into immutable, lease-owned source revisions.</summary>
internal sealed class McpCapabilitySource : IAgentCapabilitySource
{
    private readonly Channel<CapabilityInvalidation> _invalidations =
        Channel.CreateUnbounded<CapabilityInvalidation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly string? _manifestPath;
    private readonly string? _manifestContent;
    private readonly McpOptions _options;
    private readonly ILogger _logger;
    private readonly ISecretResolver? _secretResolver;
    private readonly McpServerConfig? _serverConfig;
    private readonly string? _selectedServerName;
    private readonly string? _parentToolHarness;
    private readonly bool _collapseWithinToolHarness;
    private readonly bool? _requiresPermissionOverride;
    private readonly McpCatalogPageCache _catalogPages;
    private long _revision = -1;
    private int _disposed;

    internal McpCapabilitySource(
        CapabilitySourceId id,
        string? manifestPath,
        string? manifestContent,
        McpOptions options,
        ILogger logger,
        ISecretResolver? secretResolver,
        McpServerConfig? serverConfig,
        string? selectedServerName,
        string? parentToolHarness,
        bool collapseWithinToolHarness,
        bool? requiresPermissionOverride,
        McpCatalogPageCache catalogPages)
    {
        Id = id;
        _manifestPath = manifestPath;
        _manifestContent = manifestContent;
        _options = options;
        _logger = logger;
        _secretResolver = secretResolver;
        _serverConfig = serverConfig;
        _selectedServerName = selectedServerName;
        _parentToolHarness = parentToolHarness;
        _collapseWithinToolHarness = collapseWithinToolHarness;
        _requiresPermissionOverride = requiresPermissionOverride;
        _catalogPages = catalogPages;
    }

    /// <inheritdoc />
    public CapabilitySourceId Id { get; }

    /// <inheritdoc />
    public async ValueTask<CapabilitySourceLoadResult> LoadAsync(
        CapabilityLoadContext context,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var runtime = new McpRuntime(_logger, _options, _catalogPages);
        try
        {
            List<AIFunction> functions;
            if (_serverConfig is not null)
            {
                functions = await runtime.LoadToolsForToolHarnessAsync(
                    _serverConfig,
                    secretResolver: _secretResolver,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else if (_selectedServerName is not null)
            {
                var json = await File.ReadAllTextAsync(_manifestPath!, cancellationToken).ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize(json, McpJsonSerializerContext.Default.McpManifest)
                    ?? throw new InvalidOperationException("Failed to parse MCP manifest.");
                manifest.Validate();
                var server = manifest.Servers.SingleOrDefault(candidate =>
                    string.Equals(candidate.Name, _selectedServerName, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException(
                        $"MCP manifest does not contain server '{_selectedServerName}'.");
                server.ParentToolHarness = _parentToolHarness;
                server.CollapseWithinToolHarness = _collapseWithinToolHarness;
                if (_requiresPermissionOverride is { } requiresPermission)
                    server.RequiresPermission = requiresPermission;
                functions = await runtime.LoadToolsForToolHarnessAsync(
                    server,
                    secretResolver: _secretResolver,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                functions = _manifestContent is not null
                    ? await runtime.LoadToolsFromManifestContentAsync(
                        _manifestContent, secretResolver: _secretResolver, cancellationToken: cancellationToken)
                        .ConfigureAwait(false)
                    : await runtime.LoadToolsFromManifestAsync(
                        _manifestPath!, secretResolver: _secretResolver, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
            }
            await runtime.StartSubscriptionsAsync(
                reason => _invalidations.Writer.TryWrite(new CapabilityInvalidation(Id, reason)),
                cancellationToken).ConfigureAwait(false);
            var revision = CapabilitySourceRevision.Create(Interlocked.Increment(ref _revision));
            return new(new McpCapabilityRevisionOwner(
                Id, revision, functions, runtime,
                _options.Invocation.RecoveryReferenceProtector,
                _options.Invocation.RemoteTaskAdapter));
        }
        catch
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CapabilityInvalidation> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var invalidation in _invalidations.Reader.ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return invalidation;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _invalidations.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Owns the MCP connections used by one immutable capability revision.</summary>
internal sealed class McpCapabilityRevisionOwner : ICapabilitySourceRevisionOwner, IAgentOperationRecoveryProvider
{
    private McpRuntime? _runtime;
    private readonly IMcpRecoveryReferenceProtector? _recoveryProtector;
    private readonly IMcpRemoteTaskAdapter? _remoteTasks;

    internal McpCapabilityRevisionOwner(
        CapabilitySourceId sourceId,
        CapabilitySourceRevision revision,
        IEnumerable<AIFunction> functions,
        McpRuntime runtime,
        IMcpRecoveryReferenceProtector? recoveryProtector,
        IMcpRemoteTaskAdapter? remoteTasks)
    {
        SourceId = sourceId;
        Revision = revision;
        _runtime = runtime;
        _recoveryProtector = recoveryProtector;
        _remoteTasks = remoteTasks;
        var materialized = functions.OrderBy(static function => function.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        Snapshot = new CapabilitySourceSnapshot
        {
            Functions = materialized,
            Metadata = runtime.GetSourceMetadata(),
            Descriptors = materialized.ToImmutableDictionary(
                static function => GetMetadata(function).Id,
                function =>
                {
                    var metadata = GetMetadata(function);
                    return new CapabilityDescriptor
                    {
                        Id = metadata.Id,
                        SourceId = sourceId,
                        SourceRevision = revision,
                        ModelName = function.Name,
                        Kind = metadata.Kind,
                        Metadata = function.AdditionalProperties?
                            .Where(static pair => pair.Key.StartsWith("mcp.", StringComparison.Ordinal) &&
                                pair.Value is string)
                            .ToImmutableDictionary(
                                static pair => pair.Key,
                                static pair => (string)pair.Value!,
                                StringComparer.Ordinal)
                            ?? ImmutableDictionary<string, string>.Empty
                    };
                })
        };
    }

    /// <inheritdoc />
    public CapabilitySourceId SourceId { get; }
    /// <inheritdoc />
    public CapabilitySourceRevision Revision { get; }
    /// <inheritdoc />
    public CapabilitySourceSnapshot Snapshot { get; }

    public bool CanRecover(AgentOperationRecoveryReference recoveryReference) =>
        _recoveryProtector is not null && _remoteTasks?.CanRecover(recoveryReference) == true;

    public async ValueTask<bool> TryRecoverAsync(
        AgentOperation operation,
        AgentCapabilityLease revisionLease,
        CancellationToken cancellationToken)
    {
        var recovery = operation.Snapshot.Recovery;
        var runtime = _runtime;
        if (recovery is null || runtime is null || _recoveryProtector is null || _remoteTasks is null)
            return false;
        return await _remoteTasks.TryRecoverAsync(
            operation, runtime, _recoveryProtector, revisionLease, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _runtime, null)?.DisposeAsync() ?? ValueTask.CompletedTask;

    private static HPDCapabilityMetadata GetMetadata(AIFunction function)
    {
        if (function.AdditionalProperties?.TryGetValue(
                HPDCapabilityMetadata.AdditionalPropertiesKey,
                out var value) != true || value is not HPDCapabilityMetadata metadata)
            throw new InvalidOperationException($"MCP function '{function.Name}' has no typed capability metadata.");
        return metadata;
    }
}
