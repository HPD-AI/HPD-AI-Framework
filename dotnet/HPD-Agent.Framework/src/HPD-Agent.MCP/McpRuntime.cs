using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using HPD.Agent.Secrets;
using ModelContextProtocol.Client;
using ModelContextProtocol;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;
using HPD.Agent;
using HPD.Environment.Contracts;
using System.Collections;
using System.Collections.Immutable;
using System.Text.Json;
using HPD.Agent.Middleware;


namespace HPD.Agent.MCP;

/// <summary>
/// Manages lifecycle of MCP clients and tool loading
/// </summary>
public sealed class McpRuntime : IAsyncDisposable
{
    private readonly Dictionary<string, McpClient> _clients = new();
    private readonly Dictionary<string, McpServerConfig> _serverConfigs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _oauthClientSecrets = new(StringComparer.Ordinal);
    private readonly ILogger _logger;
    private readonly McpOptions _options;
    private readonly McpCatalogPageCache _catalogPages;
    private readonly string _catalogPrivateScope = Guid.NewGuid().ToString("N");
    private DateTimeOffset? _aggregateCatalogFreshUntil;
    private CacheScope _aggregateCatalogScope = CacheScope.Public;
    private readonly List<IAsyncDisposable> _subscriptionRegistrations = [];
    private readonly List<CancellationTokenSource> _subscriptionCancellationSources = [];
    private readonly List<Task> _subscriptionTasks = [];
    private bool _disposed = false;

    private const string ListResourcesSchemaJson = """
        {
          "type": "object",
          "properties": {
            "cursor": {
              "type": "string",
              "description": "Optional cursor returned by a previous list_resources call."
            },
            "maxResults": {
              "type": "integer",
              "description": "Maximum resources to return. Defaults to the MCP server config limit.",
              "minimum": 1
            }
          },
          "additionalProperties": false
        }
        """;

    private const string ReadResourceSchemaJson = """
        {
          "type": "object",
          "properties": {
            "uri": {
              "type": "string",
              "description": "The MCP resource URI to read."
            },
            "maxChars": {
              "type": "integer",
              "description": "Maximum text characters to return. Defaults to the MCP server config limit.",
              "minimum": 1
            }
          },
          "required": [ "uri" ],
          "additionalProperties": false
        }
        """;

    private const string ListPromptsSchemaJson = """
        {
          "type": "object",
          "properties": {
            "cursor": {
              "type": "string",
              "description": "Optional cursor returned by a previous list_prompts call."
            },
            "maxResults": {
              "type": "integer",
              "description": "Maximum prompts to return. Defaults to the MCP server config limit.",
              "minimum": 1
            }
          },
          "additionalProperties": false
        }
        """;

    private const string GetPromptSchemaJson = """
        {
          "type": "object",
          "properties": {
            "name": {
              "type": "string",
              "description": "The MCP prompt name to retrieve."
            },
            "arguments": {
              "type": "object",
              "description": "Optional prompt arguments keyed by MCP prompt argument name.",
              "additionalProperties": true
            },
            "maxChars": {
              "type": "integer",
              "description": "Maximum text characters to return. Defaults to the MCP server config limit.",
              "minimum": 1
            }
          },
          "required": [ "name" ],
          "additionalProperties": false
        }
        """;

    public McpRuntime(ILogger logger, McpOptions? options = null)
        : this(logger, options, catalogPages: null)
    {
    }

    internal McpRuntime(
        ILogger logger,
        McpOptions? options,
        McpCatalogPageCache? catalogPages)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new McpOptions();
        _options.Validate();
        _catalogPages = catalogPages ?? new McpCatalogPageCache(_options.Catalog);
    }

    /// <summary>
    /// Loads MCP tools from the specified manifest file
    /// </summary>
    /// <param name="manifestPath">Path to the MCP manifest file</param>
    /// <param name="enableCollapsing">Enable ToolHarness Collapsing (groups tools by server behind containers)</param>
    /// <param name="maxFunctionNamesInDescription">Max function names to show in container descriptions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<List<AIFunction>> LoadToolsFromManifestAsync(
        string manifestPath,
        bool enableCollapsing = false,
        int maxFunctionNamesInDescription = 10,
        ISecretResolver? secretResolver = null,
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
                await ResolveServerSecretsAsync(serverConfig, secretResolver, cancellationToken).ConfigureAwait(false);
                _serverConfigs[serverConfig.Name] = serverConfig;
                var functions = await LoadServerFunctionsAsync(serverConfig, cancellationToken);

                // Determine Collapsing for this specific server
                // Per-server setting takes precedence over global setting
                var enableCollapsingForThisServer = serverConfig.EnableCollapsing || enableCollapsing;

                if (enableCollapsingForThisServer && functions.Count > 0)
                {
                    // Wrap tools with container for this server
                    var (container, CollapsedTools) = ExternalToolCollapsingWrapper.WrapMcpServerTools(
                        serverConfig.Name,
                        functions,
                        maxFunctionNamesInDescription,
                        FunctionResult: null,
                        SystemPrompt: null,
                        customDescription: serverConfig.Description);

                    allTools.Add(container);
                    allTools.AddRange(CollapsedTools);

                    _logger.LogInformation("Loaded {Count} MCP functions from server '{ServerName}' (Collapsed with container '{ContainerName}')",
                        functions.Count, serverConfig.Name, container.Name);
                }
                else
                {
                    // Original behavior - no Collapsing
                    allTools.AddRange(functions);
                    _logger.LogInformation("Loaded {Count} MCP functions from server '{ServerName}'",
                        functions.Count, serverConfig.Name);
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
    /// <param name="enableCollapsing">Enable ToolHarness Collapsing (groups tools by server behind containers)</param>
    /// <param name="maxFunctionNamesInDescription">Max function names to show in container descriptions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<List<AIFunction>> LoadToolsFromManifestContentAsync(
        string manifestContent,
        bool enableCollapsing = false,
        int maxFunctionNamesInDescription = 10,
        ISecretResolver? secretResolver = null,
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
                await ResolveServerSecretsAsync(serverConfig, secretResolver, cancellationToken).ConfigureAwait(false);
                _serverConfigs[serverConfig.Name] = serverConfig;
                var functions = await LoadServerFunctionsAsync(serverConfig, cancellationToken);

                // Determine Collapsing for this specific server
                // Per-server setting takes precedence over global setting
                var enableCollapsingForThisServer = serverConfig.EnableCollapsing || enableCollapsing;

                if (enableCollapsingForThisServer && functions.Count > 0)
                {
                    // Wrap tools with container for this server
                    var (container, CollapsedTools) = ExternalToolCollapsingWrapper.WrapMcpServerTools(
                        serverConfig.Name,
                        functions,
                        maxFunctionNamesInDescription,
                        FunctionResult: null,
                        SystemPrompt: null,
                        customDescription: serverConfig.Description);

                    allTools.Add(container);
                    allTools.AddRange(CollapsedTools);

                    _logger.LogInformation("Loaded {Count} MCP functions from server '{ServerName}' (Collapsed with container '{ContainerName}')",
                        functions.Count, serverConfig.Name, container.Name);
                }
                else
                {
                    // Original behavior - no Collapsing
                    allTools.AddRange(functions);
                    _logger.LogInformation("Loaded {Count} MCP functions from server '{ServerName}'",
                        functions.Count, serverConfig.Name);
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
    private static async Task<McpManifest> LoadManifestAsync(string manifestPath, CancellationToken cancellationToken)
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
    private static McpManifest ParseManifest(string manifestContent)
    {
        var manifest = JsonSerializer.Deserialize(manifestContent, McpJsonSerializerContext.Default.McpManifest);

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

    private async Task<List<AIFunction>> LoadServerFunctionsAsync(
        McpServerConfig serverConfig,
        CancellationToken cancellationToken)
    {
        var functions = await LoadServerToolsAsync(serverConfig, cancellationToken).ConfigureAwait(false);
        functions.AddRange(await LoadServerResourceFunctionsAsync(serverConfig, cancellationToken).ConfigureAwait(false));
        functions.AddRange(await LoadServerPromptFunctionsAsync(serverConfig, cancellationToken).ConfigureAwait(false));
        return functions;
    }

    /// <summary>
    /// Loads tools from a specific MCP server
    /// </summary>
    private async Task<List<AIFunction>> LoadServerToolsAsync(McpServerConfig serverConfig, CancellationToken cancellationToken)
    {
        var client = await GetOrCreateClientAsync(serverConfig, cancellationToken);

        // Use only the provided description from config (no reflection-based extraction for AOT compatibility)
        // If description is not provided, it will be empty

        var mcpTools = new List<McpClientTool>();
        string? cursor = null;
        do
        {
            var pageCursor = cursor;
            var partition = CatalogPartition(serverConfig.Name, "tools", pageCursor);
            var page = await GetCatalogPageAsync(
                partition,
                ct => client.ListToolsAsync(
                    new ListToolsRequestParams { Cursor = pageCursor }, ct),
                cancellationToken).ConfigureAwait(false);
            mcpTools.AddRange(page.Tools.Select(tool =>
                new McpClientTool(client, tool, McpJsonUtilities.DefaultOptions)));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

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
                Func<AIFunctionArguments, FunctionExecutionContext, CancellationToken, Task<object?>> invocationWrapper =
                    async (args, functionContext, ct) =>
                    {
                        var result = await McpToolInvocationRuntime.InvokeAsync(
                            new McpToolInvocationRuntime.McpToolInvocationRequest
                            {
                                ServerConfig = serverConfig,
                                ToolName = originalAIFunction.Name,
                                Arguments = args,
                                ParentContext = functionContext,
                                Client = client,
                                InvocationOptions = _options.Invocation,
                                InvokeToolAsync = InvokeOriginalMcpToolAsync
                            },
                            ct).ConfigureAwait(false);

                        return result.ToToolResult();

                        async Task<object?> InvokeOriginalMcpToolAsync(
                            AIFunctionArguments invocationArgs,
                            FunctionExecutionContext? invocationContext,
                            CancellationToken invocationToken)
                        {
                            using var invocationScope = McpInvocationContextScope.Push(
                                serverConfig.Name,
                                originalAIFunction.Name,
                                invocationContext);
                            if (originalAIFunction is HPDAIFunctionFactory.HPDAIFunction hpdFunction &&
                                invocationContext is not null)
                            {
                                return await hpdFunction.InvokeAsync(
                                    invocationArgs,
                                    invocationContext,
                                    invocationToken).ConfigureAwait(false);
                            }

                            return await originalAIFunction.InvokeAsync(
                                invocationArgs,
                                invocationToken).ConfigureAwait(false);
                        }
                    };

                var options = new HPDAIFunctionFactoryOptions
                {
                    Name = originalAIFunction.Name,
                    Description = originalAIFunction.Description,
                    FunctionPermission = serverConfig.RequiresPermission
                        ? CreateMcpPermissionDeclaration(originalAIFunction.Name)
                        : null,
                    // MCP tools don't have validation since they're external - just pass through
                    Validator = (_, _) => new List<ValidationError>(),
                    // Copy schema from original MCP tool for proper parameter handling
                    SchemaProvider = () => CreateMcpToolSchema(
                        originalAIFunction.JsonSchema,
                        McpToolInvocationRuntime.ResolveInvocationModePolicy(
                            serverConfig,
                            originalAIFunction.Name)),
                    AdditionalProperties = CreateCapabilityMetadata(
                        serverConfig.Name, originalAIFunction.Name, client)
                };

                // Attempt to copy schema information if the external tool exposes it
                // Note: Reflection-based schema extraction removed for Native AOT compatibility
                // Tools should provide schema through standard AIFunction properties

                // Create an adapted AIFunction via our factory so it's compatible with generated ToolHarnesses
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

    private async Task<List<AIFunction>> LoadServerResourceFunctionsAsync(
        McpServerConfig serverConfig,
        CancellationToken cancellationToken)
    {
        if (!serverConfig.EnableResources)
        {
            return new List<AIFunction>();
        }

        var client = await GetOrCreateClientAsync(serverConfig, cancellationToken).ConfigureAwait(false);
        if (client.ServerCapabilities.Resources == null)
        {
            _logger.LogInformation(
                "MCP resources enabled for server '{ServerName}', but the server did not advertise resources capability",
                serverConfig.Name);
            return new List<AIFunction>();
        }

        var serverFunctionName = SanitizeFunctionNamePart(serverConfig.Name);
        return new List<AIFunction>
        {
            CreateListResourcesFunction(serverConfig, client, serverFunctionName),
            CreateListResourceTemplatesFunction(serverConfig, client, serverFunctionName),
            CreateReadResourceFunction(serverConfig, client, serverFunctionName)
        };
    }

    private AIFunction CreateListResourcesFunction(
        McpServerConfig serverConfig,
        McpClient client,
        string serverFunctionName)
    {
        return HPDAIFunctionFactory.Create(
            async (args, _, ct) =>
            {
                var json = args.GetJson();
                var serializerOptions = args.GetJsonSerializerOptions();
                var cursor = BindOptionalString(json, "cursor");
                var maxResults = Math.Min(
                    BindOptionalPositiveInt(json, "maxResults", serverConfig.MaxResourceListResults, serializerOptions),
                    serverConfig.MaxResourceListResults);

                var result = new McpResourceListResult
                {
                    Server = serverConfig.Name
                };

                var request = new ListResourcesRequestParams
                {
                    Cursor = cursor
                };

                do
                {
                    var pageCursor = request.Cursor;
                    var page = await _catalogPages.GetAsync(
                        CatalogPartition(serverConfig.Name, "resources", pageCursor),
                        token => client.ListResourcesAsync(
                            new ListResourcesRequestParams { Cursor = pageCursor }, token),
                        ct,
                        _catalogPrivateScope).ConfigureAwait(false);
                    foreach (var resource in page.Resources)
                    {
                        if (result.Resources.Count >= maxResults)
                        {
                            result.Truncated = true;
                            result.NextCursor = null;
                            return JsonSerializer.SerializeToElement(
                                result,
                                McpJsonSerializerContext.Default.McpResourceListResult);
                        }

                        result.Resources.Add(new McpResourceSummary
                        {
                            Name = resource.Name,
                            Title = resource.Title,
                            Uri = resource.Uri,
                            Description = resource.Description,
                            MimeType = resource.MimeType,
                            Size = resource.Size
                        });
                    }

                    request.Cursor = page.NextCursor;
                }
                while (request.Cursor != null && result.Resources.Count < maxResults);

                result.NextCursor = request.Cursor;
                result.Truncated = request.Cursor != null;

                return JsonSerializer.SerializeToElement(
                    result,
                    McpJsonSerializerContext.Default.McpResourceListResult);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = $"mcp_{serverFunctionName}_list_resources",
                Description = $"List readable MCP resources exposed by server '{serverConfig.Name}'.",
                FunctionPermission = null,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateJsonSchema(ListResourcesSchemaJson),
                AdditionalProperties = CreateResourceFunctionMetadata(serverConfig.Name, "list_resources")
            });
    }

    private AIFunction CreateListResourceTemplatesFunction(
        McpServerConfig serverConfig,
        McpClient client,
        string serverFunctionName)
    {
        return HPDAIFunctionFactory.Create(
            async (args, _, ct) =>
            {
                var json = args.GetJson();
                var serializerOptions = args.GetJsonSerializerOptions();
                var cursor = BindOptionalString(json, "cursor");
                var maxResults = Math.Min(
                    BindOptionalPositiveInt(json, "maxResults", serverConfig.MaxResourceListResults, serializerOptions),
                    serverConfig.MaxResourceListResults);

                var result = new McpResourceTemplateListResult
                {
                    Server = serverConfig.Name
                };

                var request = new ListResourceTemplatesRequestParams
                {
                    Cursor = cursor
                };

                do
                {
                    var pageCursor = request.Cursor;
                    var page = await _catalogPages.GetAsync(
                        CatalogPartition(serverConfig.Name, "resource-templates", pageCursor),
                        token => client.ListResourceTemplatesAsync(
                            new ListResourceTemplatesRequestParams { Cursor = pageCursor }, token),
                        ct,
                        _catalogPrivateScope).ConfigureAwait(false);
                    foreach (var template in page.ResourceTemplates)
                    {
                        if (result.ResourceTemplates.Count >= maxResults)
                        {
                            result.Truncated = true;
                            result.NextCursor = null;
                            return JsonSerializer.SerializeToElement(
                                result,
                                McpJsonSerializerContext.Default.McpResourceTemplateListResult);
                        }

                        result.ResourceTemplates.Add(new McpResourceTemplateSummary
                        {
                            Name = template.Name,
                            Title = template.Title,
                            UriTemplate = template.UriTemplate,
                            Description = template.Description,
                            MimeType = template.MimeType,
                            IsTemplated = template.IsTemplated
                        });
                    }

                    request.Cursor = page.NextCursor;
                }
                while (request.Cursor != null && result.ResourceTemplates.Count < maxResults);

                result.NextCursor = request.Cursor;
                result.Truncated = request.Cursor != null;

                return JsonSerializer.SerializeToElement(
                    result,
                    McpJsonSerializerContext.Default.McpResourceTemplateListResult);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = $"mcp_{serverFunctionName}_list_resource_templates",
                Description = $"List readable MCP resource URI templates exposed by server '{serverConfig.Name}'.",
                FunctionPermission = null,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateJsonSchema(ListResourcesSchemaJson),
                AdditionalProperties = CreateResourceFunctionMetadata(serverConfig.Name, "list_resource_templates")
            });
    }

    private AIFunction CreateReadResourceFunction(
        McpServerConfig serverConfig,
        McpClient client,
        string serverFunctionName)
    {
        return HPDAIFunctionFactory.Create(
            async (args, _, ct) =>
            {
                var json = args.GetJson();
                var serializerOptions = args.GetJsonSerializerOptions();
                var uri = HPDToolArgumentBinder.BindRequired<string>(json, "uri", serializerOptions);
                var maxChars = Math.Min(
                    BindOptionalPositiveInt(json, "maxChars", serverConfig.MaxResourceContentLength, serializerOptions),
                    serverConfig.MaxResourceContentLength);

                var readResult = await _catalogPages.GetAsync(
                    CatalogPartition(serverConfig.Name, "resource", uri),
                    token => client.ReadResourceAsync(
                        new ReadResourceRequestParams { Uri = uri }, token),
                    ct,
                    _catalogPrivateScope).ConfigureAwait(false);
                var result = new McpResourceReadResult
                {
                    Server = serverConfig.Name,
                    Uri = uri
                };

                var remainingChars = maxChars;
                for (var contentIndex = 0; contentIndex < readResult.Contents.Count; contentIndex++)
                {
                    var content = readResult.Contents[contentIndex];
                    switch (content)
                    {
                        case TextResourceContents text:
                        {
                            var textValue = text.Text ?? string.Empty;
                            var truncated = textValue.Length > remainingChars;
                            var emittedText = truncated ? textValue[..remainingChars] : textValue;
                            remainingChars -= emittedText.Length;
                            result.Truncated |= truncated;
                            result.Contents.Add(new McpResourceContentSummary
                            {
                                Uri = text.Uri,
                                MimeType = text.MimeType,
                                ContentType = "text",
                                Text = emittedText,
                                Truncated = truncated
                            });
                            break;
                        }

                        case BlobResourceContents blob:
                            result.Contents.Add(new McpResourceContentSummary
                            {
                                Uri = blob.Uri,
                                MimeType = blob.MimeType,
                                ContentType = "blob",
                                Text = null,
                                Truncated = false,
                                ByteLength = blob.DecodedData.Length
                            });
                            break;
                    }

                    if (remainingChars <= 0)
                    {
                        result.Truncated |= contentIndex < readResult.Contents.Count - 1;
                        break;
                    }
                }

                return JsonSerializer.SerializeToElement(
                    result,
                    McpJsonSerializerContext.Default.McpResourceReadResult);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = $"mcp_{serverFunctionName}_read_resource",
                Description = $"Read one MCP resource by URI from server '{serverConfig.Name}'. Binary resources return metadata instead of blob data.",
                FunctionPermission = serverConfig.RequiresPermission
                    ? CreateMcpPermissionDeclaration($"mcp_{serverFunctionName}_read_resource")
                    : null,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateJsonSchema(ReadResourceSchemaJson),
                AdditionalProperties = CreateResourceFunctionMetadata(serverConfig.Name, "read_resource")
            });
    }

    private static Dictionary<string, object?> CreateResourceFunctionMetadata(string serverName, string operation)
    {
        return new Dictionary<string, object?>
        {
            ["SourceType"] = "MCP",
            ["MCPServerName"] = serverName,
            ["McpResourceOperation"] = operation,
            [HPDCapabilityMetadata.AdditionalPropertiesKey] = CreateTypedMetadata(
                serverName, "resource", operation)
        };
    }

    private static Dictionary<string, object?> CreateCapabilityMetadata(
        string serverName,
        string operation,
        McpClient client)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["mcp.server"] = serverName,
            ["mcp.operation"] = operation,
            [HPDCapabilityMetadata.AdditionalPropertiesKey] = CreateTypedMetadata(
                serverName, "tool", operation)
        };
        if (client.ServerCapabilities.Extensions?.ContainsKey("io.modelcontextprotocol/apps") == true)
        {
            metadata["mcp.extension.apps.advertised"] = "true";
            metadata["mcp.apps.rendering"] = "unavailable";
        }
        return metadata;
    }

    internal ImmutableDictionary<string, string> GetSourceMetadata()
    {
        var metadata = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var (serverName, client) in _clients.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            metadata[$"mcp.server.{serverName}.protocol"] = client.NegotiatedProtocolVersion;
            if (client.ServerCapabilities.Extensions?.ContainsKey("io.modelcontextprotocol/apps") == true)
            {
                metadata[$"mcp.server.{serverName}.extension.apps.advertised"] = "true";
                metadata[$"mcp.server.{serverName}.apps.rendering"] = "unavailable";
            }
        }
        if (_aggregateCatalogFreshUntil is { } freshUntil)
            metadata["mcp.catalog.freshUntil"] = freshUntil.ToUniversalTime().ToString("O");
        metadata["mcp.catalog.cacheScope"] =
            _aggregateCatalogScope == CacheScope.Private ? "private" : "public";
        return metadata.ToImmutable();
    }

    private async ValueTask<T> GetCatalogPageAsync<T>(
        string partition,
        Func<CancellationToken, ValueTask<T>> fetch,
        CancellationToken cancellationToken) where T : class, ICacheableResult
    {
        var page = await _catalogPages.GetAsync(
            partition, fetch, cancellationToken, _catalogPrivateScope).ConfigureAwait(false);
        if (_catalogPages.TryGetMetadata(partition, _catalogPrivateScope, out var metadata))
        {
            if (_aggregateCatalogFreshUntil is null || metadata.FreshUntil < _aggregateCatalogFreshUntil)
                _aggregateCatalogFreshUntil = metadata.FreshUntil;
            if (metadata.Scope == CacheScope.Private)
                _aggregateCatalogScope = CacheScope.Private;
        }
        return page;
    }

    private async Task<List<AIFunction>> LoadServerPromptFunctionsAsync(
        McpServerConfig serverConfig,
        CancellationToken cancellationToken)
    {
        if (!serverConfig.EnablePrompts)
        {
            return new List<AIFunction>();
        }

        var client = await GetOrCreateClientAsync(serverConfig, cancellationToken).ConfigureAwait(false);
        if (client.ServerCapabilities.Prompts == null)
        {
            _logger.LogInformation(
                "MCP prompts enabled for server '{ServerName}', but the server did not advertise prompts capability",
                serverConfig.Name);
            return new List<AIFunction>();
        }

        var serverFunctionName = SanitizeFunctionNamePart(serverConfig.Name);
        return new List<AIFunction>
        {
            CreateListPromptsFunction(serverConfig, client, serverFunctionName),
            CreateGetPromptFunction(serverConfig, client, serverFunctionName)
        };
    }

    private AIFunction CreateListPromptsFunction(
        McpServerConfig serverConfig,
        McpClient client,
        string serverFunctionName)
    {
        return HPDAIFunctionFactory.Create(
            async (args, _, ct) =>
            {
                var json = args.GetJson();
                var serializerOptions = args.GetJsonSerializerOptions();
                var cursor = BindOptionalString(json, "cursor");
                var maxResults = Math.Min(
                    BindOptionalPositiveInt(json, "maxResults", serverConfig.MaxPromptListResults, serializerOptions),
                    serverConfig.MaxPromptListResults);

                var result = new McpPromptListResult
                {
                    Server = serverConfig.Name
                };

                var request = new ListPromptsRequestParams
                {
                    Cursor = cursor
                };

                do
                {
                    var pageCursor = request.Cursor;
                    var page = await _catalogPages.GetAsync(
                        CatalogPartition(serverConfig.Name, "prompts", pageCursor),
                        token => client.ListPromptsAsync(
                            new ListPromptsRequestParams { Cursor = pageCursor }, token),
                        ct,
                        _catalogPrivateScope).ConfigureAwait(false);
                    foreach (var prompt in page.Prompts)
                    {
                        if (result.Prompts.Count >= maxResults)
                        {
                            result.Truncated = true;
                            result.NextCursor = null;
                            return JsonSerializer.SerializeToElement(
                                result,
                                McpJsonSerializerContext.Default.McpPromptListResult);
                        }

                        result.Prompts.Add(new McpPromptSummary
                        {
                            Name = prompt.Name,
                            Title = prompt.Title,
                            Description = prompt.Description,
                            Arguments = prompt.Arguments?
                                .Select(argument => new McpPromptArgumentSummary
                                {
                                    Name = argument.Name,
                                    Title = argument.Title,
                                    Description = argument.Description,
                                    Required = argument.Required == true
                                })
                                .ToList() ?? new List<McpPromptArgumentSummary>()
                        });
                    }

                    request.Cursor = page.NextCursor;
                }
                while (request.Cursor != null && result.Prompts.Count < maxResults);

                result.NextCursor = request.Cursor;
                result.Truncated = request.Cursor != null;

                return JsonSerializer.SerializeToElement(
                    result,
                    McpJsonSerializerContext.Default.McpPromptListResult);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = $"mcp_{serverFunctionName}_list_prompts",
                Description = $"List MCP prompts exposed by server '{serverConfig.Name}'.",
                FunctionPermission = null,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateJsonSchema(ListPromptsSchemaJson),
                AdditionalProperties = CreatePromptFunctionMetadata(serverConfig.Name, "list_prompts")
            });
    }

    private static AIFunction CreateGetPromptFunction(
        McpServerConfig serverConfig,
        McpClient client,
        string serverFunctionName)
    {
        return HPDAIFunctionFactory.Create(
            async (args, _, ct) =>
            {
                var json = args.GetJson();
                var serializerOptions = args.GetJsonSerializerOptions();
                var name = HPDToolArgumentBinder.BindRequired<string>(json, "name", serializerOptions);
                var maxChars = Math.Min(
                    BindOptionalPositiveInt(json, "maxChars", serverConfig.MaxPromptContentLength, serializerOptions),
                    serverConfig.MaxPromptContentLength);

                var promptArguments = BindPromptArguments(json);
                var promptResult = await client.GetPromptAsync(
                    new GetPromptRequestParams
                    {
                        Name = name,
                        Arguments = promptArguments
                    },
                    ct).ConfigureAwait(false);

                var result = new McpPromptGetResult
                {
                    Server = serverConfig.Name,
                    Name = name,
                    Description = promptResult.Description
                };

                var remainingChars = maxChars;
                foreach (var message in promptResult.Messages)
                {
                    var content = SummarizePromptContent(message.Content, ref remainingChars);
                    result.Truncated |= content.Truncated;
                    result.Messages.Add(new McpPromptMessageSummary
                    {
                        Role = message.Role.ToString().ToLowerInvariant(),
                        Content = content
                    });
                }

                result.Truncated |= remainingChars <= 0 &&
                    result.Messages.Count < promptResult.Messages.Count;

                return JsonSerializer.SerializeToElement(
                    result,
                    McpJsonSerializerContext.Default.McpPromptGetResult);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = $"mcp_{serverFunctionName}_get_prompt",
                Description = $"Get one MCP prompt by name from server '{serverConfig.Name}'. Prompt messages are returned as structured tool output.",
                FunctionPermission = serverConfig.RequiresPermission
                    ? CreateMcpPermissionDeclaration($"mcp_{serverFunctionName}_get_prompt")
                    : null,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateJsonSchema(GetPromptSchemaJson),
                AdditionalProperties = CreatePromptFunctionMetadata(serverConfig.Name, "get_prompt")
            });
    }

    private static AIFunctionPermissionDeclaration CreateMcpPermissionDeclaration(string functionName) => new()
    {
        RequiresPermission = true,
        Authority = $"function/{Uri.EscapeDataString(functionName)}",
        Source = PermissionDeclarationSource.FrameworkDefault
    };

    private static Dictionary<string, object?> CreatePromptFunctionMetadata(string serverName, string operation)
    {
        return new Dictionary<string, object?>
        {
            ["SourceType"] = "MCP",
            ["MCPServerName"] = serverName,
            ["McpPromptOperation"] = operation,
            [HPDCapabilityMetadata.AdditionalPropertiesKey] = CreateTypedMetadata(
                serverName, "prompt", operation)
        };
    }

    private static HPDCapabilityMetadata CreateTypedMetadata(
        string serverName,
        string category,
        string operation) => new()
    {
        Id = CapabilityId.Create($"mcp:{serverName}:{category}:{operation}"),
        Kind = HPDCapabilityKind.Mcp
    };

    private static IDictionary<string, JsonElement>? BindPromptArguments(JsonElement json)
    {
        if (!HPDToolArgumentBinder.TryGetProperty(json, "arguments", out var arguments) ||
            arguments.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new HPDToolArgumentException(
                "arguments",
                "Property 'arguments' must be an object.",
                "invalid_object");
        }

        var result = new Dictionary<string, JsonElement>();
        foreach (var property in arguments.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        return result;
    }

    private static McpPromptContentSummary SummarizePromptContent(ContentBlock content, ref int remainingChars)
    {
        switch (content)
        {
            case TextContentBlock text:
                return SummarizeTextPromptContent(text.Text, ref remainingChars);

            case ImageContentBlock image:
                return new McpPromptContentSummary
                {
                    ContentType = "image",
                    MimeType = image.MimeType,
                    ByteLength = image.DecodedData.Length
                };

            case AudioContentBlock audio:
                return new McpPromptContentSummary
                {
                    ContentType = "audio",
                    MimeType = audio.MimeType,
                    ByteLength = audio.DecodedData.Length
                };

            case EmbeddedResourceBlock embedded:
                return SummarizeEmbeddedResourcePromptContent(embedded.Resource, ref remainingChars);

            case ResourceLinkBlock link:
                return new McpPromptContentSummary
                {
                    ContentType = "resource_link",
                    Uri = link.Uri,
                    Name = link.Name,
                    Title = link.Title,
                    Description = link.Description,
                    MimeType = link.MimeType
                };

            default:
                return new McpPromptContentSummary
                {
                    ContentType = content.Type
                };
        }
    }

    private static McpPromptContentSummary SummarizeTextPromptContent(string text, ref int remainingChars)
    {
        var safeText = text ?? string.Empty;
        var truncated = safeText.Length > remainingChars;
        var emittedText = truncated ? safeText[..Math.Max(remainingChars, 0)] : safeText;
        remainingChars -= emittedText.Length;

        return new McpPromptContentSummary
        {
            ContentType = "text",
            Text = emittedText,
            Truncated = truncated
        };
    }

    private static McpPromptContentSummary SummarizeEmbeddedResourcePromptContent(
        ResourceContents resource,
        ref int remainingChars)
    {
        switch (resource)
        {
            case TextResourceContents text:
            {
                var summary = SummarizeTextPromptContent(text.Text ?? string.Empty, ref remainingChars);
                summary.ContentType = "resource_text";
                summary.Uri = text.Uri;
                summary.MimeType = text.MimeType;
                return summary;
            }

            case BlobResourceContents blob:
                return new McpPromptContentSummary
                {
                    ContentType = "resource_blob",
                    Uri = blob.Uri,
                    MimeType = blob.MimeType,
                    ByteLength = blob.DecodedData.Length
                };

            default:
                return new McpPromptContentSummary
                {
                    ContentType = "resource",
                    Uri = resource.Uri,
                    MimeType = resource.MimeType
                };
        }
    }

    private static string? BindOptionalString(JsonElement json, string name)
    {
        if (!HPDToolArgumentBinder.TryGetProperty(json, name, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.GetString();
    }

    private static int BindOptionalPositiveInt(
        JsonElement json,
        string name,
        int defaultValue,
        JsonSerializerOptions serializerOptions)
    {
        if (!HPDToolArgumentBinder.TryGetProperty(json, name, out _))
        {
            return defaultValue;
        }

        var value = HPDToolArgumentBinder.BindOptional(json, name, defaultValue, serializerOptions);
        if (value <= 0)
        {
            throw new HPDToolArgumentException(
                name,
                $"Property '{name}' must be greater than zero.",
                "invalid_positive_integer");
        }

        return value;
    }

    private static JsonElement CreateJsonSchema(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        return document.RootElement.Clone();
    }

    internal static JsonElement CreateMcpToolSchema(
        JsonElement originalSchema,
        AgentInvocationModePolicy invocationModePolicy)
        => AgentInvocationModes.CreateSchema(originalSchema, invocationModePolicy);

    private static string SanitizeFunctionNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "server";
        }

        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        var index = 0;
        var lastWasUnderscore = false;
        foreach (var ch in value)
        {
            var sanitized = char.IsAsciiLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_';
            if (sanitized == '_' && lastWasUnderscore)
            {
                continue;
            }

            buffer[index++] = sanitized;
            lastWasUnderscore = sanitized == '_';
        }

        var result = new string(buffer[..index]).Trim('_');
        return string.IsNullOrEmpty(result) ? "server" : result;
    }

    internal async Task ResolveServerSecretsAsync(
        McpServerConfig config,
        ISecretResolver? secretResolver,
        CancellationToken cancellationToken = default)
    {
        if (secretResolver == null)
        {
            return;
        }

        if (config.EnvironmentSecretKeys is { Count: > 0 })
        {
            config.Environment ??= new Dictionary<string, string?>();
            foreach (var (name, key) in config.EnvironmentSecretKeys)
            {
                if (config.Environment.TryGetValue(name, out var explicitValue) && explicitValue is not null)
                {
                    continue;
                }

                config.Environment[name] = await ResolveRequiredSecretAsync(
                    secretResolver,
                    key,
                    $"MCP server '{config.Name}' environment variable '{name}'",
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (config.HeaderSecretKeys is { Count: > 0 })
        {
            config.Headers ??= new Dictionary<string, string>();
            foreach (var (name, key) in config.HeaderSecretKeys)
            {
                if (config.Headers.ContainsKey(name))
                {
                    continue;
                }

                config.Headers[name] = await ResolveRequiredSecretAsync(
                    secretResolver,
                    key,
                    $"MCP server '{config.Name}' HTTP header '{name}'",
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (config.OAuth != null)
        {
            if (!string.IsNullOrWhiteSpace(config.OAuth.ClientSecretKey))
            {
                _oauthClientSecrets[config.Name] = await ResolveRequiredSecretAsync(
                    secretResolver,
                    config.OAuth.ClientSecretKey,
                    $"MCP server '{config.Name}' OAuth client secret",
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string> ResolveRequiredSecretAsync(
        ISecretResolver resolver,
        string key,
        string displayName,
        CancellationToken cancellationToken)
    {
        var resolved = await resolver.ResolveAsync(key, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            throw new SecretNotFoundException(
                $"Required secret '{displayName}' (key: '{key}') was not found.",
                key,
                displayName);
        }

        return resolved.Value.Value;
    }

    /// <summary>
    /// Gets or creates an MCP client for the specified server
    /// </summary>
    private async Task<McpClient> GetOrCreateClientAsync(McpServerConfig serverConfig, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(serverConfig.Name, out var existingClient))
        {
            return existingClient;
        }

        _logger.LogDebug("Creating new MCP client for server '{ServerName}'", serverConfig.Name);

        var transport = CreateTransport(serverConfig);
        var clientOptions = CreateClientOptions(serverConfig);

        using var timeoutCts = new CancellationTokenSource(_options.Protocol.DiscoveryTimeout);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var client = await McpClient.CreateAsync(transport, clientOptions, cancellationToken: combinedCts.Token);

        if (string.IsNullOrWhiteSpace(serverConfig.Description))
        {
            serverConfig.Description = CreateDescriptionFromServerInfo(client);
        }

        _clients[serverConfig.Name] = client;

        _logger.LogDebug("Successfully created MCP client for server '{ServerName}' using {Transport} transport",
            serverConfig.Name, serverConfig.Transport);
        return client;
    }

    internal async ValueTask<McpClient?> TryGetRecoveryClientAsync(
        string serverName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        return _serverConfigs.TryGetValue(serverName, out var config)
            ? await GetOrCreateClientAsync(config, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private static string CatalogPartition(string serverName, string kind, string? cursor) =>
        $"{serverName}\n{kind}\n{cursor ?? string.Empty}";

    private static string CatalogPartitionPrefix(string serverName, string kind) =>
        $"{serverName}\n{kind}\n";

    private IClientTransport CreateTransport(McpServerConfig serverConfig)
    {
        if (serverConfig.IsStdio)
        {
            return CreateStdioTransport(serverConfig);
        }

        if (serverConfig.IsHttp)
        {
            return CreateHttpTransport(serverConfig);
        }

        throw new InvalidOperationException($"Unsupported MCP transport '{serverConfig.Transport}' for server '{serverConfig.Name}'.");
    }

    private IClientTransport CreateStdioTransport(McpServerConfig serverConfig)
    {
        if (serverConfig.ProcessIsolation?.Enabled == true)
        {
            if (_options.ProcessProvider is null)
            {
                throw new InvalidOperationException(
                    $"MCP server '{serverConfig.Name}' requested process isolation, but McpOptions.ProcessProvider was not configured.");
            }

            return new McpProcessClientTransport(
                serverConfig,
                _options.ProcessProvider,
                CreateProcessEnvironmentVariables(serverConfig));
        }

        return CreateSdkStdioTransport(serverConfig);
    }

    private static StdioClientTransport CreateSdkStdioTransport(McpServerConfig serverConfig)
    {
        var environment = CreateEnvironmentVariables(serverConfig);
        var transportOptions = new StdioClientTransportOptions
        {
            Name = serverConfig.Name,
            Command = serverConfig.Command!,
            Arguments = [.. serverConfig.Arguments],
            WorkingDirectory = serverConfig.WorkingDirectory,
            InheritEnvironmentVariables = serverConfig.InheritEnvironmentVariables,
            EnvironmentVariables = environment,
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        };

        return new StdioClientTransport(transportOptions);
    }

    private static IReadOnlyDictionary<string, string?> CreateProcessEnvironmentVariables(McpServerConfig serverConfig)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (!serverConfig.InheritEnvironmentVariables)
        {
            foreach (DictionaryEntry entry in global::System.Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string key)
                    environment[key] = null;
            }
        }

        if (serverConfig.UseDefaultEnvironmentVariables)
        {
            foreach (var (key, value) in StdioClientTransportOptions.GetDefaultEnvironmentVariables())
                environment[key] = value;
        }

        if (serverConfig.Environment is { Count: > 0 })
        {
            foreach (var (key, value) in serverConfig.Environment)
                environment[key] = value;
        }

        return environment;
    }

    private static IDictionary<string, string?>? CreateEnvironmentVariables(McpServerConfig serverConfig)
    {
        Dictionary<string, string?>? environment = null;

        if (serverConfig.UseDefaultEnvironmentVariables)
        {
            environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        }

        if (serverConfig.Environment is { Count: > 0 })
        {
            environment ??= new Dictionary<string, string?>();
            foreach (var (key, value) in serverConfig.Environment)
            {
                environment[key] = value;
            }
        }

        return environment;
    }

    private HttpClientTransport CreateHttpTransport(McpServerConfig serverConfig)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Name = serverConfig.Name,
            Endpoint = serverConfig.Endpoint!,
            ConnectionTimeout = _options.Protocol.DiscoveryTimeout,
            AdditionalHeaders = serverConfig.Headers,
            OAuth = CreateOAuthOptions(serverConfig)
        };

        var httpClient = _options.HttpClientFactory?.Invoke(serverConfig);
        return httpClient is null
            ? new HttpClientTransport(transportOptions)
            : new HttpClientTransport(transportOptions, httpClient, loggerFactory: null, ownsHttpClient: false);
    }

    private ClientOAuthOptions? CreateOAuthOptions(McpServerConfig serverConfig)
    {
        var config = serverConfig.OAuth;
        if (config == null)
        {
            return null;
        }

        var options = new ClientOAuthOptions
        {
            RedirectUri = config.RedirectUri!,
            ClientId = config.RegistrationMode == McpOAuthClientRegistrationMode.PreRegistered
                ? config.ClientId
                : null,
            ClientSecret = _oauthClientSecrets.GetValueOrDefault(serverConfig.Name),
            ClientMetadataDocumentUri =
                config.RegistrationMode == McpOAuthClientRegistrationMode.ClientIdMetadataDocument
                    ? config.ClientIdMetadataDocument
                    : null,
            Scopes = config.Scopes,
            DynamicClientRegistration =
                config.RegistrationMode == McpOAuthClientRegistrationMode.DynamicRegistration
                    ? new DynamicClientRegistrationOptions()
                    : null,
            TokenCache = _options.AuthorizationStore is { } store
                ? new McpAuthorizationTokenCache(store, serverConfig, config)
                : null
        };
        return options;
    }

    private McpClientOptions CreateClientOptions(McpServerConfig serverConfig)
    {
        var options = new McpClientOptions
        {
            ProtocolVersion = serverConfig.ExactVersion ?? _options.Protocol.ExactVersion,
            InitializationTimeout = _options.Protocol.DiscoveryTimeout,
            Handlers = McpClientHandlerAdapter.Create(serverConfig.Name, _options.Invocation)
        };

        options.DiscoverProbeTimeout = _options.Protocol.DiscoveryTimeout;

        return options;
    }

    private static string? CreateDescriptionFromServerInfo(McpClient client)
    {
        var serverInfo = client.ServerInfo;
        if (!string.IsNullOrWhiteSpace(serverInfo.Description))
        {
            return serverInfo.Description;
        }

        if (!string.IsNullOrWhiteSpace(serverInfo.Title))
        {
            return serverInfo.Title;
        }

        if (!string.IsNullOrWhiteSpace(serverInfo.Name))
        {
            return $"{serverInfo.Name} MCP server";
        }

        return null;
    }

    /// <summary>
    /// Loads tools from an MCP server defined via [McpServer] attribute in a toolharness.
    /// Handles both flat and nested collapsing modes based on config.CollapseWithinToolHarness.
    /// </summary>
    /// <param name="config">Server config with ParentToolHarness and CollapseWithinToolHarness set</param>
    /// <param name="maxFunctionNamesInDescription">Max function names to show in container descriptions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of AIFunctions (flat tools or container + collapsed tools)</returns>
    public async Task<List<AIFunction>> LoadToolsForToolHarnessAsync(
        McpServerConfig config,
        int maxFunctionNamesInDescription = 10,
        ISecretResolver? secretResolver = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading MCP tools for toolharness-owned server '{ServerName}' (Parent: {ParentToolHarness}, Nested: {CollapseWithinToolHarness})",
            config.Name, config.ParentToolHarness, config.CollapseWithinToolHarness);

        await ResolveServerSecretsAsync(config, secretResolver, cancellationToken).ConfigureAwait(false);
        _serverConfigs[config.Name] = config;
        var functions = await LoadServerFunctionsAsync(config, cancellationToken);

        if (functions.Count == 0)
        {
            _logger.LogWarning("No MCP functions loaded from server '{ServerName}'", config.Name);
            return new List<AIFunction>();
        }

        if (config.CollapseWithinToolHarness)
        {
            // Nested mode: MCP tools behind their own MCP_* container, parented to the toolharness
            var (container, collapsedTools) = ExternalToolCollapsingWrapper.WrapMcpServerTools(
                serverName: config.Name,
                tools: functions,
                maxFunctionNamesInDescription: maxFunctionNamesInDescription,
                FunctionResult: null,
                SystemPrompt: null,
                customDescription: config.Description,
                parentContainer: config.ParentToolHarness);

            var result = new List<AIFunction> { container };
            result.AddRange(collapsedTools);

            _logger.LogInformation("Loaded {Count} MCP functions from server '{ServerName}' (nested under {ParentToolHarness})",
                functions.Count, config.Name, config.ParentToolHarness);

            return result;
        }
        else
        {
            // Flat mode: stamp ParentContainer directly on each tool
            var flatTools = functions.Select(tool =>
                ExternalToolCollapsingWrapper.AddParentToolMetadata(
                    tool, config.ParentToolHarness ?? config.Name, "MCP", parentContainer: config.ParentToolHarness))
                .ToList();

            _logger.LogInformation("Loaded {Count} MCP functions from server '{ServerName}' (flat under {ParentToolHarness})",
                functions.Count, config.Name, config.ParentToolHarness);

            return flatTools;
        }
    }

    /// <summary>Starts negotiated notification delivery for this immutable runtime revision.</summary>
    /// <param name="invalidate">Receives refresh hints; it never mutates the active snapshot directly.</param>
    /// <param name="cancellationToken">Cancels listener startup.</param>
    internal async ValueTask StartSubscriptionsAsync(
        Action<string> invalidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invalidate);

        foreach (var (serverName, config) in _serverConfigs)
        {
            if (!_options.Subscriptions.EnableCatalogInvalidation &&
                _options.Subscriptions.ResourceUris.Count == 0)
            {
                continue;
            }

            if (!_clients.TryGetValue(serverName, out var client))
            {
                continue;
            }

            try
            {
                // SDK notification handlers project messages delivered by either era. The
                // transport mechanism itself remains era-gated below.
                AttachInvalidationHandlers(config, client, invalidate);
                if (string.CompareOrdinal(
                    client.NegotiatedProtocolVersion,
                    "2026-07-28") >= 0)
                {
                    StartModernSubscription(config, client, cancellationToken);
                }
                else
                {
                    await AttachLegacyResourceSubscriptionsAsync(
                        config, client, invalidate, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MCP subscriptions failed for server '{ServerName}': {Error}",
                    config.Name,
                    ex.Message);
                if (_options.Subscriptions.FailurePolicy == McpSubscriptionFailurePolicy.FailSource)
                {
                    throw new InvalidOperationException(
                        $"Failed to attach MCP subscriptions for server '{config.Name}'.", ex);
                }
            }
        }
    }

    private void AttachInvalidationHandlers(
        McpServerConfig config,
        McpClient client,
        Action<string> invalidate)
    {
        if (client.ServerCapabilities.Tools?.ListChanged == true)
        {
            _subscriptionRegistrations.Add(client.RegisterNotificationHandler(
                NotificationMethods.ToolListChangedNotification,
                (_, _) =>
                {
                    _catalogPages.Invalidate(CatalogPartitionPrefix(config.Name, "tools"));
                    invalidate($"MCP server '{config.Name}' tools changed.");
                    return ValueTask.CompletedTask;
                }));
        }

        if (client.ServerCapabilities.Prompts?.ListChanged == true)
        {
            _subscriptionRegistrations.Add(client.RegisterNotificationHandler(
                NotificationMethods.PromptListChangedNotification,
                (_, _) =>
                {
                    _catalogPages.Invalidate(CatalogPartitionPrefix(config.Name, "prompts"));
                    invalidate($"MCP server '{config.Name}' prompts changed.");
                    return ValueTask.CompletedTask;
                }));
        }

        if (client.ServerCapabilities.Resources?.ListChanged == true)
        {
            _subscriptionRegistrations.Add(client.RegisterNotificationHandler(
                NotificationMethods.ResourceListChangedNotification,
                (_, _) =>
                {
                    _catalogPages.Invalidate(CatalogPartitionPrefix(config.Name, "resources"));
                    _catalogPages.Invalidate(CatalogPartitionPrefix(config.Name, "resource-templates"));
                    invalidate($"MCP server '{config.Name}' resources changed.");
                    return ValueTask.CompletedTask;
                }));
        }
        if (_options.Subscriptions.ResourceUris.Count > 0)
        {
            _subscriptionRegistrations.Add(client.RegisterNotificationHandler(
                NotificationMethods.ResourceUpdatedNotification,
                (_, _) =>
                {
                    _catalogPages.Invalidate(CatalogPartitionPrefix(config.Name, "resource"));
                    invalidate($"MCP server '{config.Name}' resource changed.");
                    return ValueTask.CompletedTask;
                }));
        }
    }

    private void StartModernSubscription(
        McpServerConfig config,
        McpClient client,
        CancellationToken startupCancellationToken)
    {
        var listenerCancellation = CancellationTokenSource.CreateLinkedTokenSource(startupCancellationToken);
        _subscriptionCancellationSources.Add(listenerCancellation);
        var request = new SubscriptionsListenRequestParams
        {
            Notifications = new SubscriptionsListenNotifications
            {
                ToolsListChanged = _options.Subscriptions.EnableCatalogInvalidation &&
                    client.ServerCapabilities.Tools?.ListChanged == true,
                PromptsListChanged = _options.Subscriptions.EnableCatalogInvalidation &&
                    client.ServerCapabilities.Prompts?.ListChanged == true,
                ResourcesListChanged = _options.Subscriptions.EnableCatalogInvalidation &&
                    client.ServerCapabilities.Resources?.ListChanged == true,
                ResourceSubscriptions = [.. _options.Subscriptions.ResourceUris]
            }
        };

        var listener = client.SendRequestAsync<SubscriptionsListenRequestParams, EmptyResult>(
            RequestMethods.SubscriptionsListen,
            request,
            McpJsonSerializerContext.Default.Options,
            requestId: default,
            cancellationToken: listenerCancellation.Token);
        _subscriptionTasks.Add(ObserveSubscriptionAsync(
            config.Name,
            listener.AsTask(),
            listenerCancellation.Token));
    }

    private async Task ObserveSubscriptionAsync(
        string serverName,
        Task<EmptyResult> listener,
        CancellationToken cancellationToken)
    {
        try
        {
            await listener.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MCP subscription listener stopped for server '{ServerName}': {Error}",
                serverName,
                ex.Message);
        }
    }

    private async Task AttachLegacyResourceSubscriptionsAsync(
        McpServerConfig config,
        McpClient client,
        Action<string> invalidate,
        CancellationToken cancellationToken)
    {
        if (_options.Subscriptions.ResourceUris.Count == 0)
        {
            return;
        }

        if (client.ServerCapabilities.Resources?.Subscribe != true)
        {
            throw new InvalidOperationException(
                $"MCP server '{config.Name}' does not advertise resource subscription support.");
        }

        foreach (var uri in _options.Subscriptions.ResourceUris)
        {
            var subscription = await client.SubscribeToResourceAsync(
                uri,
                (notification, _) =>
                {
                    invalidate($"MCP server '{config.Name}' resource '{notification.Uri}' changed.");
                    return ValueTask.CompletedTask;
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _subscriptionRegistrations.Add(subscription);
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

    /// <summary>Asynchronously closes all connections owned by this runtime revision.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.LogInformation("Disposing McpRuntime and {Count} clients", _clients.Count);

        foreach (var cancellation in _subscriptionCancellationSources)
            await cancellation.CancelAsync().ConfigureAwait(false);
        foreach (var registration in _subscriptionRegistrations)
            await registration.DisposeAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_subscriptionTasks).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timed out waiting for MCP subscription listeners to stop.");
        }
        foreach (var cancellation in _subscriptionCancellationSources)
            cancellation.Dispose();
        _subscriptionCancellationSources.Clear();
        _subscriptionRegistrations.Clear();
        _subscriptionTasks.Clear();
            
            foreach (var (serverName, client) in _clients)
            {
                try
                {
                    if (client is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
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
        _serverConfigs.Clear();
    }
}
