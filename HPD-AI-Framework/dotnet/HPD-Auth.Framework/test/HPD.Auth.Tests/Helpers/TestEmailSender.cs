using HPD.Auth.Core.Interfaces;

namespace HPD.Auth.Tests.Helpers;

/// <summary>
/// Test double for <see cref="IHPDAuthEmailSender"/> that records every call
/// so tests can assert which emails were (or were not) sent.
/// </summary>
internal sealed class TestEmailSender : IHPDAuthEmailSender
{
    public List<(string Email, string UserId, string Token)> ConfirmationsSent { get; } = [];
    public List<(string Email, string UserId, string Token)> PasswordResetsSent { get; } = [];

    public Task SendEmailConfirmationAsync(string email, string userId, string token, CancellationToken ct = default)
    {
        ConfirmationsSent.Add((email, userId, token));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string email, string userId, string token, CancellationToken ct = default)
    {
        PasswordResetsSent.Add((email, userId, token));
        return Task.CompletedTask;
    }

    public Task SendMagicLinkAsync(string email, string link, CancellationToken ct = default) => Task.CompletedTask;
    public Task SendLoginAlertAsync(string email, string ip, string device, CancellationToken ct = default) => Task.CompletedTask;
}
