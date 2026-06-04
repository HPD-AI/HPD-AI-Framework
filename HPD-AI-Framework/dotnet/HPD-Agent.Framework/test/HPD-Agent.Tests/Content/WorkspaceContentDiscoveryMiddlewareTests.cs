using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using static HPD.Agent.Tests.Middleware.V2.MiddlewareTestHelpers;

namespace HPD.Agent.Tests.Content;

public sealed class WorkspaceContentDiscoveryMiddlewareTests
{
    [Fact]
    public async Task BeforeMessageTurnAsync_InjectsWorkspaceContentAreas()
    {
        var middleware = new WorkspaceContentDiscoveryMiddleware("test-agent");
        var context = CreateBeforeMessageTurnContext(
            conversationHistory: new List<ChatMessage>
            {
                new(ChatRole.System, "system"),
                new(ChatRole.User, "hello")
            });

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        var injected = Assert.Single(context.BranchHistory.Where(message =>
            message.Text?.Contains("<workspace_content>") == true));
        Assert.Contains("/agents", injected.Text);
        Assert.Contains("/projects", injected.Text);
        Assert.Contains("content_attach", injected.Text);
        Assert.Contains("content_detach", injected.Text);
        Assert.DoesNotContain("role=skill", injected.Text);
        Assert.DoesNotContain("folder", injected.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_DoesNotReinjectUnchangedAreas()
    {
        var middleware = new WorkspaceContentDiscoveryMiddleware("test-agent");
        var context = CreateBeforeMessageTurnContext();

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Single(context.BranchHistory.Where(message =>
            message.Text?.Contains("<workspace_content>") == true));
    }
}
