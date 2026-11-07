using System.Text;
using System.Text.Json.Serialization;

namespace HPD.Agent.MCP;

/// <summary>Carries one stable MCP security configuration diagnostic.</summary>
internal sealed class McpPermissionConfigurationException : ArgumentException
{
    internal McpPermissionConfigurationException(string code, string path, string message)
        : base(message)
    {
        Code = code;
        Path = path;
    }

    internal string Code { get; }
    internal string Path { get; }

    internal McpPermissionConfigurationException AtServer(int index) =>
        new(Code, $"servers[{index}].{Path}", Message);
}

/// <summary>Declares construction-time permission policy for one MCP server and its tools.</summary>
public sealed class McpPermissionConfig
{
    private IReadOnlyDictionary<string, McpToolPermissionOverride> _tools =
        new Dictionary<string, McpToolPermissionOverride>(StringComparer.Ordinal);

    /// <summary>Gets or sets whether admitted tools require permission mediation.</summary>
    [JsonPropertyName("requiresPermission")]
    public bool RequiresPermission { get; set; } = true;

    /// <summary>Gets or sets the stable server authority base.</summary>
    [JsonPropertyName("authority")]
    public string? Authority { get; set; }

    /// <summary>Gets or sets the stable default policy descriptor ID.</summary>
    [JsonPropertyName("policy")]
    public string? Policy { get; set; }


    /// <summary>Gets or sets exact original MCP tool permission overrides.</summary>
    [JsonPropertyName("tools")]
    public IReadOnlyDictionary<string, McpToolPermissionOverride> Tools
    {
        get => _tools;
        set => _tools = new Dictionary<string, McpToolPermissionOverride>(
            value ?? new Dictionary<string, McpToolPermissionOverride>(),
            StringComparer.Ordinal);
    }

    internal void Validate()
    {
        ValidateOptionalId(Authority, nameof(Authority));
        ValidateOptionalId(Policy, nameof(Policy));
        if (!RequiresPermission && Policy is not null)
            throw new McpPermissionConfigurationException(
                "McpToolPermissionOverrideInvalid",
                "permission",
                "MCP permission cannot disable permission and declare a policy.");
        if (Tools is null)
            throw new McpPermissionConfigurationException(
                "McpToolPermissionOverrideInvalid",
                "permission.tools",
                "MCP permission tools must be a mapping when present.");
        foreach (var (toolName, tool) in Tools)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new McpPermissionConfigurationException(
                    "McpToolPermissionOverrideInvalid",
                    "permission.tools",
                    "MCP tool permission names cannot be blank.");
            if (tool is null)
                throw new McpPermissionConfigurationException(
                    "McpToolPermissionOverrideInvalid",
                    $"permission.tools.{toolName}",
                    $"MCP tool permission override '{toolName}' cannot be null.");
            if (!string.Equals(toolName, toolName.Trim(), StringComparison.Ordinal) ||
                toolName.Any(char.IsControl))
                throw new McpPermissionConfigurationException(
                    "McpToolPermissionOverrideInvalid",
                    $"permission.tools.{toolName}",
                    $"MCP tool permission key '{toolName}' must be canonical and control-free.");
            tool.Validate(toolName);
        }
    }

    private static void ValidateOptionalId(string? value, string name)
    {
        if (value is null)
            return;
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl) ||
            Encoding.UTF8.GetByteCount(value) > 512)
            throw new McpPermissionConfigurationException(
                name == nameof(Authority) ? "McpPermissionAuthorityInvalid" : "McpToolPermissionOverrideInvalid",
                $"permission.{name.ToLowerInvariant()}",
                $"MCP permission {name} must be canonical, control-free, and at most 512 UTF-8 bytes.");
    }
}

/// <summary>Overrides permission fields for one exact original MCP tool name.</summary>
public sealed record McpToolPermissionOverride
{
    /// <summary>Gets or sets whether this tool requires permission mediation.</summary>
    [JsonPropertyName("requiresPermission")]
    public bool? RequiresPermission { get; init; }

    /// <summary>Gets or sets the exact stable tool authority.</summary>
    [JsonPropertyName("authority")]
    public string? Authority { get; init; }

    /// <summary>Gets or sets the stable tool policy descriptor ID.</summary>
    [JsonPropertyName("policy")]
    public string? Policy { get; init; }


    internal void Validate(string toolName)
    {
        ValidateOptionalId(Authority, "authority", $"permission.tools.{toolName}");
        ValidateOptionalId(Policy, "policy", $"permission.tools.{toolName}");
        if (RequiresPermission == false && Policy is not null)
            throw new McpPermissionConfigurationException(
                "McpToolPermissionOverrideInvalid",
                $"permission.tools.{toolName}",
                $"MCP tool '{toolName}' cannot disable permission and declare a policy.");
    }

    private static void ValidateOptionalId(string? value, string name, string path)
    {
        if (value is null)
            return;
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl) ||
            Encoding.UTF8.GetByteCount(value) > 512)
            throw new McpPermissionConfigurationException(
                name == "authority" ? "McpPermissionAuthorityInvalid" : "McpToolPermissionOverrideInvalid",
                $"{path}.{name}",
                $"MCP permission {name} must be canonical, control-free, and at most 512 UTF-8 bytes.");
    }
}
