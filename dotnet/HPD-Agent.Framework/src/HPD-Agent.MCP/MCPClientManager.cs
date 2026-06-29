using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using HPD.Agent.Secrets;
using ModelContextProtocol.Client;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;
using HPD.Environment.Contracts;
using System.Collections;
using System.Text.Json;
using HPD.Agent.Middleware;
using HPD.Events;


namespace HPD.Agent.MCP;

/// <summary>
/// Manages lifecycle of MCP clients and tool loading
/// </summary>
public class MCPClientManager : IDisposable
{
    private readonly Dictionary<string, McpClient> _clients = new();
    private readonly Dictionary<string, MCPServerConfig> _serverConfigs = new(StringComparer.Ordinal);
    private readonly ILogger _logger;
    private readonly MCPOptions _options;
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

    public MCPClientManager(ILogger logger, MCPOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new MCPOptions();
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
                var enableCollapsingForThisServer = serverConfig.EnableCollapsing ?? enableCollapsing;

                if (enableCollapsingForThisServer && functions.Count > 0)
                {
                    // Wrap tools with container for this server
                    var (container, CollapsedTools) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
                        serverConfig.Name,
                        functions,
                        maxFunctionNamesInDescription,
                        FunctionResult: serverConfig.FunctionResult,
                        SystemPrompt: serverConfig.SystemPrompt,
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
                var enableCollapsingForThisServer = serverConfig.EnableCollapsing ?? enableCollapsing;

                if (enableCollapsingForThisServer && functions.Count > 0)
                {
                    // Wrap tools with container for this server
                    var (container, CollapsedTools) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
                        serverConfig.Name,
                        functions,
                        maxFunctionNamesInDescription,
                        FunctionResult: serverConfig.FunctionResult,
                        SystemPrompt: serverConfig.SystemPrompt,
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

    private async Task<List<AIFunction>> LoadServerFunctionsAsync(
        MCPServerConfig serverConfig,
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
                Func<AIFunctionArguments, FunctionExecutionContext, CancellationToken, Task<object?>> invocationWrapper =
                    async (args, functionContext, ct) =>
                    {
                        if (originalAIFunction is HPDAIFunctionFactory.HPDAIFunction hpdFunction)
                        {
                            return await hpdFunction.InvokeAsync(args, functionContext, ct).ConfigureAwait(false);
                        }

                        return await originalAIFunction.InvokeAsync(args, ct).ConfigureAwait(false);
                    };

                var options = new HPDAIFunctionFactoryOptions
                {
                    Name = originalAIFunction.Name,
                    Description = originalAIFunction.Description,
                    RequiresPermission = serverConfig.RequiresPermission,
                    // MCP tools don't have validation since they're external - just pass through
                    Validator = (_, _) => new List<ValidationError>(),
                    // Copy schema from original MCP tool for proper parameter handling
                    SchemaProvider = () => originalAIFunction.JsonSchema
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
        MCPServerConfig serverConfig,
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

    private static AIFunction CreateListResourcesFunction(
        MCPServerConfig serverConfig,
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

                var result = new MCPResourceListResult
                {
                    Server = serverConfig.Name
                };

                var request = new ListResourcesRequestParams
                {
                    Cursor = cursor
                };

                do
                {
                    var page = await client.ListResourcesAsync(request, ct).ConfigureAwait(false);
                    foreach (var resource in page.Resources)
                    {
                        if (result.Resources.Count >= maxResults)
                        {
                            result.Truncated = true;
                            result.NextCursor = null;
                            return JsonSerializer.SerializeToElement(
                                result,
                                MCPJsonSerializerContext.Default.MCPResourceListResult);
                        }

                        result.Resources.Add(new MCPResourceSummary
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
                    MCPJsonSerializerContext.Default.MCPResourceListResult);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = $"mcp_{serverFunctionName}_list_resources",
                Description = $"List readable MCP resources exposed by server '{serverConfig.Name}'.",
                RequiresPermission = false,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateJsonSchema(ListResourcesSchemaJson),
                AdditionalProperties = CreateResourceFunctionMetadata(serverConfig.Name, "list_resources")
            });
    }

    private static AIFunction CreateListResourceTemplatesFunction(
        MCPServerConfig serverConfig,
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

                var result = new MCPResourceTemplateListResult
                {
                    Server = serverConfig.Name
                };

                var request = new ListResourceTemplatesRequestParams
                {
                    Cursor = cursor
                };

                do
                {
                    var page = await client.ListResourceTemplatesAsync(request, ct).ConfigureAwait(false);
                    foreach (var template in page.ResourceTemplates)
                    {
                        if (result.ResourceTemplates.Count >= maxResults)
                        {
                            result.Truncated = true;
                            result.NextCursor = null;
                            return JsonSerializer.SerializeToElement(
                                result,
                                MCPJsonSerializerContext.Default.MCPResourceTemplateListResult);
                        }

                        result.ResourceTemplates.Add(new MCPResourceTemplateSummary
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
                    MCPJsonSerializerContext.Default.MCPResourceTemplateListResult);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = $"mcp_{serverFunctionName}_list_resource_templates",
                Description = $"List readable MCP resource URI templates exposed by server '{serverConfig.Name}'.",
                RequiresPermission = false,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateJsonSchema(ListResourcesSchemaJson),
                AdditionalProperties = CreateResourceFunctionMetadata(serverConfig.Name, "list_resource_templates")
            });
    }

    private static AIFunction CreateReadResourceFunction(
        MCPServerConfig serverConfig,
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

                var readResult = await client.ReadResourceAsync(uri, cancellationToken: ct).ConfigureAwait(false);
                var result = new MCPResourceReadResult
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
                            result.Contents.Add(new MCPResourceContentSummary
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
                            result.Contents.Add(new MCPResourceContentSummary
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
                    MCPJsonSerializerContext.Default.MCPResourceReadResult);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = $"mcp_{serverFunctionName}_read_resource",
                Description = $"Read one MCP resource by URI from server '{serverConfig.Name}'. Binary resources return metadata instead of blob data.",
                RequiresPermission = serverConfig.RequiresPermission,
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
            ["MCPResourceOperation"] = operation
        };
    }

    private async Task<List<AIFunction>> LoadServerPromptFunctionsAsync(
        MCPServerConfig serverConfig,
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

    private static AIFunction CreateListPromptsFunction(
        MCPServerConfig serverConfig,
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

                var result = new MCPPromptListResult
                {
                    Server = serverConfig.Name
                };

                var request = new ListPromptsRequestParams
                {
                    Cursor = cursor
                };

                do
                {
                    var page = await client.ListPromptsAsync(request, ct).ConfigureAwait(false);
                    foreach (var prompt in page.Prompts)
                    {
                        if (result.Prompts.Count >= maxResults)
                        {
                            result.Truncated = true;
                            result.NextCursor = null;
                            return JsonSerializer.SerializeToElement(
                                result,
                                MCPJsonSerializerContext.Default.MCPPromptListResult);
                        }

                        result.Prompts.Add(new MCPPromptSummary
                        {
                            Name = prompt.Name,
                            Title = prompt.Title,
                            Description = prompt.Description,
                            Arguments = prompt.Arguments?
                                .Select(argument => new MCPPromptArgumentSummary
                                {
                                    Name = argument.Name,
                                    Title = argument.Title,
                                    Description = argument.Description,
                                    Required = argument.Required == true
                                })
                                .ToList() ?? new List<MCPPromptArgumentSummary>()
                        });
                    }

                    request.Cursor = page.NextCursor;
                }
                while (request.Cursor != null && result.Prompts.Count < maxResults);

                result.NextCursor = request.Cursor;
                result.Truncated = request.Cursor != null;

                return JsonSerializer.SerializeToElement(
                    result,
                    MCPJsonSerializerContext.Default.MCPPromptListResult);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = $"mcp_{serverFunctionName}_list_prompts",
                Description = $"List MCP prompts exposed by server '{serverConfig.Name}'.",
                RequiresPermission = false,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateJsonSchema(ListPromptsSchemaJson),
                AdditionalProperties = CreatePromptFunctionMetadata(serverConfig.Name, "list_prompts")
            });
    }

    private static AIFunction CreateGetPromptFunction(
        MCPServerConfig serverConfig,
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

                var result = new MCPPromptGetResult
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
                    result.Messages.Add(new MCPPromptMessageSummary
                    {
                        Role = message.Role.ToString().ToLowerInvariant(),
                        Content = content
                    });
                }

                result.Truncated |= remainingChars <= 0 &&
                    result.Messages.Count < promptResult.Messages.Count;

                return JsonSerializer.SerializeToElement(
                    result,
                    MCPJsonSerializerContext.Default.MCPPromptGetResult);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = $"mcp_{serverFunctionName}_get_prompt",
                Description = $"Get one MCP prompt by name from server '{serverConfig.Name}'. Prompt messages are returned as structured tool output.",
                RequiresPermission = serverConfig.RequiresPermission,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateJsonSchema(GetPromptSchemaJson),
                AdditionalProperties = CreatePromptFunctionMetadata(serverConfig.Name, "get_prompt")
            });
    }

    private static Dictionary<string, object?> CreatePromptFunctionMetadata(string serverName, string operation)
    {
        return new Dictionary<string, object?>
        {
            ["SourceType"] = "MCP",
            ["MCPServerName"] = serverName,
            ["MCPPromptOperation"] = operation
        };
    }

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

    private static MCPPromptContentSummary SummarizePromptContent(ContentBlock content, ref int remainingChars)
    {
        switch (content)
        {
            case TextContentBlock text:
                return SummarizeTextPromptContent(text.Text, ref remainingChars);

            case ImageContentBlock image:
                return new MCPPromptContentSummary
                {
                    ContentType = "image",
                    MimeType = image.MimeType,
                    ByteLength = image.DecodedData.Length
                };

            case AudioContentBlock audio:
                return new MCPPromptContentSummary
                {
                    ContentType = "audio",
                    MimeType = audio.MimeType,
                    ByteLength = audio.DecodedData.Length
                };

            case EmbeddedResourceBlock embedded:
                return SummarizeEmbeddedResourcePromptContent(embedded.Resource, ref remainingChars);

            case ResourceLinkBlock link:
                return new MCPPromptContentSummary
                {
                    ContentType = "resource_link",
                    Uri = link.Uri,
                    Name = link.Name,
                    Title = link.Title,
                    Description = link.Description,
                    MimeType = link.MimeType
                };

            default:
                return new MCPPromptContentSummary
                {
                    ContentType = content.Type
                };
        }
    }

    private static MCPPromptContentSummary SummarizeTextPromptContent(string text, ref int remainingChars)
    {
        var safeText = text ?? string.Empty;
        var truncated = safeText.Length > remainingChars;
        var emittedText = truncated ? safeText[..Math.Max(remainingChars, 0)] : safeText;
        remainingChars -= emittedText.Length;

        return new MCPPromptContentSummary
        {
            ContentType = "text",
            Text = emittedText,
            Truncated = truncated
        };
    }

    private static MCPPromptContentSummary SummarizeEmbeddedResourcePromptContent(
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
                return new MCPPromptContentSummary
                {
                    ContentType = "resource_blob",
                    Uri = blob.Uri,
                    MimeType = blob.MimeType,
                    ByteLength = blob.DecodedData.Length
                };

            default:
                return new MCPPromptContentSummary
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

    internal static async Task ResolveServerSecretsAsync(
        MCPServerConfig config,
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
            await ResolveOAuthSecretsAsync(config.Name, config.OAuth, secretResolver, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ResolveOAuthSecretsAsync(
        string serverName,
        MCPOAuthConfig oauth,
        ISecretResolver secretResolver,
        CancellationToken cancellationToken)
    {
        if (oauth.ClientSecret == null && !string.IsNullOrWhiteSpace(oauth.ClientSecretKey))
        {
            oauth.ClientSecret = await ResolveRequiredSecretAsync(
                secretResolver,
                oauth.ClientSecretKey,
                $"MCP server '{serverName}' OAuth client secret",
                cancellationToken).ConfigureAwait(false);
        }

        var dynamicRegistration = oauth.DynamicClientRegistration;
        if (dynamicRegistration?.InitialAccessToken == null &&
            !string.IsNullOrWhiteSpace(dynamicRegistration?.InitialAccessTokenKey))
        {
            dynamicRegistration.InitialAccessToken = await ResolveRequiredSecretAsync(
                secretResolver,
                dynamicRegistration.InitialAccessTokenKey,
                $"MCP server '{serverName}' OAuth dynamic client registration initial access token",
                cancellationToken).ConfigureAwait(false);
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
    private async Task<McpClient> GetOrCreateClientAsync(MCPServerConfig serverConfig, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(serverConfig.Name, out var existingClient))
        {
            return existingClient;
        }

        _logger.LogDebug("Creating new MCP client for server '{ServerName}'", serverConfig.Name);

        var transport = CreateTransport(serverConfig);
        var clientOptions = CreateClientOptions(serverConfig);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(serverConfig.ConnectionTimeoutMs));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var client = await McpClient.CreateAsync(transport, clientOptions, cancellationToken: combinedCts.Token);

        if (string.IsNullOrWhiteSpace(serverConfig.SystemPrompt) && !string.IsNullOrWhiteSpace(client.ServerInstructions))
        {
            serverConfig.SystemPrompt = client.ServerInstructions;
        }

        if (string.IsNullOrWhiteSpace(serverConfig.Description))
        {
            serverConfig.Description = CreateDescriptionFromServerInfo(client);
        }

        _clients[serverConfig.Name] = client;

        _logger.LogDebug("Successfully created MCP client for server '{ServerName}' using {Transport} transport",
            serverConfig.Name, serverConfig.Transport);
        return client;
    }

    private IClientTransport CreateTransport(MCPServerConfig serverConfig)
    {
        if (serverConfig.IsStdioTransport())
        {
            return CreateStdioTransport(serverConfig);
        }

        if (serverConfig.IsHttpTransport())
        {
            return CreateHttpTransport(serverConfig);
        }

        throw new InvalidOperationException($"Unsupported MCP transport '{serverConfig.Transport}' for server '{serverConfig.Name}'.");
    }

    private IClientTransport CreateStdioTransport(MCPServerConfig serverConfig)
    {
        if (serverConfig.ProcessIsolation?.Mode is ProcessIsolationMode.Isolated)
        {
            if (_options.ProcessProvider is null)
            {
                throw new InvalidOperationException(
                    $"MCP server '{serverConfig.Name}' requested process isolation, but MCPOptions.ProcessProvider was not configured.");
            }

            return new McpProcessClientTransport(
                serverConfig,
                _options.ProcessProvider,
                CreateProcessEnvironmentVariables(serverConfig));
        }

        return CreateSdkStdioTransport(serverConfig);
    }

    private static StdioClientTransport CreateSdkStdioTransport(MCPServerConfig serverConfig)
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
            ShutdownTimeout = TimeSpan.FromMilliseconds(serverConfig.ShutdownTimeoutMs)
        };

        return new StdioClientTransport(transportOptions);
    }

    private static IReadOnlyDictionary<string, string?> CreateProcessEnvironmentVariables(MCPServerConfig serverConfig)
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

    private static IDictionary<string, string?>? CreateEnvironmentVariables(MCPServerConfig serverConfig)
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

    private HttpClientTransport CreateHttpTransport(MCPServerConfig serverConfig)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Name = serverConfig.Name,
            Endpoint = new Uri(serverConfig.Endpoint!, UriKind.Absolute),
            ConnectionTimeout = TimeSpan.FromMilliseconds(serverConfig.ConnectionTimeoutMs),
            AdditionalHeaders = serverConfig.Headers,
            KnownSessionId = serverConfig.KnownSessionId,
            OwnsSession = serverConfig.OwnsSession,
            OAuth = CreateOAuthOptions(serverConfig)
        };

        if (!string.IsNullOrWhiteSpace(serverConfig.HttpTransportMode))
        {
            transportOptions.TransportMode = ParseHttpTransportMode(serverConfig.HttpTransportMode);
        }

        return new HttpClientTransport(transportOptions);
    }

    private ClientOAuthOptions? CreateOAuthOptions(MCPServerConfig serverConfig)
    {
        var config = serverConfig.OAuth;
        if (config == null)
        {
            return null;
        }

        var runtime = _options.OAuthRuntime;
        var clientRegistration = runtime?.GetClientRegistration(serverConfig);
        var options = new ClientOAuthOptions
        {
            RedirectUri = new Uri(config.RedirectUri, UriKind.Absolute),
            ClientId = config.ClientId ?? clientRegistration?.ClientId,
            ClientSecret = config.ClientSecret ?? clientRegistration?.ClientSecret,
            ClientMetadataDocumentUri = string.IsNullOrWhiteSpace(config.ClientMetadataDocumentUri)
                ? null
                : new Uri(config.ClientMetadataDocumentUri, UriKind.Absolute),
            Scopes = config.Scopes,
            DynamicClientRegistration = CreateDynamicClientRegistrationOptions(
                serverConfig,
                config.DynamicClientRegistration,
                runtime)
        };

        options.AuthorizationRedirectDelegate = runtime?.CreateAuthorizationRedirectDelegate(serverConfig);
        options.TokenCache = runtime?.CreateTokenCache(serverConfig);
        options.AuthServerSelector = runtime?.CreateAuthServerSelector(serverConfig);
        options.ScopeSelector = runtime?.CreateScopeSelector(serverConfig);

        if (config.AdditionalAuthorizationParameters is { Count: > 0 })
        {
            foreach (var (key, value) in config.AdditionalAuthorizationParameters)
            {
                options.AdditionalAuthorizationParameters[key] = value;
            }
        }

        return options;
    }

    private static DynamicClientRegistrationOptions? CreateDynamicClientRegistrationOptions(
        MCPServerConfig serverConfig,
        MCPDynamicClientRegistrationConfig? config,
        IMcpOAuthRuntime? runtime)
    {
        var responseDelegate = runtime?.CreateDynamicClientRegistrationResponseDelegate(serverConfig);
        if (config == null && responseDelegate == null)
        {
            return null;
        }

        return new DynamicClientRegistrationOptions
        {
            ClientName = config?.ClientName,
            ClientUri = string.IsNullOrWhiteSpace(config?.ClientUri)
                ? null
                : new Uri(config.ClientUri, UriKind.Absolute),
            InitialAccessToken = config?.InitialAccessToken,
            ResponseDelegate = responseDelegate
        };
    }

    private static HttpTransportMode ParseHttpTransportMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "autodetect" or "auto" => HttpTransportMode.AutoDetect,
            "streamablehttp" or "streamable-http" or "http" => HttpTransportMode.StreamableHttp,
            "sse" => HttpTransportMode.Sse,
            _ => throw new ArgumentException($"Unsupported MCP HTTP transport mode '{value}'.", nameof(value))
        };
    }

    private static McpClientOptions CreateClientOptions(MCPServerConfig serverConfig)
    {
        var options = new McpClientOptions
        {
            ProtocolVersion = serverConfig.ProtocolVersion,
            InitializationTimeout = TimeSpan.FromMilliseconds(serverConfig.InitializationTimeoutMs)
        };

        if (!string.IsNullOrWhiteSpace(serverConfig.ClientName))
        {
            options.ClientInfo = new Implementation
            {
                Name = serverConfig.ClientName,
                Version = string.IsNullOrWhiteSpace(serverConfig.ClientVersion) ? "1.0.0" : serverConfig.ClientVersion
            };
        }

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
    /// Loads tools from an MCP server defined via [MCPServer] attribute in a toolharness.
    /// Handles both flat and nested collapsing modes based on config.CollapseWithinToolHarness.
    /// </summary>
    /// <param name="config">Server config with ParentToolHarness and CollapseWithinToolHarness set</param>
    /// <param name="maxFunctionNamesInDescription">Max function names to show in container descriptions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of AIFunctions (flat tools or container + collapsed tools)</returns>
    public async Task<List<AIFunction>> LoadToolsForToolHarnessAsync(
        MCPServerConfig config,
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
            var (container, collapsedTools) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
                serverName: config.Name,
                tools: functions,
                maxFunctionNamesInDescription: maxFunctionNamesInDescription,
                FunctionResult: config.FunctionResult,
                SystemPrompt: config.SystemPrompt,
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

    /// <summary>
    /// Registers live-update handlers for already-loaded MCP servers.
    /// </summary>
    public IDisposable AttachLiveUpdates(IEventCoordinator eventCoordinator)
    {
        ArgumentNullException.ThrowIfNull(eventCoordinator);

        var subscriptions = new List<IAsyncDisposable>();
        var attachedServers = new List<string>();
        foreach (var (serverName, config) in _serverConfigs)
        {
            if (!config.EnableLiveUpdates)
            {
                continue;
            }

            if (!_clients.TryGetValue(serverName, out var client))
            {
                continue;
            }

            try
            {
                AttachListChangedHandlers(config, client, eventCoordinator, subscriptions);
                AttachResourceUpdateSubscriptionsAsync(config, client, eventCoordinator, subscriptions)
                    .GetAwaiter()
                    .GetResult();

                eventCoordinator.Emit(new McpLiveUpdatesStartedEvent
                {
                    ServerName = config.Name,
                    ObservedAt = DateTimeOffset.UtcNow,
                    Subscriptions = DescribeLiveUpdateSubscriptions(config, client)
                });
                attachedServers.Add(config.Name);
            }
            catch (Exception ex)
            {
                EmitLiveUpdateError(eventCoordinator, config.Name, ex);
                if (_options.FailOnLiveUpdateError)
                {
                    throw new InvalidOperationException($"Failed to attach MCP live updates for server '{config.Name}'", ex);
                }
            }
        }

        return new McpLiveUpdateSubscription(this, eventCoordinator, subscriptions, attachedServers);
    }

    private void AttachListChangedHandlers(
        MCPServerConfig config,
        McpClient client,
        IEventCoordinator eventCoordinator,
        List<IAsyncDisposable> subscriptions)
    {
        if (client.ServerCapabilities.Tools?.ListChanged == true)
        {
            subscriptions.Add(client.RegisterNotificationHandler(
                NotificationMethods.ToolListChangedNotification,
                (_, _) =>
                {
                    eventCoordinator.Emit(new McpServerToolsChangedEvent
                    {
                        ServerName = config.Name,
                        ObservedAt = DateTimeOffset.UtcNow
                    });
                    return ValueTask.CompletedTask;
                }));
        }

        if (client.ServerCapabilities.Prompts?.ListChanged == true)
        {
            subscriptions.Add(client.RegisterNotificationHandler(
                NotificationMethods.PromptListChangedNotification,
                (_, _) =>
                {
                    eventCoordinator.Emit(new McpServerPromptsChangedEvent
                    {
                        ServerName = config.Name,
                        ObservedAt = DateTimeOffset.UtcNow
                    });
                    return ValueTask.CompletedTask;
                }));
        }

        if (client.ServerCapabilities.Resources?.ListChanged == true)
        {
            subscriptions.Add(client.RegisterNotificationHandler(
                NotificationMethods.ResourceListChangedNotification,
                (_, _) =>
                {
                    eventCoordinator.Emit(new McpServerResourcesChangedEvent
                    {
                        ServerName = config.Name,
                        ObservedAt = DateTimeOffset.UtcNow
                    });
                    return ValueTask.CompletedTask;
                }));
        }
    }

    private async Task AttachResourceUpdateSubscriptionsAsync(
        MCPServerConfig config,
        McpClient client,
        IEventCoordinator eventCoordinator,
        List<IAsyncDisposable> subscriptions)
    {
        if (config.ResourceSubscriptions.Count == 0)
        {
            return;
        }

        if (client.ServerCapabilities.Resources?.Subscribe != true)
        {
            throw new InvalidOperationException(
                $"MCP server '{config.Name}' does not advertise resource subscription support.");
        }

        foreach (var uri in config.ResourceSubscriptions)
        {
            var subscription = await client.SubscribeToResourceAsync(
                uri,
                (notification, _) =>
                {
                    eventCoordinator.Emit(new McpResourceUpdatedEvent
                    {
                        ServerName = config.Name,
                        ObservedAt = DateTimeOffset.UtcNow,
                        Uri = notification.Uri
                    });
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);

            subscriptions.Add(subscription);
        }
    }

    private static IReadOnlyList<McpLiveUpdateKind> DescribeLiveUpdateSubscriptions(MCPServerConfig config, McpClient client)
    {
        var subscriptions = new List<McpLiveUpdateKind>();
        if (client.ServerCapabilities.Tools?.ListChanged == true)
            subscriptions.Add(McpLiveUpdateKind.ToolsChanged);
        if (client.ServerCapabilities.Prompts?.ListChanged == true)
            subscriptions.Add(McpLiveUpdateKind.PromptsChanged);
        if (client.ServerCapabilities.Resources?.ListChanged == true)
            subscriptions.Add(McpLiveUpdateKind.ResourcesChanged);
        if (config.ResourceSubscriptions.Count > 0)
            subscriptions.Add(McpLiveUpdateKind.ResourceUpdated);

        return subscriptions;
    }

    private void EmitLiveUpdateError(IEventCoordinator eventCoordinator, string serverName, Exception exception)
    {
        _logger.LogWarning(exception, "MCP live updates failed for server '{ServerName}': {Error}", serverName, exception.Message);
        eventCoordinator.Emit(new McpLiveUpdatesErrorEvent
        {
            ServerName = serverName,
            ObservedAt = DateTimeOffset.UtcNow,
            ErrorMessage = exception.Message,
            Exception = exception
        });
    }

    private sealed class McpLiveUpdateSubscription(
        MCPClientManager manager,
        IEventCoordinator eventCoordinator,
        List<IAsyncDisposable> subscriptions,
        List<string> attachedServers) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var subscription in subscriptions)
            {
                try
                {
                    subscription.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    manager._logger.LogWarning(ex, "Failed to dispose MCP live update subscription: {Error}", ex.Message);
                }
            }

            foreach (var serverName in attachedServers)
            {
                eventCoordinator.Emit(new McpLiveUpdatesStoppedEvent
                {
                    ServerName = serverName,
                    ObservedAt = DateTimeOffset.UtcNow
                });
            }
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
            _serverConfigs.Clear();
        }
        
        _disposed = true;
    }
}
