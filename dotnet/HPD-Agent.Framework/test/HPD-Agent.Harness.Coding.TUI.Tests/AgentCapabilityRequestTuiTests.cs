using System.Runtime.CompilerServices;
using HPD.Agent;
using HPD.Agent.Security;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.ToolHarness.Coding.TUI;
using HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;
using HPD.TUI.Core;
using HPD.TUI.Rendering;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

public sealed class AgentCapabilityRequestTuiTests
{
    [Fact]
    public async Task FileCapabilityDialog_OffersTurnScopedApproval()
    {
        var dialogs = new TestDialogService();
        var handler = new AgentCapabilityRequestTuiHandler(CodingHarnessTuiTheme.Default);

        await handler.HandleAsync(
            CreateInteractionContext(dialogs, CapabilityRequest(
                "request-1",
                AgentCapabilityKind.FilesystemRead,
                "/repo/src/App")),
            CancellationToken.None);

        dialogs.RenderedDialog.Should().Contain("Allow once");
        dialogs.RenderedDialog.Should().Contain("Allow all files this turn");
        dialogs.RenderedDialog.Should().Contain("Later reads and writes this turn.");
        dialogs.RenderedDialog.Should().Contain("Turn approval remains audited.");
    }

    [Fact]
    public async Task AllowForTurn_AutoApprovesFileReadsAndWritesInSameExecution()
    {
        var dialogs = new TestDialogService
        {
            DialogKeys =
            [
                new KeyEvent(KeyCode.DownArrow),
                new KeyEvent(KeyCode.Enter)
            ]
        };
        var handler = new AgentCapabilityRequestTuiHandler(CodingHarnessTuiTheme.Default);

        var initial = await handler.HandleAsync(
            CreateInteractionContext(dialogs, CapabilityRequest(
                "request-1",
                AgentCapabilityKind.FilesystemRead,
                "/repo/src/App")),
            CancellationToken.None);
        var subsequentWrite = await handler.HandleAsync(
            CreateInteractionContext(dialogs, CapabilityRequest(
                "request-2",
                AgentCapabilityKind.FilesystemWrite,
                "/repo/src/App/obj")),
            CancellationToken.None);

        initial.Response.Should().BeOfType<AgentCapabilityResponseEvent>()
            .Which.Approved.Should().BeTrue();
        subsequentWrite.Response.Should().BeOfType<AgentCapabilityResponseEvent>()
            .Which.Approved.Should().BeTrue();
        dialogs.ShowCount.Should().Be(1);
    }

    [Fact]
    public async Task AllowForTurn_DoesNotCrossExecutionOrCapabilityBoundaries()
    {
        var dialogs = new TestDialogService
        {
            DialogKeys =
            [
                new KeyEvent(KeyCode.DownArrow),
                new KeyEvent(KeyCode.Enter)
            ]
        };
        var handler = new AgentCapabilityRequestTuiHandler(CodingHarnessTuiTheme.Default);

        await handler.HandleAsync(
            CreateInteractionContext(dialogs, CapabilityRequest(
                "request-1",
                AgentCapabilityKind.FilesystemRead,
                "/repo/src/App")),
            CancellationToken.None);
        await handler.HandleAsync(
            CreateInteractionContext(dialogs, CapabilityRequest(
                "request-2",
                AgentCapabilityKind.NetworkEgress,
                "api.example.com")),
            CancellationToken.None);
        await handler.HandleAsync(
            CreateInteractionContext(dialogs, CapabilityRequest(
                "request-3",
                AgentCapabilityKind.FilesystemRead,
                "/repo/src/App",
                threadExecutionId: "next-turn")),
            CancellationToken.None);

        dialogs.ShowCount.Should().Be(3);
    }

    private static AgentCapabilityRequestEvent CapabilityRequest(
        string requestId,
        AgentCapabilityKind capability,
        string resource,
        string threadExecutionId = "turn-1")
        => new(
            requestId,
            "AgentFilesystemAccess",
            "call-1",
            capability == AgentCapabilityKind.FilesystemWrite
                ? "WriteFile"
                : "ReadFile",
            capability,
            new AgentCapabilityResource
            {
                Value = resource,
                DisplayName = Path.GetFileName(resource)
            },
            "The operation is outside the active sandbox grants.")
        {
            ThreadExecutionId = threadExecutionId
        };

    private static AgentTuiInteractionContext CreateInteractionContext(
        IAgentTuiDialogService dialogs,
        AgentCapabilityRequestEvent request)
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
        public IReadOnlyList<KeyEvent> DialogKeys { get; init; } = [];
        public string RenderedDialog { get; private set; } = "";
        public int ShowCount { get; private set; }
        public bool HasOpenDialog => false;

        public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
        {
            ShowCount++;
            var result = AgentTuiDialogResult<TResult>.Dismissed();
            var shell = new ChatShellModel(new AgentTuiRuntimeScope("agent", "session", "main"));
            var dialog = new AgentTuiDialogContext<TResult>(
                key,
                shell.Navigation,
                value => result = value);
            var component = componentFactory(dialog);
            RenderedDialog = TuiCapture.RenderToString(
                component,
                width: 100,
                height: 24,
                trimTrailingBlankLines: true);
            foreach (var keyEvent in DialogKeys)
                component.HandleInput(keyEvent);
            return Task.FromResult(result);
        }

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
            => throw new InvalidOperationException();

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
            => throw new NotSupportedException();

        public Task<AgentTuiSubmitResult> CancelExecutionAsync(
            AgentTuiRuntimeScope scope,
            string threadExecutionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AgentRespondResult> AnswerRequestAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AgentTuiThreadState> GetThreadStateAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiThreadState(
                new ThreadJournalCursor(1, 0),
                null,
                []));
    }
}
