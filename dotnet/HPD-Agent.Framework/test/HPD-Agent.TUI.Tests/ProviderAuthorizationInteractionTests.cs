using HPD.Agent.Providers;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Tests;

public sealed class ProviderAuthorizationInteractionTests
{
    [Fact]
    public async Task BrowserChallenge_ReturnsTransientCorrelatedCallback()
    {
        var dialogs = new DialogStub
        {
            InputResult = AgentTuiDialogResult<string>.Submitted("http://127.0.0.1/callback?code=transient")
        };
        var interaction = new AgentTuiProviderAuthorizationInteraction(dialogs);

        var response = Assert.IsType<BrowserAuthorizationResponse>(await interaction.AuthorizeAsync(
            new BrowserAuthorizationChallenge
            {
                TransactionId = "transaction",
                ProviderKey = "provider",
                BackendKey = "backend",
                AccountId = "account",
                AuthorizationUri = new Uri("https://issuer.test/authorize"),
                RedirectUri = new Uri("http://127.0.0.1/callback")
            }));

        Assert.Equal("transaction", response.TransactionId);
        Assert.Equal("http://127.0.0.1/callback?code=transient", response.CallbackUri.AbsoluteUri);
    }

    [Fact]
    public async Task DeviceChallenge_MapsDismissalToExplicitCancellation()
    {
        var interaction = new AgentTuiProviderAuthorizationInteraction(new DialogStub());

        var response = Assert.IsType<DeviceAuthorizationPresentationResponse>(await interaction.AuthorizeAsync(
            new DeviceAuthorizationChallenge
            {
                TransactionId = "transaction",
                ProviderKey = "provider",
                BackendKey = "backend",
                AccountId = "account",
                VerificationUri = new Uri("https://issuer.test/device"),
                UserCode = "ABCD-EFGH"
            }));

        Assert.Equal(ProviderDeviceAuthorizationAction.Cancel, response.Action);
    }

    private sealed class DialogStub : IAgentTuiDialogService
    {
        public AgentTuiDialogResult<string> InputResult { get; init; } = AgentTuiDialogResult<string>.Dismissed();
        public bool HasOpenDialog => false;
        public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentTuiDialogResult<TResult>.Dismissed());
        public bool Close(string key) => false;
        public bool CloseTop() => false;
        public Task<AgentTuiDialogResult<bool>> ConfirmAsync(string title, bool? defaultValue = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentTuiDialogResult<bool>.Dismissed());
        public Task<AgentTuiDialogResult<T>> SelectAsync<T>(string title, IReadOnlyList<T> options,
            Func<T, string> titleSelector, CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentTuiDialogResult<T>.Dismissed());
        public Task<AgentTuiDialogResult<string>> InputAsync(string title, string? defaultValue = null,
            bool allowEmpty = false, CancellationToken cancellationToken = default) => Task.FromResult(InputResult);
        public Task<AgentTuiDialogResult<string>> SecretInputAsync(string title, bool allowEmpty = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentTuiDialogResult<string>.Dismissed());
    }
}
