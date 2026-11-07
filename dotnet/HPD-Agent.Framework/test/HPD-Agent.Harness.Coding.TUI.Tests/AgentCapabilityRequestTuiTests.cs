using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Permissions;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class AgentCapabilityRequestTuiTests
{
    [Fact]
    public async Task SandboxPermissionDialog_OffersTurnScopedApproval()
    {
        var dialogs = new TestDialogService();
        var handler = new PermissionRequestInteractionHandler();

        await handler.HandleAsync(
            CreateInteractionContext(dialogs, SandboxRequest(
                "request-1",
                "sandbox/filesystem-read",
                "/repo/src/App")),
            CancellationToken.None);

        dialogs.LastOptionLabels.Should().Equal(
            "Allow once",
            "Allow all similar requests this turn",
            "Deny");
    }

    [Fact]
    public async Task AllowForTurn_SuppressesPromptForFilesystemRequestsInSameExecution()
    {
        var dialogs = new TestDialogService
        {
            SelectionIndex = 1
        };
        var handler = new PermissionRequestInteractionHandler();

        var initial = await handler.HandleAsync(
            CreateInteractionContext(dialogs, SandboxRequest(
                "request-1",
                "sandbox/filesystem-read",
                "/repo/src/App")),
            CancellationToken.None);
        var subsequentWrite = await handler.HandleAsync(
            CreateInteractionContext(dialogs, SandboxRequest(
                "request-2",
                "sandbox/filesystem-write",
                "/repo/src/App/obj")),
            CancellationToken.None);

        initial.Response.Should().BeOfType<PermissionResponseEvent>()
            .Which.ChoiceId.Should().Be("allow_for_turn");
        subsequentWrite.Response.Should().BeOfType<PermissionResponseEvent>()
            .Which.ChoiceId.Should().Be("allow_for_turn");
        dialogs.ShowCount.Should().Be(1);
    }

    [Fact]
    public async Task AllowForTurn_DoesNotCrossAuthorityOrExecutionBoundaries()
    {
        var dialogs = new TestDialogService
        {
            SelectionIndex = 1
        };
        var handler = new PermissionRequestInteractionHandler();

        await handler.HandleAsync(
            CreateInteractionContext(dialogs, SandboxRequest(
                "request-1",
                "sandbox/filesystem-read",
                "/repo/src/App")),
            CancellationToken.None);
        await handler.HandleAsync(
            CreateInteractionContext(dialogs, SandboxRequest(
                "request-2",
                "sandbox/network-egress",
                "api.example.com")),
            CancellationToken.None);
        await handler.HandleAsync(
            CreateInteractionContext(dialogs, SandboxRequest(
                "request-3",
                "sandbox/filesystem-read",
                "/repo/src/App",
                threadExecutionId: "next-turn")),
            CancellationToken.None);

        dialogs.ShowCount.Should().Be(3);
    }

    private static PermissionRequestEvent SandboxRequest(
        string permissionId,
        string authority,
        string resource,
        string threadExecutionId = "turn-1")
        => new(
            permissionId,
            "call-1",
            "SandboxCapabilityPermission",
            PermissionDomains.SandboxCapability,
            authority,
            null,
            new PermissionResource(resource, Path.GetFileName(resource)),
            "The operation is outside the active sandbox grants.",
            PermissionRisk.High,
            [
                new("allow_once", "Allow once", PermissionDecisionKind.Allow),
                new("allow_for_turn", "Allow all similar requests this turn",
                    PermissionDecisionKind.Allow),
                new("deny_once", "Deny", PermissionDecisionKind.Deny)
            ],
            FunctionName: "file_tool",
            Arguments: JsonDocument.Parse(
                $$"""{"resource":"{{resource}}"}""").RootElement.Clone())
        {
            ThreadExecutionId = threadExecutionId
        };

    private static AgentTuiInteractionContext CreateInteractionContext(
        IAgentTuiDialogService dialogs,
        PermissionRequestEvent request)
    {
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);
        return new AgentTuiInteractionContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            dialogs,
            request);
    }

    private sealed class TestDialogService : IAgentTuiDialogService
    {
        public int? SelectionIndex { get; init; }
        public IReadOnlyList<string> LastOptionLabels { get; private set; } = [];
        public int ShowCount { get; private set; }
        public bool HasOpenDialog => false;

        public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<TResult>.Dismissed());

        public bool Close(string key) => true;
        public bool CloseTop() => true;

        public Task<AgentTuiDialogResult<bool>> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<bool>.Submitted(defaultValue ?? true));

        public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
        {
            ShowCount++;
            LastOptionLabels = options.Select(titleSelector).ToArray();
            return Task.FromResult(SelectionIndex is { } index
                ? AgentTuiDialogResult<T>.Submitted(options[index])
                : AgentTuiDialogResult<T>.Dismissed());
        }

        public Task<AgentTuiDialogResult<string>> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();

        public Task<AgentTuiDialogResult<string>> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<string>.Dismissed());
    }

    private sealed class NoopRuntime : IHpdAgentTuiRuntime
    {
        public Task<AgentTuiTargetResolution> ResolveInitialTargetAsync(
            AgentTuiExecutionTarget? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiTargetResolution(
                requested ?? new DirectAgentTuiExecutionTarget(
                    new AgentTuiRuntimeScope("agent", "session", "main")),
                IsDurable: true));

        public Task<AgentTuiExecutionTarget> EnsureDurableTargetAsync(
            AgentTuiExecutionTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(target);

        public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
            AgentTuiExecutionTarget target,
            ThreadJournalCursor after,
            ThreadJournalCursor initialObservedCursor,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<AgentTuiSubmitResult> SubmitInputAsync(
            AgentTuiExecutionTarget target,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiSubmitResult(
                AgentInputDisposition.Queued,
                "run",
                new AgentTuiThreadExecution(
                    "run", target.Scope.AgentId, target.Scope.SessionId,
                    target.Scope.ThreadId, "active", DateTimeOffset.UtcNow)));

        public Task<AgentRespondResult> AnswerRequestAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRespondResult(
                AgentRespondStatus.Accepted, response.EventId));

        public Task<AgentTuiSubmitResult> CancelExecutionAsync(
            AgentTuiRuntimeScope scope,
            string threadExecutionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiSubmitResult(
                AgentInputDisposition.Accepted, threadExecutionId, null));

        public Task<AgentTuiThreadState> GetThreadStateAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiThreadState(
                ThreadJournalCursor.Start(1), null, []));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

}
