using System.Text.Json.Serialization;
using HPD.Events;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Per-run system-instruction replacement and append behavior.</summary>
public sealed class SystemInstructionsRunConfig
{
    /// <summary>Gets or sets a complete replacement for the agent default instructions.</summary>
    public string? Override { get; set; }

    /// <summary>Gets or sets instructions appended after the effective base instructions.</summary>
    public string? Append { get; set; }
}

/// <summary>Per-run tool exposure and client-tool configuration.</summary>
public sealed class AgentToolsRunConfig
{
    /// <summary>Gets or sets the tool-mode override.</summary>
    [JsonIgnore]
    public ChatToolMode? Mode { get; set; }

    /// <summary>Gets or sets additional functions exposed for this run.</summary>
    [JsonIgnore]
    public IReadOnlyList<AIFunction>? Additional { get; set; }

    /// <summary>Gets or sets client tool input for this run.</summary>
    [JsonIgnore]
    public ClientTools.AgentClientInput? ClientInput { get; set; }

    /// <summary>Gets or sets connected client-app providers selected for this run.</summary>
    [JsonIgnore]
    public IReadOnlyList<ClientTools.ClientAppProviderReference>? ClientAppProviders { get; set; }
}

/// <summary>Per-run middleware properties and context-backed tool instances.</summary>
public sealed class AgentContextRunConfig
{
    /// <summary>Gets or sets properties exposed to middleware.</summary>
    public IDictionary<string, object>? Properties { get; set; }

    /// <summary>Gets or sets runtime-only context-backed tool metadata.</summary>
    [JsonIgnore]
    public IDictionary<string, IToolMetadata>? ToolInstances { get; set; }
}

/// <summary>Per-run background-response behavior.</summary>
public sealed class BackgroundResponsesRunConfig
{
    /// <summary>Gets or sets whether background responses are allowed.</summary>
    public bool? Allow { get; set; }

    /// <summary>Gets or sets a runtime-only continuation token.</summary>
    [JsonIgnore]
    public ResponseContinuationToken? ContinuationToken { get; set; }

    /// <summary>Gets or sets the polling interval.</summary>
    public TimeSpan? PollingInterval { get; set; }

    /// <summary>Gets or sets the background-response timeout.</summary>
    public TimeSpan? Timeout { get; set; }
}

/// <summary>Per-run streaming behavior.</summary>
public sealed class StreamingRunConfig
{
    /// <summary>Gets or sets whether streaming deltas are coalesced.</summary>
    public bool? CoalesceDeltas { get; set; }

    /// <summary>Gets or sets a runtime-only stream callback.</summary>
    [JsonIgnore]
    public Func<AgentEvent, Task>? Callback { get; set; }
}
