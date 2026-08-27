using FluentAssertions;
using HPD.Agent.MCP;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpInvocationContextScopeTests
{
    [Fact]
    public async Task ConcurrentScopes_DoNotLeakAcrossExecutionContexts()
    {
        static async Task<string?> ObserveAsync(string toolName)
        {
            using var scope = McpInvocationContextScope.Push("server", toolName, null);
            await Task.Yield();
            return McpInvocationContextScope.Current?.ToolName;
        }

        var observed = await Task.WhenAll(ObserveAsync("first"), ObserveAsync("second"));

        observed.Should().Equal("first", "second");
        McpInvocationContextScope.Current.Should().BeNull();
    }

    [Fact]
    public void NestedScopes_RequireStackOrderedDisposal()
    {
        var outer = McpInvocationContextScope.Push("server", "outer", null);
        var inner = McpInvocationContextScope.Push("server", "inner", null);

        var action = () => outer.Dispose();

        action.Should().Throw<InvalidOperationException>();
        inner.Dispose();
        outer.Dispose();
    }
}
