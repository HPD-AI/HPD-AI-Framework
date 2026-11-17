using System.Collections.Concurrent;

namespace HPD.AI.Platform.Studio;

/// <summary>Owns bounded host-lifetime command previews, executions, outcomes, continuations, and fresh authority.</summary>
internal sealed class StudioCommandAuthorityRegistry
{
    internal ConcurrentDictionary<string, FreshChallengeState> Continuations { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, FreshAuthorityState> Authorities { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, CommandOutcome> Outcomes { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, CommandExecutionIdentity> Executions { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, CommandPreviewEvidence> Previews { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, FreshAcquisitionState> FreshAcquisitions { get; } = new(StringComparer.Ordinal);
    internal int PendingFreshAcquisitions;
    internal object FreshRegistryGate { get; } = new();
}

internal sealed class FreshAuthorityState(StudioFreshAuthenticationAuthority authority)
{ internal StudioFreshAuthenticationAuthority Authority { get; } = authority; internal int Consumed; }
internal sealed class FreshAcquisitionState(StudioFreshAuthenticationBinding binding, StudioFreshAuthenticationRequest request)
{
    internal StudioFreshAuthenticationBinding Binding { get; } = binding;
    internal StudioFreshAuthenticationRequest Request { get; } = request;
    internal DateTimeOffset ExpiresAtUtc => Binding.ExpiresAtUtc;
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal Task<StudioAuthenticationResult<StudioFreshAuthenticationResult>>? Operation;
    internal StudioAuthenticationResult<StudioFreshAuthenticationResult>? Result;
}
internal sealed class FreshChallengeState(StudioFreshAuthenticationContinuation continuation,
    StudioFreshAuthenticationBrowserAction browserAction)
{
    internal StudioFreshAuthenticationContinuation Continuation { get; } = continuation;
    internal StudioFreshAuthenticationBrowserAction BrowserAction { get; } = browserAction;
    internal SemaphoreSlim CompletionGate { get; } = new(1, 1);
    internal Task<StudioAuthenticationResult<StudioFreshAuthenticationResult>>? CompletionOperation;
    internal StudioAuthenticationResult<StudioFreshAuthenticationResult>? TerminalResult;
}
internal sealed record CommandExecutionIdentity(string Key, string CommandId, StudioSha256 TargetChecksum,
    StudioSha256 PreviewChecksum, StudioSha256 RequestChecksum, StudioSha256 SessionChecksum,
    StudioSha256 ProtectedScopeChecksum, string PreviewKey, string? ProtectedAuthority,
    DateTimeOffset RetainThroughUtc, bool ReceiptOnly = false);
internal sealed record CommandOutcome(string CommandId, StudioSha256 TargetChecksum, StudioSha256 PreviewChecksum,
    StudioSha256 RequestChecksum, StudioSha256 SessionChecksum, StudioSha256 ProtectedScopeChecksum,
    byte[] Result, DateTimeOffset RetainThroughUtc);
internal sealed record CommandPreviewInvocation(string CommandId, string PageId, StudioResourceIdentity Target,
    StudioSessionObservation Session);
internal sealed record CommandPreviewEvidence(string PageId, StudioSha256 TargetChecksum, byte[] CanonicalBytes,
    DateTimeOffset ExpiresAtUtc);
