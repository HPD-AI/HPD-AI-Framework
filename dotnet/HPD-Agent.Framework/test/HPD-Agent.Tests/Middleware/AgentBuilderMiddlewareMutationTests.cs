using HPD.Agent;
using HPD.Agent.Middleware;

namespace HPD.Agent.Tests.Middleware;

public sealed class AgentBuilderMiddlewareMutationTests
{
    [Fact]
    public void WithMiddleware_Null_ThrowsImmediately()
    {
        var builder = new AgentBuilder();

        Assert.Throws<ArgumentNullException>(() =>
            builder.WithMiddleware(null!));
    }

    [Fact]
    public void ReplaceMiddleware_PreservesFirstPosition_AndRemovesAdditionalMatches()
    {
        var first = new MarkerMiddleware();
        var other = new OtherMiddleware();
        var duplicate = new MarkerMiddleware();
        var builder = new AgentBuilder()
            .WithMiddleware(first)
            .WithMiddleware(other)
            .WithMiddleware(duplicate);
        var replacement = new MarkerMiddleware();

        builder.ReplaceMiddleware<MarkerMiddleware>(replacement);

        Assert.Equal(
            [replacement, other],
            builder.Middlewares);
    }

    [Fact]
    public void ReplaceMiddleware_WhenMissing_AppendsReplacement()
    {
        var other = new OtherMiddleware();
        var replacement = new MarkerMiddleware();
        var builder = new AgentBuilder().WithMiddleware(other);

        builder.ReplaceMiddleware<MarkerMiddleware>(replacement);

        Assert.Equal([other, replacement], builder.Middlewares);
    }

    [Fact]
    public void InsertMiddlewareBefore_WhenAnchorMissing_AppendsMiddleware()
    {
        var existing = new OtherMiddleware();
        var inserted = new MarkerMiddleware();
        var builder = new AgentBuilder().WithMiddleware(existing);

        builder.InsertMiddlewareBefore<AnchorMiddleware>(inserted);

        Assert.Equal([existing, inserted], builder.Middlewares);
    }

    [Fact]
    public void InsertMiddlewareBefore_WhenAnchorExists_InsertsBeforeAnchor()
    {
        var first = new OtherMiddleware();
        var anchor = new AnchorMiddleware();
        var last = new OtherMiddleware();
        var inserted = new MarkerMiddleware();
        var builder = new AgentBuilder()
            .WithMiddlewares(first, anchor, last);

        builder.InsertMiddlewareBefore<AnchorMiddleware>(inserted);

        Assert.Equal([first, inserted, anchor, last], builder.Middlewares);
    }

    [Fact]
    public void MoveAndRemoveMiddleware_UpdatePipelineOrder()
    {
        var marker = new MarkerMiddleware();
        var first = new OtherMiddleware();
        var last = new AnchorMiddleware();
        var builder = new AgentBuilder()
            .WithMiddlewares(first, marker, last);

        builder.MoveMiddlewareToStart<MarkerMiddleware>();

        Assert.Equal([marker, first, last], builder.Middlewares);

        builder.RemoveMiddleware<MarkerMiddleware>();

        Assert.Equal([first, last], builder.Middlewares);
    }

    private class MarkerMiddleware : IAgentMiddleware;
    private sealed class OtherMiddleware : IAgentMiddleware;
    private sealed class AnchorMiddleware : IAgentMiddleware;
}
