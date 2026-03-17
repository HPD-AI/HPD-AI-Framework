using Microsoft.Extensions.AI;

namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Full-fidelity data transfer object for a chat message.
/// Carries the complete Contents list (tool calls, tool results, reasoning, images, etc.)
/// using the M.E.AI polymorphic $type discriminator format.
/// </summary>
/// <param name="Id">Stable message identifier (GUID assigned at AddMessage time)</param>
/// <param name="Role">Message role ('user' | 'assistant' | 'system' | 'tool')</param>
/// <param name="Contents">All content items in this message. UsageContent is excluded (billing metadata, not conversation content).</param>
/// <param name="AuthorName">Optional author name (used by some providers for multi-agent scenarios)</param>
/// <param name="Timestamp">When this message was created (ISO 8601 format)</param>
public record MessageDto(
    string Id,
    string Role,
    List<AIContent> Contents,
    string? AuthorName,
    string Timestamp);
