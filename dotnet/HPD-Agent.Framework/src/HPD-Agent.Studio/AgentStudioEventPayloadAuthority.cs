// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Studio;

/// <summary>Configures optional event payload disclosure for Agent Studio.</summary>
public sealed class AgentStudioPayloadOptions
{
    /// <summary>Gets or sets whether the default payload authority permits disclosure.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the maximum sanitized payload length shown in Studio.</summary>
    public int MaximumLength { get; set; } = 32_768;
}

/// <summary>Authorizes one event payload read after normal Studio resource inspection succeeds.</summary>
public interface IAgentStudioEventPayloadAuthority
{
    /// <summary>Returns whether the current principal may inspect the selected event payload.</summary>
    ValueTask<bool> CanInspectAsync(
        ClaimsPrincipal principal,
        AgentEvent agentEvent,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fail-closed default payload authority. Payload inspection is enabled only when the host
/// explicitly opts in; production hosts should replace this with an HPD Auth-aware authority.
/// </summary>
public sealed class DefaultAgentStudioEventPayloadAuthority(
    IOptions<AgentStudioPayloadOptions> options) : IAgentStudioEventPayloadAuthority
{
    /// <inheritdoc />
    public ValueTask<bool> CanInspectAsync(
        ClaimsPrincipal principal,
        AgentEvent agentEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(options.Value.Enabled);
    }
}
