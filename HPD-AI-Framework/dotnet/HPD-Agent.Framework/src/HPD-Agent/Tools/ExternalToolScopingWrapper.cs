using Microsoft.Extensions.AI;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Middleware;

/// <summary>
/// Wraps external tools (MCP, Client) with ToolHarness Collapsing metadata at runtime.
/// Unlike C# ToolHarnesses which get metadata from the source generator, external tools
/// need runtime wrapping to support the ToolHarness Collapsing architecture.
/// </summary>
public static class ExternalToolCollapsingWrapper
{
    /// <summary>
    /// Wraps a group of MCP tools from one server with a container function.
    /// Creates a template description listing the available function names.
    /// </summary>
    /// <param name="serverName">Name of the MCP server (e.g., "filesystem", "github")</param>
    /// <param name="tools">Tools from this MCP server</param>
    /// <param name="maxFunctionNamesInDescription">Maximum number of function names to include in description (default: 10)</param>
    /// <param name="FunctionResult">Ephemeral instructions returned in function result after expansion</param>
    /// <param name="SystemPrompt">Persistent instructions injected into system prompt after expansion</param>
    /// <param name="customDescription">Optional custom description from JSON config. If provided, replaces auto-generated description.</param>
    /// <returns>Container function and Collapsed tools with metadata</returns>
    public static (AIFunction container, List<AIFunction> CollapsedTools) WrapMCPServerTools(
        string serverName,
        List<AIFunction> tools,
        int maxFunctionNamesInDescription = 10,
        string? FunctionResult = null,
        string?SystemPrompt = null,
        string? customDescription = null,
        string? parentContainer = null)
    {
        if (string.IsNullOrEmpty(serverName))
            throw new ArgumentException("Server name cannot be null or empty", nameof(serverName));

        if (tools == null || tools.Count == 0)
            throw new ArgumentException("Tools list cannot be null or empty", nameof(tools));

        var containerName = $"MCP_{serverName}";
        var allFunctionNames = tools.Select(t => t.Name).ToList();

        // Build function list suffix (used for all description types)
        var displayedNames = allFunctionNames.Take(maxFunctionNamesInDescription);
        var functionNamesList = string.Join(", ", displayedNames);
        var moreCount = allFunctionNames.Count > maxFunctionNamesInDescription
            ? $" and {allFunctionNames.Count - maxFunctionNamesInDescription} more"
            : "";
        var functionSuffix = $"Contains {allFunctionNames.Count} functions: {functionNamesList}{moreCount}";

        // Build description: user-provided or auto-generated, always with function list
        string description;
        if (!string.IsNullOrWhiteSpace(customDescription))
        {
            // User-provided description from JSON config + function list
            description = $"{customDescription}. {functionSuffix}";
        }
        else
        {
            // Auto-generated description with function list
            description = $"MCP Server '{serverName}'. {functionSuffix}";
        }

        var fullFunctionList = string.Join(", ", allFunctionNames);

        // Build return message with optional ephemeral context (function result)
        var returnMessage = $"{serverName} server expanded. Available functions: {fullFunctionList}";
        if (!string.IsNullOrEmpty(FunctionResult))
        {
            returnMessage += $"\n\n{FunctionResult}";
        }

        // Create container function
        var container = HPDAIFunctionFactory.Create(
            async (arguments, _, cancellationToken) =>
            {
                return returnMessage;
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = containerName,
                Description = description,
                RequiresPermission = false, // Container expansion doesn't need permission
                Validator = (_, _) => new List<ValidationError>(), // No validation needed
                SchemaProvider = () => CreateEmptyContainerSchema(),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["ToolHarnessName"] = containerName,
                    ["ParentContainer"] = parentContainer, // null for standalone WithMCP(), toolharness name for [MCPServer]
                    ["FunctionNames"] = allFunctionNames.ToArray(),
                    ["FunctionCount"] = allFunctionNames.Count,
                    ["SourceType"] = "MCP",
                    ["MCPServerName"] = serverName,
                    // Dual-context architecture: FunctionResult for ephemeral, SystemPrompt for persistent
                    ["FunctionResult"] = FunctionResult,
                    ["SystemPrompt"] = SystemPrompt,
                    // Legacy key for backward compatibility with ContainerMiddleware
                    ["Instructions"] = SystemPrompt
                }
            });

        // Add metadata to individual tools
        var CollapsedTools = tools.Select(tool => AddParentToolMetadata(tool, containerName, "MCP")).ToList();

        return (container, CollapsedTools);
    }

    /// <summary>
    /// Wraps all Client tools in a single container function.
    /// Client tools are AGUI tools executed by the Client (human-in-the-loop).
    /// </summary>
    /// <param name="tools">Client tools to wrap</param>
    /// <param name="maxFunctionNamesInDescription">Maximum number of function names to include in description (default: 10)</param>
    /// <param name="FunctionResult">Ephemeral instructions returned in function result after expansion</param>
    /// <param name="SystemPrompt">Persistent instructions injected into system prompt after expansion</param>
    /// <returns>Container function and Collapsed tools with metadata</returns>
    public static (AIFunction container, List<AIFunction> CollapsedTools) WrapClientTools(
        List<AIFunction> tools,
        int maxFunctionNamesInDescription = 10,
        string? FunctionResult = null,
        string?SystemPrompt = null)
    {
        if (tools == null || tools.Count == 0)
            throw new ArgumentException("Tools list cannot be null or empty", nameof(tools));

        var containerName = "ClientTools";
        var allFunctionNames = tools.Select(t => t.Name).ToList();

        // Generate template description with function names
        var displayedNames = allFunctionNames.Take(maxFunctionNamesInDescription);
        var functionNamesList = string.Join(", ", displayedNames);
        var moreCount = allFunctionNames.Count > maxFunctionNamesInDescription
            ? $" and {allFunctionNames.Count - maxFunctionNamesInDescription} more"
            : "";

        var description = $"Client UI tools for user interaction. Contains {allFunctionNames.Count} functions: {functionNamesList}{moreCount}";
        var fullFunctionList = string.Join(", ", allFunctionNames);

        // Build return message with optional ephemeral context (function result)
        var returnMessage = $"Client tools expanded. Available functions: {fullFunctionList}";
        if (!string.IsNullOrEmpty(FunctionResult))
        {
            returnMessage += $"\n\n{FunctionResult}";
        }

        // Create container function
        var container = HPDAIFunctionFactory.Create(
            async (arguments, _, cancellationToken) =>
            {
                return returnMessage;
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = containerName,
                Description = description,
                RequiresPermission = false, // Container expansion doesn't need permission
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateEmptyContainerSchema(),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["ToolHarnessName"] = containerName,
                    ["FunctionNames"] = allFunctionNames.ToArray(),
                    ["FunctionCount"] = allFunctionNames.Count,
                    ["SourceType"] = "Client"
                }
            });

        // Add metadata to individual tools
        var CollapsedTools = tools.Select(tool => AddParentToolMetadata(tool, containerName, "Client")).ToList();

        return (container, CollapsedTools);
    }

    /// <summary>
    /// Wraps a group of Client tools from one ToolHarness with a container function.
    /// Uses "Client_" prefix to distinguish from other ToolHarness types.
    /// Requires a description (since collapsed ToolHarnesses need to tell the LLM what they contain).
    /// </summary>
    /// <param name="toolName">Name of the Client ToolHarness (e.g., "ECommerce", "Settings")</param>
    /// <param name="description">Description of the ToolHarness (REQUIRED - tells LLM when to expand)</param>
    /// <param name="tools">Tools in this ToolHarness</param>
    /// <param name="maxFunctionNamesInDescription">Maximum number of function names to include in description (default: 10)</param>
    /// <param name="FunctionResult">Ephemeral instructions returned in function result after expansion</param>
    /// <param name="SystemPrompt">Persistent instructions injected into system prompt after expansion</param>
    /// <returns>Container function and Collapsed tools with metadata</returns>
    public static (AIFunction container, List<AIFunction> CollapsedTools) WrapclientToolHarness(
        string toolName,
        string description,
        List<AIFunction> tools,
        int maxFunctionNamesInDescription = 10,
        string? FunctionResult = null,
        string?SystemPrompt = null)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("ToolHarness name cannot be null or empty", nameof(toolName));

        if (string.IsNullOrEmpty(description))
            throw new ArgumentException(
                "Description is required for Client ToolHarnesses so the LLM knows when to expand them",
                nameof(description));

        if (tools == null || tools.Count == 0)
            throw new ArgumentException("Tools list cannot be null or empty", nameof(tools));

        var containerName = $"Client_{toolName}";
        var allFunctionNames = tools.Select(t => t.Name).ToList();

        // Build function list suffix
        var displayedNames = allFunctionNames.Take(maxFunctionNamesInDescription);
        var functionNamesList = string.Join(", ", displayedNames);
        var moreCount = allFunctionNames.Count > maxFunctionNamesInDescription
            ? $" and {allFunctionNames.Count - maxFunctionNamesInDescription} more"
            : "";
        var functionSuffix = $"Contains {allFunctionNames.Count} functions: {functionNamesList}{moreCount}";

        // Build description: user-provided + function list
        var fullDescription = $"{description}. {functionSuffix}";
        var fullFunctionList = string.Join(", ", allFunctionNames);

        // Build return message with optional ephemeral context (function result)
        var returnMessage = $"{toolName} ToolHarness expanded. Available functions: {fullFunctionList}";
        if (!string.IsNullOrEmpty(FunctionResult))
        {
            returnMessage += $"\n\n{FunctionResult}";
        }

        // Create container function
        var container = HPDAIFunctionFactory.Create(
            async (arguments, _, cancellationToken) =>
            {
                return returnMessage;
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = containerName,
                Description = fullDescription,
                RequiresPermission = false, // Container expansion doesn't need permission
                Validator = (_, _) => new List<ValidationError>(), // No validation needed
                SchemaProvider = () => CreateEmptyContainerSchema(),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["ToolHarnessName"] = containerName,
                    ["clientToolHarnessName"] = toolName, // Original name without prefix
                    ["FunctionNames"] = allFunctionNames.ToArray(),
                    ["FunctionCount"] = allFunctionNames.Count,
                    ["SourceType"] = "clientToolHarness",
                    // Dual-context architecture: FunctionResult for ephemeral, SystemPrompt for persistent
                    ["FunctionResult"] = FunctionResult,
                    ["SystemPrompt"] = SystemPrompt,
                    // Legacy key for backward compatibility with ContainerMiddleware
                    ["Instructions"] = SystemPrompt
                }
            });

        // Add metadata to individual tools
        var CollapsedTools = tools.Select(tool => AddParentToolMetadata(tool, containerName, "clientToolHarness")).ToList();

        return (container, CollapsedTools);
    }

    /// <summary>
    /// Wraps OpenAPI functions behind a container nested inside a parent toolharness.
    /// Used when <c>CollapseWithinToolHarness = true</c> on <c>OpenApiConfig</c>.
    /// The container gets <c>["ParentContainer"] = parentContainer</c> so
    /// <c>IsCollapseContainerVisible()</c> enforces parent-first visibility.
    /// </summary>
    public static (AIFunction container, List<AIFunction> CollapsedTools) WrapOpenApiTools(
        string containerName,
        List<AIFunction> tools,
        int maxFunctionNamesInDescription = 10,
        string? functionResult = null,
        string? systemPrompt = null,
        string? customDescription = null,
        string? parentContainer = null)
    {
        if (string.IsNullOrEmpty(containerName))
            throw new ArgumentException("Container name cannot be null or empty", nameof(containerName));

        if (tools == null || tools.Count == 0)
            throw new ArgumentException("Tools list cannot be null or empty", nameof(tools));

        var allFunctionNames = tools.Select(t => t.Name).ToList();

        var displayedNames = allFunctionNames.Take(maxFunctionNamesInDescription);
        var functionNamesList = string.Join(", ", displayedNames);
        var moreCount = allFunctionNames.Count > maxFunctionNamesInDescription
            ? $" and {allFunctionNames.Count - maxFunctionNamesInDescription} more"
            : "";
        var functionSuffix = $"Contains {allFunctionNames.Count} operations: {functionNamesList}{moreCount}";

        string description;
        if (!string.IsNullOrWhiteSpace(customDescription))
            description = $"{customDescription}. {functionSuffix}";
        else
            description = $"OpenAPI source '{containerName}'. {functionSuffix}";

        var fullFunctionList = string.Join(", ", allFunctionNames);
        var returnMessage = $"{containerName} expanded. Available operations: {fullFunctionList}";
        if (!string.IsNullOrEmpty(functionResult))
            returnMessage += $"\n\n{functionResult}";

        var container = HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>(returnMessage),
            new HPDAIFunctionFactoryOptions
            {
                Name = containerName,
                Description = description,
                RequiresPermission = false,
                Validator = (_, _) => new List<ValidationError>(),
                SchemaProvider = () => CreateEmptyContainerSchema(),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["ToolHarnessName"] = containerName,
                    ["ParentContainer"] = parentContainer,
                    ["FunctionNames"] = allFunctionNames.ToArray(),
                    ["FunctionCount"] = allFunctionNames.Count,
                    ["SourceType"] = "OpenApi",
                    ["FunctionResult"] = functionResult,
                    ["SystemPrompt"] = systemPrompt
                }
            });

        var collapsedTools = tools
            .Select(tool => AddParentToolMetadata(tool, containerName, "OpenApi", parentContainer: null))
            .ToList();

        return (container, collapsedTools);
    }

    /// <summary>
    /// Adds ParentToolHarness metadata to an existing AIFunction by wrapping it.
    /// This is necessary because AIFunction.AdditionalProperties is read-only,
    /// so we create a new function that delegates to the original.
    /// </summary>
    /// <param name="tool">Original tool to wrap</param>
    /// <param name="parentToolHarnessName">Parent container name</param>
    /// <param name="sourceType">Source type (MCP, Client, clientToolHarness)</param>
    /// <param name="parentContainer">Optional parent container for nested visibility (e.g., parent toolharness name for flat MCP tools)</param>
    /// <returns>New AIFunction with metadata</returns>
    internal static AIFunction AddParentToolMetadata(AIFunction tool, string parentToolHarnessName, string sourceType, string? parentContainer = null)
    {
        // Check if tool already has Collapsing metadata (avoid double-wrapping)
        if (tool.AdditionalProperties?.ContainsKey("ParentToolHarness") == true)
        {
            return tool;
        }

        var additionalProperties = tool.AdditionalProperties?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, object?>();
        additionalProperties["ParentToolHarness"] = parentToolHarnessName;
        additionalProperties["ParentContainer"] = parentContainer; // For nested visibility (flat MCP tools under a toolharness)
        additionalProperties["ToolHarnessName"] = parentToolHarnessName;
        additionalProperties["IsContainer"] = false;
        additionalProperties["SourceType"] = sourceType;

        // Wrap the existing tool with metadata
        // This delegates invocation to the original tool while adding metadata
        return HPDAIFunctionFactory.Create(
            async (args, functionContext, ct) => await InvokeWrappedToolAsync(tool, args, functionContext, ct).ConfigureAwait(false),
            new HPDAIFunctionFactoryOptions
            {
                Name = tool.Name,
                Description = tool.Description,
                SchemaProvider = () => tool.JsonSchema,
                RequiresPermission = tool is HPDAIFunctionFactory.HPDAIFunction hpdFunction
                    ? hpdFunction.HPDOptions.RequiresPermission
                    : true,
                Validator = (_, _) => new List<ValidationError>(), // Original tool handles validation
                AdditionalProperties = additionalProperties
            });
    }

    private static async Task<object?> InvokeWrappedToolAsync(
        AIFunction tool,
        AIFunctionArguments args,
        FunctionExecutionContext functionContext,
        CancellationToken cancellationToken)
    {
        if (tool is HPDAIFunctionFactory.HPDAIFunction hpdFunction)
        {
            return await hpdFunction.InvokeAsync(args, functionContext, cancellationToken).ConfigureAwait(false);
        }

        return await tool.InvokeAsync(args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an empty JSON schema for container functions (no parameters).
    /// Container functions don't take arguments - they just trigger expansion.
    /// </summary>
    private static JsonElement CreateEmptyContainerSchema()
    {
        return global::Microsoft.Extensions.AI.AIJsonUtilities.CreateJsonSchema(
            null,
            serializerOptions: HPDJsonContext.Default.Options,
            inferenceOptions: new global::Microsoft.Extensions.AI.AIJsonSchemaCreateOptions
            {
                IncludeSchemaKeyword = false
            }
        );
    }
}
