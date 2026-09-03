using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using System.Reflection;
using System.Text;

namespace HPD.Agent.TUI.Tests;

public sealed class RunConfigComposerTests
{
    [Fact]
    public async Task SubmittedPrompt_CarriesComposedRunConfig()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            builder => builder
                .AddAgentTuiDefaults()
                .SetRunConfigComposer(context =>
                {
                    context.Scope.Should().BeSameAs(scope);
                    context.Prompt.Should().Be("hello");

                    return new AgentTuiInputRunConfig(new AgentRunConfig
                    {
                        Clients = new AgentClientsConfig { Chat = new ChatClientConfig
                        {
                            Provider = new ProviderReference { Key = "openrouter" },
                            ModelName = "deepseek/deepseek-chat"
                        } }
                    }, null);
                }),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        InvokePrivate(app, "SubmitPrompt", "hello".AsMemory());

        runtime.LastInput.Should().BeOfType<UserMessagesInputEvent>()
            .Which.RunConfig.Should().NotBeNull();
        runtime.LastInput!.RunConfig!.Clients.Chat!.Provider!.Key.Should().Be("openrouter");
        runtime.LastInput.RunConfig.Clients.Chat.ModelName.Should().Be("deepseek/deepseek-chat");
    }

    [Fact]
    public async Task SubmittedPrompt_DoesNotAppendSenderOnlyUserMessage()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        InvokePrivate(app, "SubmitPrompt", "hello".AsMemory());

        runtime.SubmitCount.Should().Be(1);
        var state = GetPrivateField<HPD.Agent.TUI.Application.AgentTuiSessionState>(app, "_state");
        state.Shell.Transcript.Snapshot().Entries
            .Select(static entry => entry.Cell)
            .OfType<HPD.Agent.TUI.Models.UserMessageCell>()
            .Should()
            .BeEmpty("the committed event stream is the only transcript authority");
    }

    [Fact]
    public async Task SubmittedPrompt_WhenRunIsActive_QueuesLocallyUntilEscapeSteers()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        await InvokePrivateAsync(
            app,
            "OnAgentEventAsync",
            new ThreadExecutionStartedEvent("run-1", scope.AgentId, DateTimeOffset.UtcNow)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            },
            AgentTuiEventDeliveryMode.Live,
            CancellationToken.None);
        InvokePrivate(app, "SubmitPrompt", "hello".AsMemory());

        runtime.SubmitCount.Should().Be(0);
        GetPrivateField<HPD.Agent.TUI.Application.AgentTuiSessionState>(app, "_state")
            .Shell.PromptStatusText.Should().Be("state: running | follow-up queued | Alt+↑ edit | Esc steer now");

        InvokePrivate(app, "TryExecuteShortcut", new KeyEvent(KeyCode.Escape));
        await runtime.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.SubmitCount.Should().Be(1);
        var steering = runtime.LastInput.Should().BeOfType<UserMessagesInputEvent>().Subject;
        steering.Delivery.Should().Be(AgentInputDelivery.Steer);
        steering.ThreadExecutionId.Should().Be("run-1");
    }

    [Fact]
    public async Task QueuedPrompt_WhenActiveExecutionFinishes_SubmitsOrdinaryWork()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        await InvokePrivateAsync(
            app,
            "OnAgentEventAsync",
            new ThreadExecutionStartedEvent("run-1", scope.AgentId, DateTimeOffset.UtcNow)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            },
            AgentTuiEventDeliveryMode.Live,
            CancellationToken.None);
        InvokePrivate(app, "SubmitPrompt", "follow up".AsMemory());

        await InvokePrivateAsync(
            app,
            "OnAgentEventAsync",
            new ThreadExecutionFinishedEvent(
                "run-1",
                scope.AgentId,
                ThreadExecutionOutcome.Succeeded,
                DateTimeOffset.UtcNow)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            },
            AgentTuiEventDeliveryMode.Live,
            CancellationToken.None);

        await runtime.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.SubmitCount.Should().Be(1);
        runtime.LastInput.Should().BeOfType<UserMessagesInputEvent>();
        PendingPrompts(app, scope).Count.Should().Be(0);
    }

    [Fact]
    public async Task EscapeSteering_WhenRejected_PreservesPendingFollowUp()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope)
        {
            ActiveControlDisposition = AgentInputDisposition.ActiveExecutionMismatch,
            ActiveExecution = new AgentTuiThreadExecution(
                "run-1",
                scope.AgentId,
                scope.SessionId,
                scope.ThreadId,
                "active",
                DateTimeOffset.UtcNow)
        };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        await InvokePrivateAsync(
            app,
            "OnAgentEventAsync",
            new ThreadExecutionStartedEvent("run-1", scope.AgentId, DateTimeOffset.UtcNow)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            },
            AgentTuiEventDeliveryMode.Live,
            CancellationToken.None);
        InvokePrivate(app, "SubmitPrompt", "keep this".AsMemory());
        var state = GetPrivateField<HPD.Agent.TUI.Application.AgentTuiSessionState>(app, "_state");
        await InvokePrivateAsync(app, "PromotePendingPromptToSteeringAsync", new DirectAgentTuiExecutionTarget(scope), state);
        PendingPrompts(app, scope).Snapshot()
            .Should().ContainSingle().Which.Text.Should().Be("keep this");
    }

    [Fact]
    public async Task AltUp_PopsLatestQueuedFollowUpBackIntoComposer()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime, new DirectAgentTuiExecutionTarget(scope), static builder => builder.AddAgentTuiDefaults(), new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        await StartExecutionAsync(app, scope);
        InvokePrivate(app, "SubmitPrompt", "first".AsMemory());
        InvokePrivate(app, "SubmitPrompt", "second".AsMemory());

        InvokePrivate<bool>(app, "TryExecuteShortcut", new KeyEvent(KeyCode.UpArrow, Modifiers: KeyModifiers.Alt))
            .Should().BeTrue();

        GetPrivateField<HPD.TUI.Views.PromptView>(app, "_prompt").Model.Text.ToString().Should().Be("second");
        PendingPrompts(app, scope).Snapshot().Should().ContainSingle().Which.Text.Should().Be("first");
    }

    [Fact]
    public async Task AltUp_DoesNotOverwriteExistingDraft()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime, new DirectAgentTuiExecutionTarget(scope), static builder => builder.AddAgentTuiDefaults(), new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        await StartExecutionAsync(app, scope);
        InvokePrivate(app, "SubmitPrompt", "queued".AsMemory());
        GetPrivateField<HPD.TUI.Views.PromptView>(app, "_prompt").Model.SetText("current draft");

        InvokePrivate<bool>(app, "TryExecuteShortcut", new KeyEvent(KeyCode.UpArrow, Modifiers: KeyModifiers.Alt))
            .Should().BeTrue();

        GetPrivateField<HPD.TUI.Views.PromptView>(app, "_prompt").Model.Text.ToString().Should().Be("current draft");
        PendingPrompts(app, scope).Snapshot().Should().ContainSingle().Which.Text.Should().Be("queued");
    }

    [Fact]
    public async Task QueuedFollowUp_SubmissionFailure_RestoresComposerInsteadOfLosingInput()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope) { SubmissionError = new InvalidOperationException("offline") };
        await using var app = HpdAgentTuiApp.Create(
            runtime, new DirectAgentTuiExecutionTarget(scope), static builder => builder.AddAgentTuiDefaults(), new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        await StartExecutionAsync(app, scope);
        InvokePrivate(app, "SubmitPrompt", "do not lose me".AsMemory());
        await FinishExecutionAsync(app, scope);
        await runtime.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        GetPrivateField<HPD.TUI.Views.PromptView>(app, "_prompt").Model.Text.ToString().Should().Be("do not lose me");
        PendingPrompts(app, scope).Count.Should().Be(0);
    }

    [Fact]
    public async Task RebuildShell_PreservesIndependentQueuesPerScope()
    {
        var firstScope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var secondScope = new AgentTuiRuntimeScope("agent-a", "session-b", "main");
        var firstTarget = new DirectAgentTuiExecutionTarget(firstScope);
        var secondTarget = new DirectAgentTuiExecutionTarget(secondScope);
        var runtime = new CapturingRuntime(firstScope);
        await using var app = HpdAgentTuiApp.Create(
            runtime, firstTarget, static builder => builder.AddAgentTuiDefaults(), new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", firstTarget, "First");
        await StartExecutionAsync(app, firstScope);
        InvokePrivate(app, "SubmitPrompt", "first scope".AsMemory());
        InvokePrivate(app, "RebuildShell", secondTarget, "Second");
        await StartExecutionAsync(app, secondScope);
        InvokePrivate(app, "SubmitPrompt", "second scope".AsMemory());
        InvokePrivate(app, "RebuildShell", firstTarget, "First again");

        PendingPrompts(app, firstTarget).Snapshot().Should().ContainSingle().Which.Text.Should().Be("first scope");
        PendingPrompts(app, secondTarget).Snapshot().Should().ContainSingle().Which.Text.Should().Be("second scope");
    }

    [Fact]
    public void PendingPromptPreview_BoundsVisibleItemsAndShowsControls()
    {
        var queue = new PendingPromptQueue();
        queue.Enqueue("first");
        queue.Enqueue("second");
        queue.Enqueue("third");
        queue.Enqueue("fourth");

        var rendered = TuiCapture.RenderToString(
            new PendingPromptPreview(queue), width: 60, height: 8, trimTrailingBlankLines: true);

        rendered.Should().Contain("Queued follow-ups");
        rendered.Should().Contain("first").And.Contain("second").And.Contain("third");
        rendered.Should().NotContain("fourth");
        rendered.Should().Contain("1 more");
        rendered.Should().Contain("Alt+↑ edit latest · Esc steer next");
    }

    private static PendingPromptQueue PendingPrompts(HpdAgentTuiApp app, AgentTuiRuntimeScope scope)
        => PendingPrompts(app, new DirectAgentTuiExecutionTarget(scope));

    private static PendingPromptQueue PendingPrompts(HpdAgentTuiApp app, AgentTuiExecutionTarget target)
        => InvokePrivate<PendingPromptQueue>(app, "PendingPrompts", target);

    private static Task StartExecutionAsync(HpdAgentTuiApp app, AgentTuiRuntimeScope scope)
        => InvokePrivateAsync(
            app,
            "OnAgentEventAsync",
            new ThreadExecutionStartedEvent("run-1", scope.AgentId, DateTimeOffset.UtcNow)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            },
            AgentTuiEventDeliveryMode.Live,
            CancellationToken.None);

    private static Task FinishExecutionAsync(HpdAgentTuiApp app, AgentTuiRuntimeScope scope)
        => InvokePrivateAsync(
            app,
            "OnAgentEventAsync",
            new ThreadExecutionFinishedEvent("run-1", scope.AgentId, ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            },
            AgentTuiEventDeliveryMode.Live,
            CancellationToken.None);

    private static void InvokePrivate(
        HpdAgentTuiApp app,
        string methodName,
        params object[] args)
    {
        var method = typeof(HpdAgentTuiApp).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(app, args);
    }

    private static T InvokePrivate<T>(HpdAgentTuiApp app, string methodName, params object[] args)
    {
        var method = typeof(HpdAgentTuiApp).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(app, args).Should().BeOfType<T>().Subject;
    }

    private static async Task InvokePrivateAsync(
        HpdAgentTuiApp app,
        string methodName,
        params object[] args)
    {
        var method = typeof(HpdAgentTuiApp).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        await ((Task)method!.Invoke(app, args)!).ConfigureAwait(false);
    }

    private static T GetPrivateField<T>(HpdAgentTuiApp app, string fieldName)
        where T : class
    {
        var field = typeof(HpdAgentTuiApp).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(app).Should().BeOfType<T>().Subject;
    }

    private sealed class CapturingRuntime : IHpdAgentTuiRuntime
    {
        private readonly AgentTuiRuntimeScope _scope;

        public CapturingRuntime(AgentTuiRuntimeScope scope)
        {
            _scope = scope;
        }

        public AgentInputEvent? LastInput { get; private set; }
        public int SubmitCount { get; private set; }
        public AgentInputDisposition ActiveControlDisposition { get; init; } = AgentInputDisposition.Accepted;
        public Exception? SubmissionError { get; init; }
        public AgentTuiThreadExecution? ActiveExecution { get; init; }
        public TaskCompletionSource Submitted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AgentTuiTargetResolution> ResolveInitialTargetAsync(
            AgentTuiExecutionTarget? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiTargetResolution(
                requested ?? new DirectAgentTuiExecutionTarget(_scope), IsDurable: true));

        public Task<AgentTuiExecutionTarget> EnsureDurableTargetAsync(
            AgentTuiExecutionTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(target);

        public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
            AgentTuiExecutionTarget target,
            ThreadJournalCursor after,
            ThreadJournalCursor initialObservedCursor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<AgentTuiSubmitResult> SubmitInputAsync(
            AgentTuiExecutionTarget target,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
        {
            var scope = target.Scope;
            SubmitCount++;
            LastInput = input;
            Submitted.TrySetResult();
            if (SubmissionError is not null) throw SubmissionError;
            return Task.FromResult(new AgentTuiSubmitResult(
                input is UserMessagesInputEvent { Delivery: AgentInputDelivery.Steer }
                    ? ActiveControlDisposition
                    : AgentInputDisposition.Queued,
                input.ThreadExecutionId ?? "run",
                new AgentTuiThreadExecution("run", scope.AgentId, scope.SessionId, scope.ThreadId, "active", DateTimeOffset.UtcNow)));
        }

        public Task<AgentRespondResult> AnswerRequestAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRespondResult(AgentRespondStatus.Accepted, ((IAgentResponseEvent)response).RequestId));

        public Task<AgentTuiSubmitResult> CancelExecutionAsync(
            AgentTuiRuntimeScope scope, string threadExecutionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiSubmitResult(AgentInputDisposition.Accepted, threadExecutionId, null));

        public Task<AgentTuiThreadState> GetThreadStateAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiThreadState(ThreadJournalCursor.Start(1), ActiveExecution, []));
    }

    private sealed class TestTerminal : ITerminal, ITerminalInput
    {
        private readonly StringBuilder _output = new();
        private TerminalSize _size;

        public TestTerminal(int width, int height)
        {
            _size = new TerminalSize(width, height);
        }

        public TerminalSize GetSize() => _size;

        public void Write(ReadOnlySpan<char> text)
        {
            _output.Append(text);
        }

        public void Flush()
        {
        }

        public ITerminalInput Input => this;

        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(TerminalInputEvent.Stop);

        public void HideCursor()
        {
        }

        public void ShowCursor()
        {
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
