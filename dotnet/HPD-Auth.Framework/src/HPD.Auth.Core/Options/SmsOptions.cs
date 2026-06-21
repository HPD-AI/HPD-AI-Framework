namespace HPD.Auth.Core.Options;

/// <summary>
/// Configuration for SMS-based OTP delivery.
/// </summary>
public class SmsOptions
{
    /// <summary>
    /// Whether SMS delivery is enabled. Defaults to false.
    /// Requires an IHPDAuthSmsSender implementation to be registered.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// OTP code length (number of digits). Defaults to 6.
    /// </summary>
    public int OtpLength { get; set; } = 6;

    /// <summary>
    /// How long an SMS OTP remains valid. Defaults to 10 minutes.
    /// </summary>
    public TimeSpan OtpLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Minimum interval between SMS sends to the same phone number.
    /// Prevents SMS flooding and limits carrier costs.
    /// Defaults to 60 seconds.
    /// </summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Sender ID or phone number to use as the SMS "From" field.
    /// Format depends on carrier and country (e.g., "+15551234567" or "MyApp").
    /// </summary>
    public string? SenderId { get; set; }
}
