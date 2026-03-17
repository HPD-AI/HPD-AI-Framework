# Send SMS

HPD.Auth uses a no-op SMS sender by default. Implement `IHPDAuthSmsSender` to send real SMS messages for phone verification and OTP delivery.

## Implement IHPDAuthSmsSender

```csharp
using HPD.Auth.Core.Interfaces;

public class TwilioSmsSender : IHPDAuthSmsSender
{
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _fromNumber;

    public TwilioSmsSender(IConfiguration config)
    {
        _accountSid = config["Twilio:AccountSid"]!;
        _authToken = config["Twilio:AuthToken"]!;
        _fromNumber = config["Twilio:FromNumber"]!;
    }

    public async Task SendOtpAsync(string phoneNumber, string code, CancellationToken ct = default)
    {
        await SendAsync(phoneNumber, $"Your verification code is: {code}", ct);
    }

    public async Task SendVerificationAsync(string phoneNumber, string code, CancellationToken ct = default)
    {
        await SendAsync(phoneNumber, $"Your login code is: {code}", ct);
    }

    private async Task SendAsync(string to, string message, CancellationToken ct)
    {
        // Use Twilio SDK or any HTTP client
    }
}
```

## Register it in DI

```csharp
builder.Services.AddScoped<IHPDAuthSmsSender, TwilioSmsSender>();

builder.Services.AddHPDAuth(options => { ... }).AddAuthentication();
```

## Interface reference

```csharp
public interface IHPDAuthSmsSender
{
    // Deliver a one-time password (e.g., for phone number verification)
    Task SendOtpAsync(string phoneNumber, string code, CancellationToken ct = default);

    // Deliver a login verification code
    Task SendVerificationAsync(string phoneNumber, string code, CancellationToken ct = default);
}
```
