using System.Collections.Immutable;
using System.ComponentModel;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Threading.Channels;

namespace HPD.Agent.MCP;

/// <summary>
/// Source-generated integration surface for creating an MCP capability source.
/// Application code should use <see cref="AgentBuilderMcpExtensions.WithMcp"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
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
    private readonly CapabilityId? _parentCapabilityId;
    private readonly bool _collapseWithinToolHarness;
    private readonly McpPermissionConfig? _permissionOverride;
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
        McpPermissionConfig? permissionOverride = null,
        CapabilityId? parentCapabilityId = null)
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
        _parentCapabilityId = parentCapabilityId;
        _collapseWithinToolHarness = collapseWithinToolHarness;
        _permissionOverride = SnapshotPermission(permissionOverride);
        _catalogPages = new McpCatalogPageCache(options.Catalog);
    }

    /// <summary>Creates a source factory for one generated inline server registration.</summary>
    public static McpCapabilitySourceFactory FromServer(
        string sourceId,
        McpServerConfig server,
        string parentToolHarness,
        bool collapseWithinToolHarness,
        McpPermissionConfig? permissionOverride = null,
        McpOptions? options = null) =>
        FromServer(sourceId, server, parentToolHarness, collapseWithinToolHarness, permissionOverride, options, null);

    /// <summary>Creates a generated MCP source with an exact parent activation capability.</summary>
    public static McpCapabilitySourceFactory FromServer(
        string sourceId,
        McpServerConfig server,
        string parentToolHarness,
        bool collapseWithinToolHarness,
        McpPermissionConfig? permissionOverride,
        McpOptions? options,
        string? parentCapabilityId) =>
        new(
            CapabilitySourceId.Create(sourceId),
            null,
            null,
            options ?? new McpOptions(),
            null,
            null,
            PrepareServer(server, parentToolHarness, collapseWithinToolHarness, permissionOverride,
                parentCapabilityId is null ? null : CapabilityId.Create(parentCapabilityId)));

    /// <summary>Creates a source factory selecting one server from a final manifest file.</summary>
    public static McpCapabilitySourceFactory FromManifestServer(
        string sourceId,
        string manifestPath,
        string serverName,
        string parentToolHarness,
        bool collapseWithinToolHarness,
        McpPermissionConfig? permissionOverride = null,
        McpOptions? options = null) =>
        FromManifestServer(sourceId, manifestPath, serverName, parentToolHarness, collapseWithinToolHarness, permissionOverride, options, null);

    /// <summary>Creates a generated MCP source with an exact parent activation capability.</summary>
    public static McpCapabilitySourceFactory FromManifestServer(
        string sourceId,
        string manifestPath,
        string serverName,
        string parentToolHarness,
        bool collapseWithinToolHarness,
        McpPermissionConfig? permissionOverride,
        McpOptions? options,
        string? parentCapabilityId) =>
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
            permissionOverride: permissionOverride,
            parentCapabilityId: parentCapabilityId is null ? null : CapabilityId.Create(parentCapabilityId));

    /// <summary>Creates a source factory from inline MCP server declarations.</summary>
    internal static McpCapabilitySourceFactory FromServers(
        string sourceId,
        IEnumerable<McpServerConfig> servers,
        McpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(servers);
        var manifest = new McpManifest { Servers = servers.Select(static server => server).ToList() };
        manifest.Validate();
        var content = JsonSerializer.Serialize(manifest, McpJsonSerializerContext.Default.McpManifest);
        return new(
            CapabilitySourceId.Create(sourceId),
            null,
            content,
            options ?? new McpOptions(),
            null,
            null);
    }

    private static McpServerConfig PrepareServer(
        McpServerConfig server,
        string parentToolHarness,
        bool collapseWithinToolHarness,
        McpPermissionConfig? permissionOverride,
        CapabilityId? parentCapabilityId)
    {
        ArgumentNullException.ThrowIfNull(server);
        var json = JsonSerializer.Serialize(server, McpJsonSerializerContext.Default.McpServerConfig);
        var snapshot = JsonSerializer.Deserialize(
                json,
                McpJsonSerializerContext.Default.McpServerConfig)
            ?? throw new InvalidOperationException($"Failed to snapshot MCP server '{server.Name}'.");
        snapshot.ParentToolHarness = parentToolHarness;
        snapshot.ParentCapabilityId = parentCapabilityId;
        snapshot.CollapseWithinToolHarness = collapseWithinToolHarness;
        if (permissionOverride is not null)
            snapshot.Permission = MergePermission(
                snapshot.Permission,
                SnapshotPermission(permissionOverride)!);
        snapshot.Validate();
        return snapshot;
    }

    private static McpPermissionConfig? SnapshotPermission(McpPermissionConfig? permission)
    {
        if (permission is null)
            return null;
        var json = JsonSerializer.Serialize(permission, McpJsonSerializerContext.Default.McpPermissionConfig);
        return JsonSerializer.Deserialize(json, McpJsonSerializerContext.Default.McpPermissionConfig);
    }

    private static McpPermissionConfig MergePermission(
        McpPermissionConfig? serverPermission,
        McpPermissionConfig attributePermission) => new()
    {
        RequiresPermission = attributePermission.RequiresPermission,
        Authority = attributePermission.Authority ?? serverPermission?.Authority,
        Policy = attributePermission.Policy ?? serverPermission?.Policy,
        Tools = serverPermission?.Tools ??
            attributePermission.Tools
    };

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
            _permissionOverride,
            _catalogPages,
            _parentCapabilityId));
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
    private readonly CapabilityId? _parentCapabilityId;
    private readonly bool _collapseWithinToolHarness;
    private readonly McpPermissionConfig? _permissionOverride;
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
        McpPermissionConfig? permissionOverride,
        McpCatalogPageCache catalogPages,
        CapabilityId? parentCapabilityId = null)
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
        _parentCapabilityId = parentCapabilityId;
        _collapseWithinToolHarness = collapseWithinToolHarness;
        _permissionOverride = permissionOverride;
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
                server.ParentCapabilityId = _parentCapabilityId;
                server.CollapseWithinToolHarness = _collapseWithinToolHarness;
                if (_permissionOverride is not null)
                    server.Permission = MergePermission(server.Permission, _permissionOverride);
                server.Validate();
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

    private static McpPermissionConfig MergePermission(
        McpPermissionConfig? serverPermission,
        McpPermissionConfig overridePermission) => new()
    {
        RequiresPermission = overridePermission.RequiresPermission,
        Authority = overridePermission.Authority ?? serverPermission?.Authority,
        Policy = overridePermission.Policy ?? serverPermission?.Policy,
        Tools = serverPermission?.Tools ?? overridePermission.Tools
    };

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
