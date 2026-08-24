using System.Collections.Concurrent;

namespace HPD.AI.Platform.Studio;

/// <summary>Owns bounded host-lifetime command previews, executions, outcomes, continuations, and fresh authority.</summary>
internal sealed class BaseStudioCommandAuthorityRegistry
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

internal sealed class FreshAuthorityState(BaseStudioFreshAuthenticationAuthority authority)
{ internal BaseStudioFreshAuthenticationAuthority Authority { get; } = authority; internal int Consumed; }
internal sealed class FreshAcquisitionState(BaseStudioFreshAuthenticationBinding binding, BaseStudioFreshAuthenticationRequest request)
{
    internal BaseStudioFreshAuthenticationBinding Binding { get; } = binding;
    internal BaseStudioFreshAuthenticationRequest Request { get; } = request;
    internal DateTimeOffset ExpiresAtUtc => Binding.ExpiresAtUtc;
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal Task<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>>? Operation;
    internal BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>? Result;
}
internal sealed class FreshChallengeState(BaseStudioFreshAuthenticationContinuation continuation,
    BaseStudioFreshAuthenticationBrowserAction browserAction)
{
    internal BaseStudioFreshAuthenticationContinuation Continuation { get; } = continuation;
    internal BaseStudioFreshAuthenticationBrowserAction BrowserAction { get; } = browserAction;
    internal SemaphoreSlim CompletionGate { get; } = new(1, 1);
    internal Task<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>>? CompletionOperation;
    internal BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>? TerminalResult;
}
internal sealed record CommandExecutionIdentity(string Key, string CommandId, BaseStudioSha256 TargetChecksum,
    BaseStudioSha256 PreviewChecksum, BaseStudioSha256 RequestChecksum, BaseStudioSha256 SessionChecksum,
    BaseStudioSha256 ProtectedScopeChecksum, string PreviewKey, string? ProtectedAuthority,
    DateTimeOffset RetainThroughUtc, bool ReceiptOnly = false);
internal sealed record CommandOutcome(string CommandId, BaseStudioSha256 TargetChecksum, BaseStudioSha256 PreviewChecksum,
    BaseStudioSha256 RequestChecksum, BaseStudioSha256 SessionChecksum, BaseStudioSha256 ProtectedScopeChecksum,
    byte[] Result, DateTimeOffset RetainThroughUtc);
internal sealed record CommandPreviewInvocation(string CommandId, string PageId, BaseStudioResourceIdentity Target,
    BaseStudioSessionObservation Session);
internal sealed record CommandPreviewEvidence(string PageId, BaseStudioSha256 TargetChecksum, byte[] CanonicalBytes,
    DateTimeOffset ExpiresAtUtc);
