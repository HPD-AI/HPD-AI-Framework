using HPD.Agent.TUI.Runtime;
using HPD.TUI.Models;

namespace HPD.Agent.TUI.Models;

public sealed class ChatShellModel
{
    public ChatShellModel(AgentTuiRuntimeScope scope)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public AgentTuiRuntimeScope Scope { get; }

    public string HeaderText { get; set; } = "";

    public string FooterText { get; set; } = "";

    public IHpdAgentTuiRuntime? Runtime { get; set; }

    public Func<AgentTuiRuntimeScope, CancellationToken, ValueTask>? SwitchScopeAsync { get; set; }

    public Func<string, CancellationToken, ValueTask>? SetPromptDraftAsync { get; set; }

    public AgentTuiNavigationModel Navigation { get; } = new();

    public TranscriptModel Transcript { get; } = new();

    public ActivityGroupModel Activities { get; } = new() { Title = "activity" };

    public WidgetSlotModel AboveEditor { get; } = new();

    public WidgetSlotModel BelowEditor { get; } = new();
}
