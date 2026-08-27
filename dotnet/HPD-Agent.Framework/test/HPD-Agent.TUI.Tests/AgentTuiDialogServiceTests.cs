using FluentAssertions;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Forms;
using HPD.TUI.Views;

namespace HPD.Agent.TUI.Tests;

public sealed class AgentTuiDialogServiceTests
{
    [Fact]
    public async Task FormAsync_SubmitsTypedResultAndCleansUpDialogState()
    {
        var fixture = CreateFixture();
        var enabled = FormFields.Boolean("enabled", "Enabled");
        var form = new FormDefinition<bool>(new FormModel().Add(enabled), () => enabled.Value);

        var pending = fixture.Dialogs.FormAsync("Settings", form);
        fixture.Host.HandleInput(new KeyEvent(KeyCode.RightArrow));
        fixture.Host.HandleInput(new KeyEvent(KeyCode.Enter, Modifiers: KeyModifiers.Ctrl));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

        result.Should().Be(AgentTuiDialogResult<bool>.Submitted(true));
        fixture.Host.HasOpenDialog.Should().BeFalse();
        fixture.Slot.Count.Should().Be(0);
        fixture.Navigation.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public async Task FormAsync_EscapeCancelsDraftAndCleansUpDialogState()
    {
        var fixture = CreateFixture();
        var value = new TextFormField("value", "Value", "original");
        var form = new FormDefinition<string>(new FormModel().Add(value), () => value.Value);

        var pending = fixture.Dialogs.FormAsync("Settings", form);
        fixture.Host.HandleInput(new KeyEvent(KeyCode.Escape));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

        result.IsCanceled.Should().BeTrue();
        fixture.Host.HasOpenDialog.Should().BeFalse();
        fixture.Slot.Count.Should().Be(0);
        fixture.Navigation.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public async Task FormAsync_CancellationTokenUnwindsEveryOwnedResource()
    {
        var fixture = CreateFixture();
        var value = new TextFormField("value", "Value");
        var form = new FormDefinition<string>(new FormModel().Add(value), () => value.Value);
        using var cancellation = new CancellationTokenSource();

        var pending = fixture.Dialogs.FormAsync("Settings", form, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        fixture.Host.HasOpenDialog.Should().BeFalse();
        fixture.Slot.Count.Should().Be(0);
        fixture.Navigation.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public async Task FormAsync_LiveUpdateFlushesBeforeEscapeCompletes()
    {
        var fixture = CreateFixture();
        var enabled = FormFields.Boolean("enabled", "Enabled");
        var updateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var persisted = false;
        var form = new FormDefinition<bool>(
            new FormModel().Add(enabled),
            () => enabled.Value,
            FormUpdateMode.Live,
            async (value, cancellationToken) =>
            {
                updateStarted.TrySetResult();
                await releaseUpdate.Task.WaitAsync(cancellationToken);
                persisted = value;
            });

        var pending = fixture.Dialogs.FormAsync("Settings", form);
        fixture.Host.HandleInput(new KeyEvent(KeyCode.RightArrow));
        await updateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Host.HandleInput(new KeyEvent(KeyCode.Escape));

        pending.IsCompleted.Should().BeFalse();
        releaseUpdate.TrySetResult();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

        result.IsCanceled.Should().BeTrue();
        persisted.Should().BeTrue();
        fixture.Host.HasOpenDialog.Should().BeFalse();
    }

    [Fact]
    public async Task RunFlowAsync_ClosesCompletedStepBeforeMountingNextStep()
    {
        var fixture = CreateFixture();

        IAgentTuiDialogService dialogs = fixture.Dialogs;
        var pending = dialogs.RunFlowAsync<string>(async (flow, cancellationToken) =>
        {
            var first = await flow.SelectAsync(
                "Provider",
                new[] { "OpenAI Codex" },
                static value => value,
                cancellationToken);
            if (!first.IsSubmitted)
                return null;

            var second = await flow.SelectAsync(
                "Sign in",
                new[] { "Use device code" },
                static value => value,
                cancellationToken);
            return second.IsSubmitted ? second.Value : null;
        });

        fixture.Host.HandleInput(new KeyEvent(KeyCode.Enter));
        await WaitUntilAsync(() => fixture.Navigation.ActiveFrame.Title == "Sign in");
        fixture.Host.HasOpenDialog.Should().BeTrue("the next flow step must survive cleanup of the completed step");
        fixture.Slot.Count.Should().Be(1);
        fixture.Navigation.ActiveFrame.Title.Should().Be("Sign in");

        fixture.Host.HandleInput(new KeyEvent(KeyCode.Enter));
        (await pending.WaitAsync(TimeSpan.FromSeconds(2))).Should().Be("Use device code");
        fixture.Host.HasOpenDialog.Should().BeFalse();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 50 && !predicate(); attempt++)
            await Task.Delay(10);
    }

    private static DialogFixture CreateFixture()
    {
        var focus = new FocusManager();
        var content = PromptView.Create();
        focus.SetFocus(content);
        var host = new DialogHost(content, focus);
        var slot = new WidgetSlotModel();
        var navigation = new AgentTuiNavigationModel();
        var dialogs = new AgentTuiDialogService(
            host,
            new AgentTuiDialogChrome { Width = 80 },
            slot,
            navigation);
        return new DialogFixture(host, slot, navigation, dialogs);
    }

    private sealed record DialogFixture(
        DialogHost Host,
        WidgetSlotModel Slot,
        AgentTuiNavigationModel Navigation,
        AgentTuiDialogService Dialogs);
}
