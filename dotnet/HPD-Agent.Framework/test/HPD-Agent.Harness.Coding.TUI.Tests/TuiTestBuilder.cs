using HPD.Agent;
using HPD.Agent.TUI;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using System.Runtime.CompilerServices;

namespace HPD.Agent.ToolHarness.Coding.TUI.Tests;

internal static class TuiTestBuilder
{
    private static readonly ConditionalWeakTable<AgentTuiSessionState, RegistryHolder> Registries = new();

    public static HpdAgentTuiBuilder Create()
        => new(
            new AgentTuiContributionStore(),
            new HpdContributionOwner(
                "hpd.test",
                "test",
                DisplayName: "HPD Coding TUI Tests"));

    public static AgentTuiSessionState CreateState(
        AgentTuiRuntimeScope scope,
        HpdAgentTuiRegistry registry)
    {
        var state = new AgentTuiSessionState(scope);
        Registries.Add(state, new RegistryHolder(registry));
        return state;
    }

    public static HpdAgentTuiRegistry GetRegistry(AgentTuiSessionState state)
        => Registries.TryGetValue(state, out var holder)
            ? holder.Registry
            : throw new InvalidOperationException("No test registry was associated with this TUI session state.");

    public static IAgentTuiSessionUi NoopSessionUi { get; } = new NoopAgentTuiSessionUi();

    private sealed class RegistryHolder(HpdAgentTuiRegistry registry)
    {
        public HpdAgentTuiRegistry Registry { get; } = registry;
    }

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

internal static class AgentTuiSessionStateTestExtensions
{
    public static ValueTask ApplyEventAsync(
        this AgentTuiSessionState state,
        AgentEvent evt,
        CancellationToken cancellationToken = default)
        => state.ApplyEventAsync(evt, TuiTestBuilder.GetRegistry(state), cancellationToken);
}
