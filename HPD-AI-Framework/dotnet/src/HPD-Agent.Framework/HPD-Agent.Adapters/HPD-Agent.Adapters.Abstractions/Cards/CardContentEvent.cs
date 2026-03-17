using HPD.Agent;
using HPD.Agent.Adapters.Cards;

namespace HPD.Agent.Adapters;

/// <summary>
/// Emitted when the agent produces a structured card instead of (or after) text content.
/// Platform adapters convert this to Block Kit, Adaptive Cards, etc.
/// The fallback plain-text representation is produced by <see cref="CardFallbackText.From"/>
/// and used for mobile notifications, screen readers, and platforms that can't render blocks.
/// </summary>
public record CardContentEvent(CardElement Card) : AgentEvent;
