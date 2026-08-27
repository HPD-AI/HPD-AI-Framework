using HPD.Agent.Providers;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.TUI.Interactions;

/// <summary>
/// Presents provider browser and device authorization challenges through the TUI dialog host.
/// </summary>
/// <remarks>
/// The adapter does not launch a browser or create a callback listener. Browser callback URIs
/// remain transient dialog input and are returned directly to the authentication coordinator.
/// </remarks>
public sealed class AgentTuiProviderAuthorizationInteraction : IProviderAuthorizationInteraction
{
    private readonly IAgentTuiDialogService _dialogs;

    /// <summary>Creates a provider authorization interaction over the supplied dialog service.</summary>
    /// <param name="dialogs">The active TUI dialog boundary.</param>
    public AgentTuiProviderAuthorizationInteraction(IAgentTuiDialogService dialogs) =>
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    /// <inheritdoc />
    public async ValueTask<ProviderAuthorizationResponse> AuthorizeAsync(
        ProviderAuthorizationChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return challenge switch
        {
            BrowserAuthorizationChallenge browser =>
                await AuthorizeBrowserAsync(browser, cancellationToken).ConfigureAwait(false),
            DeviceAuthorizationChallenge device =>
                await AuthorizeDeviceAsync(device, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(challenge), challenge.GetType().Name,
                "The TUI does not support this provider authorization challenge.")
        };
    }

    private async ValueTask<ProviderAuthorizationResponse> AuthorizeBrowserAsync(
        BrowserAuthorizationChallenge challenge,
        CancellationToken cancellationToken)
    {
        var result = await _dialogs.InputAsync(
            $"Authorize {challenge.ProviderKey}/{challenge.BackendKey} in a browser, then paste the complete callback URI. Open: {challenge.AuthorizationUri}",
            allowEmpty: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSubmitted || !Uri.TryCreate(result.Value, UriKind.Absolute, out var callback))
            throw new OperationCanceledException("Provider browser authorization was canceled.", cancellationToken);
        return new BrowserAuthorizationResponse
        {
            TransactionId = challenge.TransactionId,
            CallbackUri = callback
        };
    }

    private async ValueTask<ProviderAuthorizationResponse> AuthorizeDeviceAsync(
        DeviceAuthorizationChallenge challenge,
        CancellationToken cancellationToken)
    {
        var uri = challenge.VerificationUriComplete ?? challenge.VerificationUri;
        var result = await _dialogs.ConfirmAsync(
            $"Open {uri} and enter code {challenge.UserCode}. Mark this challenge as presented?",
            defaultValue: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new DeviceAuthorizationPresentationResponse
        {
            TransactionId = challenge.TransactionId,
            Action = result.IsSubmitted && result.Value
                ? ProviderDeviceAuthorizationAction.Presented
                : ProviderDeviceAuthorizationAction.Cancel
        };
    }
}
