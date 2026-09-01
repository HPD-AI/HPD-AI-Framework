using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding.Tests;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Harness.Coding.Tests;

public sealed class CodingAgentShutdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentDisposal_DrainsExecutionBeforeWorkspaceAndLowerDependencies(
        bool failExecutionCleanup)
    {
        var events = new ConcurrentQueue<string>();
        var workspaces = new ShutdownTrackingWorkspaceRegistry(events, failExecutionCleanup);
        var client = new BlockingCodingHarnessClient(events);
        var builder = new AgentBuilder(new AgentConfig
            {
                Name = "coding-shutdown-order",
                MaxAgenticIterations = 5,
                Shutdown = new AgentShutdownOptions
                {
                    GracefulDrainTimeout = TimeSpan.FromMilliseconds(10),
                    CancellationDrainTimeout = TimeSpan.FromSeconds(2)
                }
            })
            .WithChatClient(client)
            .WithEventComposition(CodingEventTestCodec.Composition)
            .WithToolHarness<CodingToolHarness>();
        ReplaceWorkspaceResource(builder, workspaces);
        var agent = await builder.BuildAsync();
        var run = agent.RunAsync("activate-coding");
        await client.BlockedAfterExpansion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = agent.DisposeAsync().AsTask();
        if (failExecutionCleanup)
            await Assert.ThrowsAsync<AggregateException>(() => disposal);
        else
            await disposal;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Equal(1, workspaces.AcquireCount);
        Assert.Equal(1, workspaces.ReleaseCount);
        Assert.True(workspaces.Disposed);
        Assert.True(client.Disposed);
        var ordered = events.ToArray();
        Assert.True(Array.IndexOf(ordered, "lease-released") < Array.IndexOf(ordered, "workspace-disposed"));
        Assert.True(Array.IndexOf(ordered, "workspace-disposed") < Array.IndexOf(ordered, "client-disposed"));
    }

    private static void ReplaceWorkspaceResource(
        AgentBuilder builder,
        ILanguageServerWorkspaceRegistry replacement)
    {
        var index = builder._selectedToolHarnessFactories.FindIndex(
            factory => factory.Name == nameof(CodingToolHarness));
        Assert.True(index >= 0);
        var factory = builder._selectedToolHarnessFactories[index];
        builder._selectedToolHarnessFactories[index] = factory with
        {
            AgentResources = factory.AgentResources!.Select(descriptor =>
                descriptor.ResourceType == typeof(ILanguageServerWorkspaceRegistry)
                    ? descriptor with { Factory = () => replacement }
                    : descriptor).ToArray()
        };
    }

    private sealed class ShutdownTrackingWorkspaceRegistry(
        ConcurrentQueue<string> events,
        bool failLeaseRelease) : ILanguageServerWorkspaceRegistry
    {
        private readonly ProbeService _service = new();
        private int _acquired;
        private int _released;
        internal int AcquireCount => Volatile.Read(ref _acquired);
        internal int ReleaseCount => Volatile.Read(ref _released);
        internal bool Disposed { get; private set; }

        public ILanguageServerWorkspaceLease Acquire(
            string canonicalWorkspaceIdentity,
            LanguageServerOptions options)
        {
            Interlocked.Increment(ref _acquired);
            return new Lease(_service, () =>
            {
                Interlocked.Increment(ref _released);
                events.Enqueue("lease-released");
                if (failLeaseRelease)
                    throw new InvalidOperationException("execution cleanup failed");
            });
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            events.Enqueue("workspace-disposed");
            return _service.DisposeAsync();
        }

        private sealed class Lease(ILanguageServerService service, Action release)
            : ILanguageServerWorkspaceLease
        {
            private int _disposed;
            public ILanguageServerService Service { get; } = service;
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) release();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class BlockingCodingHarnessClient(ConcurrentQueue<string> events) : IChatClient
    {
        private int _stage;
        internal TaskCompletionSource BlockedAfterExpansion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _stage) == 1)
                return ToolCall(nameof(CodingToolHarness), "expand-coding");
            BlockedAfterExpansion.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(chatMessages, options, cancellationToken);
            foreach (var message in response.Messages)
            {
                yield return new ChatResponseUpdate
                {
                    Role = message.Role,
                    Contents = message.Contents,
                    FinishReason = ChatFinishReason.ToolCalls
                };
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose()
        {
            Disposed = true;
            events.Enqueue("client-disposed");
        }

        private static ChatResponse ToolCall(string name, string callId) => new(
            [new ChatMessage(ChatRole.Assistant,
                [(AIContent)new FunctionCallContent(callId, name, new Dictionary<string, object?>())])]);
    }

    private sealed class ProbeService : ILanguageServerService
    {
        public ValueTask<IReadOnlyList<LanguageServerStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<LanguageServerStatus>>([]);
        public ValueTask<bool> HasServerForFileAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<LanguageServerDocumentResolution> ResolveDocumentAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LanguageServerDocumentResolution { Path = path, Uri = path });
        public ValueTask<LanguageServerOpenResult> OpenDocumentAsync(LanguageServerDocumentOpenRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LanguageServerOpenResult { Path = request.Path, Uri = request.Uri, LanguageId = "test" });
        public ValueTask<LanguageServerChangeResult> ChangeDocumentAsync(LanguageServerDocumentChangeRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LanguageServerChangeResult { Path = request.Path });
        public ValueTask SaveDocumentAsync(LanguageServerDocumentSaveRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CloseDocumentAsync(LanguageServerDocumentCloseRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask NotifyWatchedFileChangedAsync(LanguageServerWatchedFileChangeRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<LanguageServerDiagnosticSet>> GetDiagnosticsAsync(LanguageServerDiagnosticRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<LanguageServerDiagnosticSet>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
