using System.Diagnostics;
using HPD.Agent.MCP;
using ModelContextProtocol.Protocol;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpClientHandlerAdapterTests
{
    [Fact]
    public void MissingResolver_DoesNotAdvertiseUnsupportedElicitationHandler()
    {
        var handlers = McpClientHandlerAdapter.Create("server", new McpInvocationOptions());

        Assert.Null(handlers.ElicitationHandler);
    }

    [Fact]
    public async Task OversizedPayload_IsRejectedBeforeApplicationResolver()
    {
        var resolver = new DelegateResolver((_, _) => throw new InvalidOperationException("must not run"));
        var handlers = McpClientHandlerAdapter.Create("server", new McpInvocationOptions
        {
            InputResolver = resolver,
            MaxInputPayloadCharacters = 4
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handlers.ElicitationHandler!(Request("oversized"), default).AsTask());
        Assert.Equal(0, resolver.CallCount);
    }

    [Fact]
    public async Task HandlerTimeout_CancelsApplicationResolver()
    {
        var resolver = new DelegateResolver(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        });
        var handlers = McpClientHandlerAdapter.Create("server", new McpInvocationOptions
        {
            InputResolver = resolver,
            HandlerTimeout = TimeSpan.FromMilliseconds(10)
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handlers.ElicitationHandler!(Request("confirm"), default).AsTask());
    }

    [Fact]
    public async Task PermissionDenial_FailsClosedBeforeResolverReceivesSensitiveRequest()
    {
        var resolver = new DelegateResolver((_, _) =>
            throw new InvalidOperationException("resolver must not run"));
        var handlers = McpClientHandlerAdapter.Create("server", new McpInvocationOptions
        {
            InputResolver = resolver,
            InputAuthorizer = new DenyingAuthorizer()
        });

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handlers.ElicitationHandler!(Request("secret confirmation"), default).AsTask());

        Assert.Equal("mcp_mrtr_permission_denied", error.Message);
        Assert.Equal(0, resolver.CallCount);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAndSensitiveContextContainsNoResolvedValue()
    {
        McpInputResolutionContext? observed = null;
        var resolver = new DelegateResolver(async (context, cancellationToken) =>
        {
            observed = context;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        });
        var handlers = McpClientHandlerAdapter.Create("server", new McpInvocationOptions
        {
            InputResolver = resolver
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handlers.ElicitationHandler!(Request("secret confirmation"), cancellation.Token).AsTask());
        Assert.NotNull(observed);
        Assert.True(observed!.IsSensitive);
        Assert.DoesNotContain("resolved", observed.Schema, StringComparison.OrdinalIgnoreCase);
    }

    private static ElicitRequestParams Request(string message) => new()
    {
        Message = message,
        RequestedSchema = new ElicitRequestParams.RequestSchema
        {
            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
            {
                ["confirm"] = new ElicitRequestParams.BooleanSchema()
            }
        }
    };

    private sealed class DelegateResolver(
        Func<McpInputResolutionContext, CancellationToken, ValueTask<McpInputResolution>> resolve)
        : IMcpInputResolver
    {
        internal int CallCount { get; private set; }

        public ValueTask<McpInputResolution> ResolveAsync(
            McpInputResolutionContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return resolve(context, cancellationToken);
        }
    }

    private sealed class DenyingAuthorizer : IMcpInputAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(
            McpInputResolutionContext context,
            CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}
