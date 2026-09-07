namespace HPD.Agent;

/// <summary>The trusted owner that requested execution cancellation.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<AgentInputCancellationCause>))]
public enum AgentInputCancellationCause { Unknown, Caller, Explicit, RuntimeShutdown, Middleware }

/// <summary>Cancellation provenance carried through the ordinary input lifecycle.</summary>
public sealed record AgentInputCancellation(AgentInputCancellationCause Cause, string? Reason, string Source);
