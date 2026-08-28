using FluentAssertions;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Extensions;
using HPD.Auth.Testing;
using HPD.Auth.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task NoOpSenders_DoNotLogDeliverySecretsOrRecipientIdentifiers()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();

        services.AddHttpContextAccessor();
        services.AddLogging(builder => builder.AddProvider(loggerProvider));
        services
            .AddHPDAuth(o => o.AppName = "NoOp_Safe_Logs")
            .UseBaseTestHost("NoOp_Safe_Logs");

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var emailSender = scope.ServiceProvider.GetRequiredService<IHPDAuthEmailSender>();
        var smsSender = scope.ServiceProvider.GetRequiredService<IHPDAuthSmsSender>();

        await emailSender.SendEmailConfirmationAsync("alice@example.com", "user-secret-id", "confirm-secret-token");
        await emailSender.SendPasswordResetAsync("bob@example.com", "reset-user-id", "reset-secret-token");
        await emailSender.SendMagicLinkAsync("carol@example.com", "https://app.example.test/magic?token=magic-secret-token");
        await emailSender.SendLoginAlertAsync("dave@example.com", "203.0.113.42", "Sensitive Device Name");
        await smsSender.SendOtpAsync("+15551234567", "123456");
        await smsSender.SendVerificationAsync("+15557654321", "654321");

        var logText = string.Join('\n', loggerProvider.Messages);

        logText.Should().Contain("NoOpEmailSender");
        logText.Should().Contain("NoOpSmsSender");
        logText.Should().Contain("NOT sent");
        logText.Should().NotContain("alice@example.com");
        logText.Should().NotContain("bob@example.com");
        logText.Should().NotContain("carol@example.com");
        logText.Should().NotContain("dave@example.com");
        logText.Should().NotContain("user-secret-id");
        logText.Should().NotContain("reset-user-id");
        logText.Should().NotContain("confirm-secret-token");
        logText.Should().NotContain("reset-secret-token");
        logText.Should().NotContain("magic-secret-token");
        logText.Should().NotContain("203.0.113.42");
        logText.Should().NotContain("Sensitive Device Name");
        logText.Should().NotContain("+15551234567");
        logText.Should().NotContain("+15557654321");
        logText.Should().NotContain("123456");
        logText.Should().NotContain("654321");
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

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly object _gate = new();

        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private void Add(string categoryName, LogLevel logLevel, string message)
        {
            lock (_gate)
            {
                Messages.Add($"{logLevel}: {categoryName}: {message}");
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly CapturingLoggerProvider _provider;
            private readonly string _categoryName;

            public CapturingLogger(CapturingLoggerProvider provider, string categoryName)
            {
                _provider = provider;
                _categoryName = categoryName;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);

                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                {
                    message += " " + string.Join(" ", values.Select(value => $"{value.Key}={value.Value}"));
                }

                _provider.Add(_categoryName, logLevel, message);
            }
        }
    }
}
