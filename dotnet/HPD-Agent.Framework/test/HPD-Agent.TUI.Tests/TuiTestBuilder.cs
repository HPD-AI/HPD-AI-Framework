using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;

namespace HPD.Agent.TUI.Tests;

internal static class TuiTestBuilder
{
    public static HpdAgentTuiBuilder Create()
        => new(
            new AgentTuiContributionStore(),
            new HpdContributionOwner(
                "hpd.test",
                "test",
                DisplayName: "HPD TUI Tests"));

    public static HpdAgentTuiRegistry CreateRegistry(Action<HpdAgentTuiBuilder> configure)
    {
        var builder = Create();
        configure(builder);
        return builder.Build();
    }

    public static IHpdAgentTuiRegistryProvider CreateProvider(Action<HpdAgentTuiBuilder> configure)
    {
        var store = new AgentTuiContributionStore();
        var builder = new HpdAgentTuiBuilder(
            store,
            new HpdContributionOwner(
                "hpd.test",
                "test",
                DisplayName: "HPD TUI Tests"));
        configure(builder);
        return new HpdAgentTuiRegistryProvider(store);
    }

    public static IAgentTuiSessionUi NoopSessionUi { get; } = new NoopAgentTuiSessionUi();

    private sealed class NoopAgentTuiSessionUi : IAgentTuiSessionUi
    {
        public void SetStatus(string key, string? text)
        {
        }

        public void SetTemporaryWidget(
            string key,
            IAgentTuiWidget? widget,
            TuiSlot slot = TuiSlot.AboveEditor)
        {
        }

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<string?> PromptAsync(
            string title,
            string? placeholder = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public void Notify(
            string message,
            TranscriptSeverity severity = TranscriptSeverity.Info)
        {
        }

        public void SetWorkingMessage(string? message)
        {
        }

        public ValueTask SetPromptDraftAsync(
            string text,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public void RequestRender()
        {
        }
    }
}
