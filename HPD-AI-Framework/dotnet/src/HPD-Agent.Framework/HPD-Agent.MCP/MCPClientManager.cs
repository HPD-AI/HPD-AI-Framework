using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.Text.Json;


namespace HPD.Agent.MCP;

/// <summary>
/// Manages lifecycle of MCP clients and tool loading
/// </summary>
public class MCPClientManager : IDisposable
{
    private readonly Dictionary<string, McpClient> _clients = new();
    private readonly ILogger _logger;
    private readonly MCPOptions _options;
    private bool _disposed = false;

    public MCPClientManager(ILogger logger, MCPOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new MCPOptions();
    }

    /// <summary>
    /// Loads MCP tools from the specified manifest file
    /// </summary>
    /// <param name="manifestPath">Path to the MCP manifest file</param>
    /// <param name="enableCollapsing">Enable Toolkit Collapsing (groups tools by server behind containers)</param>
    /// <param name="maxFunctionNamesInDescription">Max function names to show in container descriptions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<List<AIFunction>> LoadToolsFromManifestAsync(
        string manifestPath,
        bool enableCollapsing = false,
        int maxFunctionNamesInDescription = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading MCP tools from manifest: {ManifestPath} (Collapsing: {Collapsing})",
            manifestPath, enableCollapsing);

        var manifest = await LoadManifestAsync(manifestPath, cancellationToken);
        var allTools = new List<AIFunction>();

        var enabledServers = manifest.Servers.Where(s => s.Enabled).ToList();
        _logger.LogInformation("Found {Count} enabled servers in manifest", enabledServers.Count);

        foreach (var serverConfig in enabledServers)
        {
            try
            {
                var tools = await LoadServerToolsAsync(serverConfig, cancellationToken);

                // Determine Collapsing for this specific server
                // Per-server setting takes precedence over global setting
                var enableCollapsingForThisServer = serverConfig.EnableCollapsing ?? enableCollapsing;

                if (enableCollapsingForThisServer && tools.Count > 0)
                {
                    // Wrap tools with container for this server
                    var (container, CollapsedTools) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
                        serverConfig.Name,
                        tools,
                        maxFunctionNamesInDescription,
                        FunctionResult: serverConfig.FunctionResult,
                        SystemPrompt: serverConfig.SystemPrompt,
                        customDescription: serverConfig.Description);

                    allTools.Add(container);
                    allTools.AddRange(CollapsedTools);

                    _logger.LogInformation("Loaded {Count} tools from server '{ServerName}' (Collapsed with container '{ContainerName}')",
                        tools.Count, serverConfig.Name, container.Name);
                }
                else
                {
                    // Original behavior - no Collapsing
                    allTools.AddRange(tools);
                    _logger.LogInformation("Loaded {Count} tools from server '{ServerName}'",
                        tools.Count, serverConfig.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load tools from server '{ServerName}': {Error}",
                    serverConfig.Name, ex.Message);

                if (_options.FailOnServerError)
                {
                    throw new InvalidOperationException($"Failed to load server '{serverConfig.Name}'", ex);
                }
                // Continue with other servers if FailOnServerError is false
            }
        }

        _logger.LogInformation("Successfully loaded {TotalCount} MCP tools from {ServerCount} servers",
            allTools.Count, _clients.Count);

        return allTools;
    }

    /// <summary>
    /// Loads MCP tools from manifest content
    /// </summary>
    /// <param name="manifestContent">JSON content of the MCP manifest</param>
    /// <param name="enableCollapsing">Enable Toolkit Collapsing (groups tools by server behind containers)</param>
    /// <param name="maxFunctionNamesInDescription">Max function names to show in container descriptions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<List<AIFunction>> LoadToolsFromManifestContentAsync(
        string manifestContent,
        bool enableCollapsing = false,
        int maxFunctionNamesInDescription = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading MCP tools from manifest content (Collapsing: {Collapsing})", enableCollapsing);

        var manifest = ParseManifest(manifestContent);
        var allTools = new List<AIFunction>();

        var enabledServers = manifest.Servers.Where(s => s.Enabled).ToList();
        _logger.LogInformation("Found {Count} enabled servers in manifest", enabledServers.Count);

        foreach (var serverConfig in enabledServers)
        {
            try
            {
                var tools = await LoadServerToolsAsync(serverConfig, cancellationToken);

                // Determine Collapsing for this specific server
                // Per-server setting takes precedence over global setting
                var enableCollapsingForThisServer = serverConfig.EnableCollapsing ?? enableCollapsing;

                if (enableCollapsingForThisServer && tools.Count > 0)
                {
                    // Wrap tools with container for this server
                    var (container, CollapsedTools) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
                        serverConfig.Name,
                        tools,
                        maxFunctionNamesInDescription,
                        FunctionResult: serverConfig.FunctionResult,
                        SystemPrompt: serverConfig.SystemPrompt,
                        customDescription: serverConfig.Description);

                    allTools.Add(container);
                    allTools.AddRange(CollapsedTools);

                    _logger.LogInformation("Loaded {Count} tools from server '{ServerName}' (Collapsed with container '{ContainerName}')",
                        tools.Count, serverConfig.Name, container.Name);
                }
                else
                {
                    // Original behavior - no Collapsing
                    allTools.AddRange(tools);
                    _logger.LogInformation("Loaded {Count} tools from server '{ServerName}'",
                        tools.Count, serverConfig.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load tools from server '{ServerName}': {Error}",
                    serverConfig.Name, ex.Message);

                if (_options.FailOnServerError)
                {
                    throw new InvalidOperationException($"Failed to load server '{serverConfig.Name}'", ex);
                }
                // Continue with other servers if FailOnServerError is false
            }
        }

        _logger.LogInformation("Successfully loaded {TotalCount} MCP tools from {ServerCount} servers",
            allTools.Count, _clients.Count);

        return allTools;
    }

    /// <summary>
    /// Loads and validates manifest from file
    /// </summary>
    private static async Task<MCPManifest> LoadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(manifestPath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException($"MCP manifest file not found: {manifestPath}");
            }

            using var stream = fileInfo.OpenRead();
            using var reader = new StreamReader(stream);
            var manifestJson = await reader.ReadToEndAsync(cancellationToken);
            
            return ParseManifest(manifestJson);
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load manifest from {manifestPath}", ex);
        }
    }

    /// <summary>
    /// Parses manifest from JSON content
    /// </summary>
    private static MCPManifest ParseManifest(string manifestContent)
    {
        var manifest = JsonSerializer.Deserialize(manifestContent, MCPJsonSerializerContext.Default.MCPManifest);

        if (manifest == null)
        {
            throw new InvalidOperationException("Failed to parse MCP manifest");
        }

        // Validate all server configurations
        foreach (var server in manifest.Servers)
        {
            server.Validate();
        }

        return manifest;
    }

    /// <summary>
    /// Loads tools from a specific MCP server
    /// </summary>
    private async Task<List<AIFunction>> LoadServerToolsAsync(MCPServerConfig serverConfig, CancellationToken cancellationToken)
    {
        var client = await GetOrCreateClientAsync(serverConfig, cancellationToken);

        // Use only the provided description from config (no reflection-based extraction for AOT compatibility)
        // If description is not provided, it will be empty

        // ListToolsAsync returns McpClientTool[], which inherit from AIFunction
        var mcpTools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        var adaptedTools = new List<AIFunction>();

        foreach (var tool in mcpTools)
        {
            try
            {
                // Ensure we have an AIFunction reference to invoke
                if (tool is not AIFunction originalAIFunction)
                {
                    _logger.LogWarning("MCP tool from server '{ServerName}' is not an AIFunction - skipping", serverConfig.Name);
                    continue;
                }

                // Invocation wrapper delegates to the original tool's InvokeAsync
                Func<AIFunctionArguments, CancellationToken, Task<object?>> invocationWrapper =
                    async (args, ct) => await originalAIFunction.InvokeAsync(args, ct).ConfigureAwait(false);

                var options = new HPDAIFunctionFactoryOptions
                {
                    Name = originalAIFunction.Name,
                    Description = originalAIFunction.Description,
                    RequiresPermission = serverConfig.RequiresPermission,
                    // MCP tools don't have validation since they're external - just pass through
                    Validator = _ => new List<ValidationError>(),
                    // Copy schema from original MCP tool for proper parameter handling
                    SchemaProvider = () => originalAIFunction.JsonSchema
                };

                // Attempt to copy schema information if the external tool exposes it
                // Note: Reflection-based schema extraction removed for Native AOT compatibility
                // Tools should provide schema through standard AIFunction properties

                // Create an adapted AIFunction via our factory so it's compatible with generated Toolkits
                var adapted = HPDAIFunctionFactory.Create(invocationWrapper, options);
                adaptedTools.Add(adapted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to adapt MCP tool from server '{ServerName}': {Error}", serverConfig.Name, ex.Message);
                // As a fallback, if possible, include the original AIFunction instance
                if (tool is AIFunction fallback)
                {
                    adaptedTools.Add(fallback);
                }
            }
        }

        return adaptedTools;
    }

    /// <summary>
    /// Gets or creates an MCP client for the specified server
    /// </summary>
    private async Task<McpClient> GetOrCreateClientAsync(MCPServerConfig serverConfig, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(serverConfig.Name, out var existingClient))
        {
            return existingClient;
        }

        _logger.LogDebug("Creating new MCP client for server '{ServerName}'", serverConfig.Name);

        var transportOptions = new StdioClientTransportOptions
        {
            Name = serverConfig.Name,
            Command = serverConfig.Command,
            Arguments = [.. serverConfig.Arguments]
        };

        var transport = new StdioClientTransport(transportOptions);
        
        // Create client with timeout handling
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(serverConfig.TimeoutMs));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        
        var client = await McpClient.CreateAsync(transport);
        _clients[serverConfig.Name] = client;

        _logger.LogDebug("Successfully created MCP client for server '{ServerName}'", serverConfig.Name);
        return client;
    }

    /// <summary>
    /// Loads tools from an MCP server defined via [MCPServer] attribute in a toolkit.
    /// Handles both flat and nested collapsing modes based on config.CollapseWithinToolkit.
    /// </summary>
    /// <param name="config">Server config with ParentToolkit and CollapseWithinToolkit set</param>
    /// <param name="maxFunctionNamesInDescription">Max function names to show in container descriptions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of AIFunctions (flat tools or container + collapsed tools)</returns>
    public async Task<List<AIFunction>> LoadToolsForToolkitAsync(
        MCPServerConfig config,
        int maxFunctionNamesInDescription = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading MCP tools for toolkit-owned server '{ServerName}' (Parent: {ParentToolkit}, Nested: {CollapseWithinToolkit})",
            config.Name, config.ParentToolkit, config.CollapseWithinToolkit);

        var tools = await LoadServerToolsAsync(config, cancellationToken);

        if (tools.Count == 0)
        {
            _logger.LogWarning("No tools loaded from server '{ServerName}'", config.Name);
            return new List<AIFunction>();
        }

        if (config.CollapseWithinToolkit)
        {
            // Nested mode: MCP tools behind their own MCP_* container, parented to the toolkit
            var (container, collapsedTools) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
                serverName: config.Name,
                tools: tools,
                maxFunctionNamesInDescription: maxFunctionNamesInDescription,
                FunctionResult: config.FunctionResult,
                SystemPrompt: config.SystemPrompt,
                customDescription: config.Description,
                parentContainer: config.ParentToolkit);

            var result = new List<AIFunction> { container };
            result.AddRange(collapsedTools);

            _logger.LogInformation("Loaded {Count} tools from server '{ServerName}' (nested under {ParentToolkit})",
                tools.Count, config.Name, config.ParentToolkit);

            return result;
        }
        else
        {
            // Flat mode: stamp ParentContainer directly on each tool
            var flatTools = tools.Select(tool =>
                ExternalToolCollapsingWrapper.AddParentToolMetadata(
                    tool, config.ParentToolkit ?? config.Name, "MCP", parentContainer: config.ParentToolkit))
                .ToList();

            _logger.LogInformation("Loaded {Count} tools from server '{ServerName}' (flat under {ParentToolkit})",
                tools.Count, config.Name, config.ParentToolkit);

            return flatTools;
        }
    }

    /// <summary>
    /// Performs health check on all connected servers
    /// </summary>
    public async Task<Dictionary<string, bool>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, bool>();
        
        foreach (var (serverName, client) in _clients)
        {
            try
            {
                // Try to list tools as a basic health check
                await client.ListToolsAsync(cancellationToken: cancellationToken);
                results[serverName] = true;
                _logger.LogDebug("Health check passed for server '{ServerName}'", serverName);
            }
            catch (Exception ex)
            {
                results[serverName] = false;
                _logger.LogWarning(ex, "Health check failed for server '{ServerName}': {Error}", serverName, ex.Message);
            }
        }
        
        return results;
    }

    /// <summary>
    /// Gets information about all loaded servers
    /// </summary>
    public IReadOnlyDictionary<string, bool> GetServerStatus()
    {
        return _clients.ToDictionary(kvp => kvp.Key, kvp => true);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _logger.LogInformation("Disposing MCPClientManager and {Count} clients", _clients.Count);
            
            foreach (var (serverName, client) in _clients)
            {
                try
                {
                    if (client is IAsyncDisposable asyncDisposable)
                    {
                        asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                    else if (client is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    
                    _logger.LogDebug("Disposed MCP client for server '{ServerName}'", serverName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing MCP client for server '{ServerName}': {Error}", serverName, ex.Message);
                }
            }
            
            _clients.Clear();
        }
        
        _disposed = true;
    }
}
