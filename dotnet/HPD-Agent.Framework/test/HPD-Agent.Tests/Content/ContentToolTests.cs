using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using HPD.Agent.Middleware;
using HPD.Agent.Validation;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Content;

public sealed class ContentToolTests
{
    [Fact]
    public async Task Read_AppliesLineRangeBeforePerCallByteLimit()
    {
        var store = new InMemoryContentStore();
        var info = await WriteBundleAsync(store, "line one\nline two\nline three\nline four\n");
        var context = CreateContext(store, limits: new ContentRetrievalLimits
        {
            MaxReadBytesPerCall = 20,
            MaxReadBytesPerTurn = 100
        });

        var result = Assert.IsType<ContentToolReadResult>(await new ContentToolHarness().ContentAsync(
            new ContentReadOperation(info.Address.ContentId, Part: "log", StartLine: 1, EndLine: 1),
            context));

        Assert.Equal("line two", result.Text);
        Assert.Equal("utf-8", result.Encoding);
    }

    [Fact]
    public async Task Read_ByteRangeReturnsExactNextOffset()
    {
        var store = new InMemoryContentStore();
        var info = await WriteBundleAsync(store, "0123456789");
        var context = CreateContext(store, limits: new ContentRetrievalLimits
        {
            MaxReadBytesPerCall = 4,
            MaxReadBytesPerTurn = 100
        });

        var result = Assert.IsType<ContentToolReadResult>(await new ContentToolHarness().ContentAsync(
            new ContentReadOperation(info.Address.ContentId, Part: "log", Offset: 2, MaxBytes: 4),
            context));

        Assert.Equal("base64", result.Encoding);
        Assert.Equal("2345", Encoding.UTF8.GetString(Convert.FromBase64String(result.Text)));
        Assert.Equal(6, result.NextOffset);
    }

    [Fact]
    public async Task RetrievalBudget_IsSharedAcrossCallsInOneAcceptedInput()
    {
        var store = new InMemoryContentStore();
        var info = await WriteBundleAsync(store, "abcdef");
        var agentContext = CreateAgentContext(store, "main");
        var harness = new ContentToolHarness();

        _ = await harness.ContentAsync(
            new ContentReadOperation(info.Address.ContentId, Part: "log", MaxBytes: 1),
            CreateContext(agentContext));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ContentAsync(
                new ContentReadOperation(info.Address.ContentId, Part: "log", MaxBytes: 1),
                CreateContext(agentContext)));
        Assert.Equal("content_retrieval_turn_budget_exceeded", error.Message);
    }

    [Fact]
    public async Task Read_RejectsRestrictedAndExpiredContent()
    {
        var store = new InMemoryContentStore();
        var restricted = await WriteBundleAsync(
            store,
            "secret",
            tags: new Dictionary<string, string>
            {
                ["sensitivity"] = nameof(ContentSensitivity.Restricted)
            });
        var context = CreateContext(store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ContentToolHarness().ContentAsync(
                new ContentReadOperation(restricted.Address.ContentId, Part: "log"),
                context));

        var expired = await WriteBundleAsync(
            store,
            "expired",
            tags: new Dictionary<string, string>
            {
                ["expires-at"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")
            });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ContentToolHarness().ContentAsync(
                new ContentReadOperation(expired.Address.ContentId, Part: "log"),
                CreateContext(store)));
    }

    [Fact]
    public async Task Read_AllowsDirectSameSessionForkButRejectsUnrelatedThread()
    {
        var store = new InMemoryContentStore();
        var info = await WriteBundleAsync(store, "fork evidence");
        var forked = CreateContext(store, threadId: "child", forkedFrom: "main");

        var result = Assert.IsType<ContentToolReadResult>(await new ContentToolHarness().ContentAsync(
            new ContentReadOperation(info.Address.ContentId, Part: "log"),
            forked));
        Assert.Equal("fork evidence", result.Text);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ContentToolHarness().ContentAsync(
                new ContentReadOperation(info.Address.ContentId, Part: "log"),
                CreateContext(store, threadId: "unrelated")));
    }

    [Fact]
    public async Task Stat_UsesPartManifestWithoutOpeningBody()
    {
        var store = new NoOpenStore();
        var info = await WriteBundleAsync(store, "not opened");

        var result = Assert.IsType<ContentStatResult>(await new ContentToolHarness().ContentAsync(
            new ContentStatOperation(info.Address.ContentId),
            CreateContext(store)));

        Assert.Equal(new[] { "log" }, result.Parts);
        Assert.Equal(0, store.OpenCalls);
    }

    [Fact]
    public void ConfigurationValidation_RejectsInvalidRetrievalBudgets()
    {
        var errors = AgentConfigValidator.Validate(new AgentConfig
        {
            Name = "invalid-content",
            ToolOutputs = new ToolOutputConfig
            {
                RetrievalLimits = new ContentRetrievalLimits
                {
                    MaxReadBytesPerCall = 100,
                    MaxReadBytesPerTurn = 50
                }
            }
        });

        Assert.Contains(errors, error =>
            error.Contains("MaxReadBytesPerTurn", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Read_RespectsRunLevelRetrievalDisableIndependentlyOfOffloading()
    {
        var store = new InMemoryContentStore();
        var info = await WriteBundleAsync(store, "existing");
        var context = CreateContext(
            CreateAgentContext(store, "main"),
            new AgentRunConfig
            {
                ToolOutputs = new ToolOutputRunPolicy
                {
                    Enabled = false,
                    ContentRetrievalEnabled = false
                }
            });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ContentToolHarness().ContentAsync(
                new ContentReadOperation(info.Address.ContentId, Part: "log"),
                context));
        Assert.Equal("content_retrieval_disabled", error.Message);
    }

    [Fact]
    public async Task Read_RequiresExactPartDigestWhenPartIsSupplied()
    {
        var store = new InMemoryContentStore();
        var info = await WriteBundleAsync(store, "digest target");
        var harness = new ContentToolHarness();

        var error = await Assert.ThrowsAsync<ContentConflictException>(() =>
            harness.ContentAsync(
                new ContentReadOperation(
                    info.Address.ContentId,
                    Part: "log",
                    Digest: "sha256:0000"),
                CreateContext(store)));
        Assert.Contains("part digest", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ContentInfo> WriteBundleAsync(
        InMemoryContentStore store,
        string value,
        IReadOnlyDictionary<string, string>? tags = null) =>
        await WriteBundleCoreAsync(store, value, tags);

    private static async Task<ContentInfo> WriteBundleAsync(
        NoOpenStore store,
        string value) =>
        await WriteBundleCoreAsync(store, value);

    private static async Task<ContentInfo> WriteBundleCoreAsync(
        IContentStore store,
        string value,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        var effectiveTags = new Dictionary<string, string>
        {
            ["kind"] = "tool-output",
            ["thread-id"] = "main",
            ["parts"] = "[\"log\"]",
            ["part-media-types"] = "{\"log\":\"text/plain\"}",
            ["part-digests"] = "{\"log\":\"sha256:" +
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant() + "\"}",
            ["sensitivity"] = nameof(ContentSensitivity.Internal)
        };
        foreach (var pair in tags ?? new Dictionary<string, string>())
            effectiveTags[pair.Key] = pair.Value;
        return await store.WriteBytesAsync(
            ContentScope.Create("session"),
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { parts = new { log = value } })),
            "application/json",
            new ContentMetadata
            {
                Origin = ContentSource.Agent,
                Tags = effectiveTags
            });
    }

    private static FunctionExecutionContext CreateContext(
        IContentStore store,
        string threadId = "main",
        string? forkedFrom = null,
        ContentRetrievalLimits? limits = null)
    {
        var runConfig = new AgentRunConfig
        {
            ToolOutputs = new ToolOutputRunPolicy
            {
                RetrievalLimits = limits ?? new ContentRetrievalLimits
                {
                    MaxReadCallsPerTurn = 1,
                    MaxListCallsPerTurn = 2
                }
            }
        };
        return CreateContext(CreateAgentContext(store, threadId, forkedFrom), runConfig);
    }

    private static AgentContext CreateAgentContext(
        IContentStore store,
        string threadId,
        string? forkedFrom = null)
    {
        var session = new global::HPD.Agent.Session("session");
        var thread = new global::HPD.Agent.Thread("session", threadId, "TestAgent") { ForkedFrom = forkedFrom };
        return new AgentContext(
            "TestAgent",
            "session",
            AgentLoopState.InitialSafe([], "run", "session", "TestAgent"),
            new global::HPD.Events.Core.EventCoordinator(),
            session,
            thread,
            CancellationToken.None,
            contentStore: store);
    }

    private static FunctionExecutionContext CreateContext(
        AgentContext agentContext,
        AgentRunConfig? runConfig = null)
    {
        runConfig ??= new AgentRunConfig
        {
            ToolOutputs = new ToolOutputRunPolicy
            {
                RetrievalLimits = new ContentRetrievalLimits
                {
                    MaxReadCallsPerTurn = 1,
                    MaxListCallsPerTurn = 2
                }
            }
        };
        var tool = HPDAIFunctionFactory.Create(
            static (_, _, _) => Task.FromResult<object?>(null),
            new HPDAIFunctionFactoryOptions { Name = "Content" });
        var before = agentContext.AsBeforeFunction(
            tool,
            "call-content",
            new Dictionary<string, object?>(),
            runConfig);
        return new FunctionExecutionContext(
            before,
            new FunctionRequest
            {
                ExecutionContext = agentContext,
                Function = tool,
                CallId = "call-content",
                Arguments = new Dictionary<string, object?>(),
                State = agentContext.State,
                RunConfig = runConfig
            });
    }

    private sealed class NoOpenStore : IContentStore
    {
        private readonly InMemoryContentStore _inner = new();
        public int OpenCalls { get; private set; }

        public ValueTask<ContentInfo> WriteAsync(
            ContentScope scope,
            Stream data,
            ContentMetadata metadata,
            ContentWriteOptions options,
            CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(scope, data, metadata, options, cancellationToken);

        public ValueTask<ContentReadResult?> OpenReadAsync(
            ContentAddress address,
            CancellationToken cancellationToken = default)
        {
            OpenCalls++;
            return _inner.OpenReadAsync(address, cancellationToken);
        }

        public ValueTask<Uri?> CreateReadUriAsync(
            ContentAddress address,
            TimeSpan expiresIn,
            CancellationToken cancellationToken = default) =>
            _inner.CreateReadUriAsync(address, expiresIn, cancellationToken);

        public ValueTask<ContentInfo?> StatAsync(
            ContentAddress address,
            CancellationToken cancellationToken = default) =>
            _inner.StatAsync(address, cancellationToken);

        public ValueTask DeleteAsync(
            ContentAddress address,
            CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(address, cancellationToken);

        public ValueTask<IReadOnlyList<ContentInfo>> QueryAsync(
            ContentScope scope,
            ContentQuery? query = null,
            CancellationToken cancellationToken = default) =>
            _inner.QueryAsync(scope, query, cancellationToken);
    }
}
