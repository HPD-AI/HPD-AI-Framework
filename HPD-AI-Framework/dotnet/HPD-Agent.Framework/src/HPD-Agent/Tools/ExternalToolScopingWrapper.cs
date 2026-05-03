using Microsoft.Extensions.AI;
using System.Text.Json;
using HPD.Agent;

/// <summary>
/// Wraps external tools (MCP, Client) with Harness Collapsing metadata at runtime.
/// Unlike C# Harneses which get metadata from the source generator, external tools
/// need runtime wrapping to support the Harness Collapsing architecture.
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
            async (arguments, cancellationToken) =>
            {
                return returnMessage;
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = containerName,
                Description = description,
                RequiresPermission = false, // Container expansion doesn't need permission
                Validator = _ => new List<ValidationError>(), // No validation needed
                SchemaProvider = () => CreateEmptyContainerSchema(),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["HarnessName"] = containerName,
                    ["ParentContainer"] = parentContainer, // null for standalone WithMCP(), harness name for [MCPServer]
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
            async (arguments, cancellationToken) =>
            {
                return returnMessage;
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = containerName,
                Description = description,
                RequiresPermission = false, // Container expansion doesn't need permission
                Validator = _ => new List<ValidationError>(),
                SchemaProvider = () => CreateEmptyContainerSchema(),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["HarnessName"] = containerName,
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
    /// Wraps a group of Client tools from one Harness with a container function.
    /// Uses "Client_" prefix to distinguish from other Harness types.
    /// Requires a description (since collapsed Harneses need to tell the LLM what they contain).
    /// </summary>
    /// <param name="toolName">Name of the Client Harness (e.g., "ECommerce", "Settings")</param>
    /// <param name="description">Description of the Harness (REQUIRED - tells LLM when to expand)</param>
    /// <param name="tools">Tools in this Harness</param>
    /// <param name="maxFunctionNamesInDescription">Maximum number of function names to include in description (default: 10)</param>
    /// <param name="FunctionResult">Ephemeral instructions returned in function result after expansion</param>
    /// <param name="SystemPrompt">Persistent instructions injected into system prompt after expansion</param>
    /// <returns>Container function and Collapsed tools with metadata</returns>
    public static (AIFunction container, List<AIFunction> CollapsedTools) WrapclientHarness(
        string toolName,
        string description,
        List<AIFunction> tools,
        int maxFunctionNamesInDescription = 10,
        string? FunctionResult = null,
        string?SystemPrompt = null)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Harness name cannot be null or empty", nameof(toolName));

        if (string.IsNullOrEmpty(description))
            throw new ArgumentException(
                "Description is required for Client Harneses so the LLM knows when to expand them",
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
        var returnMessage = $"{toolName} Harness expanded. Available functions: {fullFunctionList}";
        if (!string.IsNullOrEmpty(FunctionResult))
        {
            returnMessage += $"\n\n{FunctionResult}";
        }

        // Create container function
        var container = HPDAIFunctionFactory.Create(
            async (arguments, cancellationToken) =>
            {
                return returnMessage;
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = containerName,
                Description = fullDescription,
                RequiresPermission = false, // Container expansion doesn't need permission
                Validator = _ => new List<ValidationError>(), // No validation needed
                SchemaProvider = () => CreateEmptyContainerSchema(),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["HarnessName"] = containerName,
                    ["clientHarnessName"] = toolName, // Original name without prefix
                    ["FunctionNames"] = allFunctionNames.ToArray(),
                    ["FunctionCount"] = allFunctionNames.Count,
                    ["SourceType"] = "clientHarness",
                    // Dual-context architecture: FunctionResult for ephemeral, SystemPrompt for persistent
                    ["FunctionResult"] = FunctionResult,
                    ["SystemPrompt"] = SystemPrompt,
                    // Legacy key for backward compatibility with ContainerMiddleware
                    ["Instructions"] = SystemPrompt
                }
            });

        // Add metadata to individual tools
        var CollapsedTools = tools.Select(tool => AddParentToolMetadata(tool, containerName, "clientHarness")).ToList();

        return (container, CollapsedTools);
    }

    /// <summary>
    /// Wraps OpenAPI functions behind a container nested inside a parent harness.
    /// Used when <c>CollapseWithinHarness = true</c> on <c>OpenApiConfig</c>.
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
            (_, _) => Task.FromResult<object?>(returnMessage),
            new HPDAIFunctionFactoryOptions
            {
                Name = containerName,
                Description = description,
                RequiresPermission = false,
                Validator = _ => new List<ValidationError>(),
                SchemaProvider = () => CreateEmptyContainerSchema(),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["HarnessName"] = containerName,
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
    /// Adds ParentHarness metadata to an existing AIFunction by wrapping it.
    /// This is necessary because AIFunction.AdditionalProperties is read-only,
    /// so we create a new function that delegates to the original.
    /// </summary>
    /// <param name="tool">Original tool to wrap</param>
    /// <param name="parentHarnessName">Parent container name</param>
    /// <param name="sourceType">Source type (MCP, Client, clientHarness)</param>
    /// <param name="parentContainer">Optional parent container for nested visibility (e.g., parent harness name for flat MCP tools)</param>
    /// <returns>New AIFunction with metadata</returns>
    internal static AIFunction AddParentToolMetadata(AIFunction tool, string parentHarnessName, string sourceType, string? parentContainer = null)
    {
        // Check if tool already has Collapsing metadata (avoid double-wrapping)
        if (tool.AdditionalProperties?.ContainsKey("ParentHarness") == true)
        {
            return tool;
        }

        // Wrap the existing tool with metadata
        // This delegates invocation to the original tool while adding metadata
        return HPDAIFunctionFactory.Create(
            async (args, ct) => await tool.InvokeAsync(args, ct),
            new HPDAIFunctionFactoryOptions
            {
                Name = tool.Name,
                Description = tool.Description,
                SchemaProvider = () => tool.JsonSchema,
                RequiresPermission = true, // Preserve permission requirement from original tool
                Validator = _ => new List<ValidationError>(), // Original tool handles validation
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["ParentHarness"] = parentHarnessName,
                    ["ParentContainer"] = parentContainer, // For nested visibility (flat MCP tools under a harness)
                    ["HarnessName"] = parentHarnessName,
                    ["IsContainer"] = false,
                    ["SourceType"] = sourceType
                }
            });
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
