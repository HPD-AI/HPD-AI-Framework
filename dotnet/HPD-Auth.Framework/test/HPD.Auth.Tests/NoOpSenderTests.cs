using FluentAssertions;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Extensions;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies no-op email and SMS sender registrations (tests 5.1 – 5.3).
/// </summary>
public class NoOpSenderTests
{
    // ── 5.1 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Registers_NoOpEmailSender_By_Default()
    {
        var sp = ServiceProviderBuilder.Build(appName: "NoOp_Email_Default");
        using var scope = sp.CreateScope();

        var sender = scope.ServiceProvider.GetService<IHPDAuthEmailSender>();

        sender.Should().NotBeNull();
        sender!.GetType().Name.Should().Be("NoOpEmailSender");
    }

    // ── 5.2 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Does_Not_Override_Pre_Registered_EmailSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();

        // Register a custom sender BEFORE AddHPDAuth — TryAdd must skip the no-op.
        services.AddScoped<IHPDAuthEmailSender, CustomEmailSender>();
        services.AddHPDAuth(o => o.AppName = "NoOp_Email_Custom");

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IHPDAuthEmailSender>();

        sender.Should().BeOfType<CustomEmailSender>();
    }

    // ── 5.3 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAuth_Registers_NoOpSmsSender_By_Default()
    {
        var sp = ServiceProviderBuilder.Build(appName: "NoOp_Sms_Default");
        using var scope = sp.CreateScope();

        var sender = scope.ServiceProvider.GetService<IHPDAuthSmsSender>();

        sender.Should().NotBeNull();
        sender!.GetType().Name.Should().Be("NoOpSmsSender");
    }

    // ── 5.4 — Custom IHPDAuthSmsSender replaces NoOpSmsSender (TryAdd behaviour) ─

    [Fact]
    public void AddHPDAuth_Does_Not_Override_Pre_Registered_SmsSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();

        // Register a custom sender BEFORE AddHPDAuth — TryAdd must skip the no-op.
        services.AddScoped<IHPDAuthSmsSender, CustomSmsSender>();
        services.AddHPDAuth(o => o.AppName = "NoOp_Sms_Custom");

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IHPDAuthSmsSender>();

        sender.Should().BeOfType<CustomSmsSender>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Stubs used by tests 5.2 and 5.4
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class CustomEmailSender : IHPDAuthEmailSender
    {
        public Task SendEmailConfirmationAsync(string email, string userId, string token, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendPasswordResetAsync(string email, string userId, string token, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendMagicLinkAsync(string email, string link, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendLoginAlertAsync(string email, string ip, string device, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CustomSmsSender : IHPDAuthSmsSender
    {
        public Task SendOtpAsync(string phoneNumber, string code, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendVerificationAsync(string phoneNumber, string code, CancellationToken ct = default) => Task.CompletedTask;
    }
}
