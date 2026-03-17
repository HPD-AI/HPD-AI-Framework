# Send Real Emails

By default, HPD.Auth uses a no-op email sender that logs a warning instead of sending email. This lets your app start without configuration, but you'll need a real sender for email confirmation, password reset, and magic links to work.

## Implement IHPDAuthEmailSender

Create a class that implements `IHPDAuthEmailSender`:

```csharp
using HPD.Auth.Core.Interfaces;

public class SendGridEmailSender : IHPDAuthEmailSender
{
    private readonly string _apiKey;
    private readonly ILogger<SendGridEmailSender> _logger;

    public SendGridEmailSender(IConfiguration config, ILogger<SendGridEmailSender> logger)
    {
        _apiKey = config["SendGrid:ApiKey"]
            ?? throw new InvalidOperationException("SendGrid API key is required");
        _logger = logger;
    }

    public async Task SendEmailConfirmationAsync(
        string email, string userId, string token, CancellationToken ct = default)
    {
        var link = $"https://yourapp.com/confirm?userId={userId}&token={Uri.EscapeDataString(token)}";
        await SendAsync(email, "Confirm your email", $"Click here to confirm: {link}", ct);
    }

    public async Task SendPasswordResetAsync(
        string email, string userId, string token, CancellationToken ct = default)
    {
        var link = $"https://yourapp.com/reset-password?userId={userId}&token={Uri.EscapeDataString(token)}";
        await SendAsync(email, "Reset your password", $"Click here to reset: {link}", ct);
    }

    public async Task SendMagicLinkAsync(
        string email, string link, CancellationToken ct = default)
    {
        await SendAsync(email, "Your login link", $"Click here to log in: {link}", ct);
    }

    public async Task SendLoginAlertAsync(
        string email, string ipAddress, string deviceInfo, CancellationToken ct = default)
    {
        await SendAsync(email, "New login detected",
            $"A new login was detected from {ipAddress} on {deviceInfo}.", ct);
    }

    private async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        // Use your preferred HTTP client or SendGrid SDK here
        _logger.LogInformation("Sending email to {Email}: {Subject}", to, subject);
        // ... actual send logic
    }
}
```

## Register it in DI

Register your implementation **before** calling `AddHPDAuth()`. HPD.Auth uses `TryAddScoped` internally, so your registration takes precedence:

```csharp
// Register BEFORE AddHPDAuth
builder.Services.AddScoped<IHPDAuthEmailSender, SendGridEmailSender>();

builder.Services.AddHPDAuth(options => { ... }).AddAuthentication();
```

Alternatively, register it after using `services.Replace()`:

```csharp
builder.Services.AddHPDAuth(options => { ... });

// Replace the no-op sender registered by AddHPDAuth
builder.Services.Replace(
    ServiceDescriptor.Scoped<IHPDAuthEmailSender, SendGridEmailSender>());
```

## Verify it works

With `Features.RequireEmailConfirmation = true`, sign up a new user and check that the confirmation email is sent (and not just logged).

## Interface reference

```csharp
public interface IHPDAuthEmailSender
{
    // Called when a new user signs up with RequireEmailConfirmation = true
    Task SendEmailConfirmationAsync(string email, string userId, string token,
        CancellationToken ct = default);

    // Called when a user requests a password reset via POST /api/auth/recover
    Task SendPasswordResetAsync(string email, string userId, string token,
        CancellationToken ct = default);

    // Called when a magic link is generated
    Task SendMagicLinkAsync(string email, string link,
        CancellationToken ct = default);

    // Called on login from a new IP/device (optional to implement)
    Task SendLoginAlertAsync(string email, string ipAddress, string deviceInfo,
        CancellationToken ct = default);
}
```
