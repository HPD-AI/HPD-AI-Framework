using FluentAssertions;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Verifies that UseHPDAuth() integrates cleanly into the ASP.NET Core middleware pipeline (tests 8.1 – 8.2).
/// </summary>
public class UseHPDAuthMiddlewareTests
{
    // ── 8.1 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void UseHPDAuth_Does_Not_Throw()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthorization();
        builder.Services.AddHPDAuth(o => o.AppName = "Middleware_NoThrow");
        var app = builder.Build();

        var act = () => app.UseHPDAuth();

        act.Should().NotThrow();
    }

    // ── 8.2 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void UseHPDAuth_Returns_Same_ApplicationBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddLogging();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthorization();
        builder.Services.AddHPDAuth(o => o.AppName = "Middleware_Fluent");
        var app = builder.Build();

        var result = app.UseHPDAuth();

        result.Should().BeSameAs(app);
    }
}
