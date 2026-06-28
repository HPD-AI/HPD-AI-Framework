using FluentAssertions;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Tests;

public sealed class AgentTuiSessionUiTests
{
    [Fact]
    public void SetStatus_AddsAndClearsOwnerScopedStatus()
    {
        var owner = new HpdContributionOwner("hpd.test.package", "test");
        var shell = CreateShell();
        var ui = CreateUi(owner, shell);

        ui.SetStatus("build", "building");
        shell.Status.Snapshot().Should().ContainSingle(status =>
            status.Key == "build" &&
            status.Text == "building" &&
            status.Owner == owner);

        ui.SetStatus("build", null);
        shell.Status.Count.Should().Be(0);
    }

    [Fact]
    public void SetTemporaryWidget_AddsReplacesAndClearsOwnerScopedWidget()
    {
        var owner = new HpdContributionOwner("hpd.test.package", "test");
        var shell = CreateShell();
        var ui = CreateUi(owner, shell);

        ui.SetTemporaryWidget("progress", new TextWidget("one"));
        ui.SetTemporaryWidget("progress", new TextWidget("two"));

        shell.AboveEditor.Count.Should().Be(1);
        shell.AboveEditor.Snapshot().Should().ContainSingle(entry =>
            entry.Key == "progress" &&
            entry.Owner == owner);

        ui.SetTemporaryWidget("progress", null);
        shell.AboveEditor.Count.Should().Be(0);
    }

    [Fact]
    public void InvalidateOwner_ClearsOwnerScopedSessionUiAndRejectsOldHandle()
    {
        var owner = new HpdContributionOwner("hpd.test.package", "test");
        var other = new HpdContributionOwner("hpd.other", "test");
        var shell = CreateShell();
        var controller = new AgentTuiSessionUiController();
        var ui = CreateUi(owner, shell, controller);
        var otherUi = CreateUi(other, shell, controller);

        ui.SetStatus("owned", "owned");
        ui.SetTemporaryWidget("owned", new TextWidget("owned"));
        otherUi.SetStatus("other", "other");

        controller.InvalidateOwner(shell, owner);

        shell.Status.Snapshot().Should().ContainSingle(status => status.Owner == other);
        shell.AboveEditor.Count.Should().Be(0);
        var act = () => ui.SetStatus("owned", "again");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*hpd.test.package*stale*");
        otherUi.SetStatus("other", "still current");
        shell.Status.Snapshot().Should().ContainSingle(status =>
            status.Owner == other && status.Text == "still current");
    }

    [Fact]
    public async Task PromptDraftAndNotify_UpdateSessionModels()
    {
        var owner = new HpdContributionOwner("hpd.test.package", "test", DisplayName: "Test Package");
        var shell = CreateShell();
        var renderRequests = 0;
        string? draft = null;
        var ui = CreateUi(
            owner,
            shell,
            requestRender: () => renderRequests++,
            setPromptDraftAsync: (value, _) =>
            {
                draft = value;
                return ValueTask.CompletedTask;
            });

        await ui.SetPromptDraftAsync("hello").ConfigureAwait(false);
        ui.Notify("done", TranscriptSeverity.Success);

        draft.Should().Be("hello");
        shell.Transcript.Count.Should().Be(1);
        renderRequests.Should().Be(1);
    }

    private static ChatShellModel CreateShell()
        => new(new AgentTuiRuntimeScope("agent", "session", "main"));

    private static AgentTuiSessionUi CreateUi(
        HpdContributionOwner owner,
        ChatShellModel shell,
        AgentTuiSessionUiController? controller = null,
        Action? requestRender = null,
        Func<string, CancellationToken, ValueTask>? setPromptDraftAsync = null)
    {
        controller ??= new AgentTuiSessionUiController();
        var registry = TuiTestBuilder.Create()
            .AddAgentTuiDefaults()
            .Build();
        return new AgentTuiSessionUi(
            controller,
            owner,
            controller.GetGeneration(owner),
            shell.Scope,
            shell,
            new AgentTuiStateBag(),
            registry,
            NoopDialogs.Instance,
            setPromptDraftAsync ?? ((_, _) => ValueTask.CompletedTask),
            requestRender ?? (() => { }));
    }

    private sealed class TextWidget : IAgentTuiWidget
    {
        private readonly string _text;

        public TextWidget(string text)
        {
            _text = text;
        }

        public IComponent Create(AgentTuiWidgetContext context) => new Text(_text);
    }

    private sealed class NoopDialogs : IAgentTuiDialogService
    {
        public static NoopDialogs Instance { get; } = new();

        public bool HasOpenDialog => false;

        public Task<TResult?> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TResult?>(default);

        public bool Close(string key) => false;

        public bool CloseTop() => false;

        public Task<bool?> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(defaultValue);

        public Task<T?> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
            => Task.FromResult<T?>(default);

        public Task<string?> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(defaultValue);

        public Task<string?> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }
}
