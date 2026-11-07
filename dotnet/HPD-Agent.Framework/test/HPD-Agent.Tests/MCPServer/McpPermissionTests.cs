using HPD.Agent.MCP;
using HPD.Agent.Permissions;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpPermissionTests
{
    [Fact]
    public void Allowed_tools_distinguish_null_empty_and_exact_names()
    {
        var all = Server();
        Assert.True(McpPermissionResolver.IsToolAllowed(all, "search"));

        var none = Server(allowedTools: []);
        Assert.False(McpPermissionResolver.IsToolAllowed(none, "search"));

        var selected = Server(allowedTools: ["search"]);
        Assert.True(McpPermissionResolver.IsToolAllowed(selected, "search"));
        Assert.False(McpPermissionResolver.IsToolAllowed(selected, "Search"));
        Assert.False(McpPermissionResolver.IsToolAllowed(selected, "search_admin"));
    }

    [Fact]
    public void Tool_override_inherits_server_binding_and_replaces_only_declared_fields()
    {
        var server = Server(permission: new McpPermissionConfig
        {
            Authority = "mcp.github",
            Policy = "github.default",
            Tools = new Dictionary<string, McpToolPermissionOverride>(StringComparer.Ordinal)
            {
                ["search"] = new()
                {
                    Policy = "github.read-only"
                },
                ["delete_repository"] = new()
                {
                    Authority = "mcp.github/delete",
                    Policy = "github.destructive",
                }
            }
        });

        var search = Assert.IsType<AIFunctionPermissionDeclaration>(
            McpPermissionResolver.ResolveTool(
                server,
                "search",
                PermissionDeclarationSource.AgentConfig));
        Assert.Equal("mcp.github/tool/search", search.Authority);
        Assert.Equal("github.read-only", search.PolicyDescriptorId);
        Assert.Equal(PermissionDeclarationSource.ToolOverride, search.Source);

        var unknown = Assert.IsType<AIFunctionPermissionDeclaration>(
            McpPermissionResolver.ResolveTool(
                server,
                "new_tool",
                PermissionDeclarationSource.AgentConfig));
        Assert.Equal("mcp.github/tool/new_tool", unknown.Authority);
        Assert.Equal("github.default", unknown.PolicyDescriptorId);
        Assert.Equal(PermissionDeclarationSource.AgentConfig, unknown.Source);

        var delete = Assert.IsType<AIFunctionPermissionDeclaration>(
            McpPermissionResolver.ResolveTool(
                server,
                "delete_repository",
                PermissionDeclarationSource.AgentConfig));
        Assert.Equal("mcp.github/delete", delete.Authority);
        Assert.Equal("github.destructive", delete.PolicyDescriptorId);
    }

    [Fact]
    public void Tool_override_can_disable_one_tool_or_enable_one_under_server_default_false()
    {
        var server = Server(permission: new McpPermissionConfig
        {
            RequiresPermission = false,
            Tools = new Dictionary<string, McpToolPermissionOverride>(StringComparer.Ordinal)
            {
                ["delete_repository"] = new() { RequiresPermission = true }
            }
        });

        Assert.Null(McpPermissionResolver.ResolveTool(
            server,
            "search",
            PermissionDeclarationSource.FunctionAttribute));
        var delete = Assert.IsType<AIFunctionPermissionDeclaration>(
            McpPermissionResolver.ResolveTool(
                server,
                "delete_repository",
                PermissionDeclarationSource.FunctionAttribute));
        Assert.Equal("mcp/wire/tool/delete_repository", delete.Authority);

        var disabled = Server(permission: new McpPermissionConfig
        {
            Tools = new Dictionary<string, McpToolPermissionOverride>(StringComparer.Ordinal)
            {
                ["search"] = new() { RequiresPermission = false }
            }
        });
        Assert.Null(McpPermissionResolver.ResolveTool(
            disabled,
            "search",
            PermissionDeclarationSource.AgentConfig));
    }

    [Fact]
    public void Synthetic_operations_use_reserved_authorities_and_server_defaults()
    {
        var server = Server(permission: new McpPermissionConfig
        {
            Authority = "mcp/github/",
            Policy = "github.default",
            Tools = new Dictionary<string, McpToolPermissionOverride>(StringComparer.Ordinal)
            {
                ["read_resource"] = new() { Authority = "must/not/apply" }
            }
        });

        var read = Assert.IsType<AIFunctionPermissionDeclaration>(
            McpPermissionResolver.ResolveSynthetic(
                server,
                "resource/read",
                PermissionDeclarationSource.AgentConfig));
        Assert.Equal("mcp/github/resource/read", read.Authority);
        Assert.Equal("github.default", read.PolicyDescriptorId);

        var prompt = Assert.IsType<AIFunctionPermissionDeclaration>(
            McpPermissionResolver.ResolveSynthetic(
                server,
                "prompt/get",
                PermissionDeclarationSource.FunctionAttribute));
        Assert.Equal("mcp/github/prompt/get", prompt.Authority);
        Assert.Equal(PermissionDeclarationSource.FunctionAttribute, prompt.Source);
    }

    [Fact]
    public void Validation_rejects_duplicate_allowlist_and_inactive_descriptors()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Server(allowedTools: ["search", "search"]).Validate());
        Assert.ThrowsAny<ArgumentException>(() =>
            Server(allowedTools: [" search"]).Validate());
        Assert.ThrowsAny<ArgumentException>(() =>
            Server(permission: new McpPermissionConfig
            {
                RequiresPermission = false,
                Policy = "inactive"
            }).Validate());
        Assert.ThrowsAny<ArgumentException>(() =>
            Server(permission: new McpPermissionConfig
            {
                Tools = new Dictionary<string, McpToolPermissionOverride>(StringComparer.Ordinal)
                {
                    ["search"] = new()
                    {
                        RequiresPermission = false,
                        Policy = "inactive"
                    }
                }
            }).Validate());
    }

    private static McpServerConfig Server(
        IReadOnlyList<string>? allowedTools = null,
        McpPermissionConfig? permission = null) => new()
    {
        Name = "wire",
        Transport = "http",
        Endpoint = new Uri("https://wire.test/mcp"),
        AllowedTools = allowedTools,
        Permission = permission
    };
}
