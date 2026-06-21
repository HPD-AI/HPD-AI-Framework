namespace HPD.Auth.Core.Interfaces;

/// <summary>
/// Abstraction for sending HPD.Auth transactional SMS messages.
/// Implementations can use any SMS provider (Twilio, AWS SNS, etc.).
/// </summary>
public interface IHPDAuthSmsSender
{
    /// <summary>
    /// Send a one-time password (OTP) code for SMS-based two-factor authentication.
    /// </summary>
    /// <param name="phoneNumber">Recipient phone number in E.164 format (e.g., "+15551234567").</param>
    /// <param name="code">The numeric OTP code.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendOtpAsync(
        string phoneNumber,
        string code,
        CancellationToken ct = default);

    /// <summary>
    /// Send a phone number verification code during registration or phone change.
    /// </summary>
    /// <param name="phoneNumber">Recipient phone number in E.164 format.</param>
    /// <param name="code">The verification code.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendVerificationAsync(
        string phoneNumber,
        string code,
        CancellationToken ct = default);
}
