namespace HPD.Agent.MCP;

internal static class McpPermissionResolver
{
    internal static bool IsToolAllowed(McpServerConfig server, string toolName)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return server.AllowedTools is null ||
            server.AllowedTools.Contains(toolName, StringComparer.Ordinal);
    }

    internal static AIFunctionPermissionDeclaration? ResolveTool(
        McpServerConfig server,
        string originalToolName,
        PermissionDeclarationSource serverSource)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalToolName);
        if (server.Permission is not { } serverPermission)
            return null;
        serverPermission.Tools.TryGetValue(originalToolName, out var toolOverride);
        var requiresPermission = toolOverride?.RequiresPermission ??
            serverPermission.RequiresPermission;
        if (!requiresPermission)
            return null;
        var authority = toolOverride?.Authority ??
            $"{GetAuthorityBase(server)}/tool/{Uri.EscapeDataString(originalToolName)}";
        RequireCanonicalAuthority(authority, originalToolName);
        return new AIFunctionPermissionDeclaration
        {
            RequiresPermission = true,
            Authority = authority,
            PolicyDescriptorId = toolOverride?.Policy ?? serverPermission.Policy,
            Source = toolOverride is null
                ? serverSource
                : PermissionDeclarationSource.ToolOverride
        };
    }

    internal static AIFunctionPermissionDeclaration? ResolveSynthetic(
        McpServerConfig server,
        string operation,
        PermissionDeclarationSource serverSource)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (server.Permission is not { RequiresPermission: true } permission)
            return null;
        var authority = $"{GetAuthorityBase(server)}/{operation}";
        RequireCanonicalAuthority(authority, operation);
        return new AIFunctionPermissionDeclaration
        {
            RequiresPermission = true,
            Authority = authority,
            PolicyDescriptorId = permission.Policy,
            Source = serverSource
        };
    }

    private static string GetAuthorityBase(McpServerConfig server)
    {
        var authority = server.Permission?.Authority ?? $"mcp/{Uri.EscapeDataString(server.Name)}";
        return authority.TrimEnd('/');
    }

    private static void RequireCanonicalAuthority(string authority, string operation)
    {
        if (string.IsNullOrWhiteSpace(authority) ||
            !string.Equals(authority, authority.Trim(), StringComparison.Ordinal) ||
            authority.Any(char.IsControl) ||
            System.Text.Encoding.UTF8.GetByteCount(authority) > 512)
            throw new McpPermissionConfigurationException(
                "McpPermissionAuthorityInvalid",
                $"permission.tools.{operation}",
                $"MCP permission authority for '{operation}' must be canonical, control-free, and at most 512 UTF-8 bytes.");
    }
}
