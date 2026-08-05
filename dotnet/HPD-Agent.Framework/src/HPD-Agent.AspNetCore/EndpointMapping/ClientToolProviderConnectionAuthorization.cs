// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.ClientTools;
using Microsoft.AspNetCore.Http;

namespace HPD.Agent.AspNetCore;

/// <summary>
/// Authorizes an application-scoped provider attachment before WebSocket
/// upgrade. Production gateways implement this contract; the explicit
/// development endpoint can run without an implementation.
/// </summary>
public interface IClientToolProviderConnectionAuthorizer
{
    ValueTask<ClientToolProviderConnectionAuthorization?> AuthorizeAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Server-verified attachment authority projected into one provider
/// connection.
/// </summary>
public sealed record ClientToolProviderConnectionAuthorization
{
    public required ClientToolProviderRuntimeIdentity RuntimeIdentity { get; init; }
    public required string ExpectedProviderName { get; init; }
    public required string ExpectedAppKind { get; init; }
    public string? AcceptedWebSocketSubprotocol { get; init; }
    public IAsyncDisposable? ConnectionLease { get; init; }
}
