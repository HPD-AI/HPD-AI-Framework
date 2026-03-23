using HPD.Auth.Core.Options;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Tests.Helpers;

/// <summary>
/// Shared helper for building a minimal ServiceProvider that satisfies all
/// HPD.Auth DI dependencies without needing a full WebApplication host.
/// </summary>
internal static class ServiceProviderBuilder
{
    /// <summary>
    /// Builds a ServiceProvider with HPD.Auth registered using an optional configure action.
    /// Each caller should supply a unique AppName to avoid sharing an in-memory DB between tests.
    /// </summary>
    public static ServiceProvider Build(Action<HPDAuthOptions>? configure = null, string appName = "UnitTestApp")
    {
        var services = new ServiceCollection();

        // Logging is required by AuditLogStore, NoOpEmailSender, etc.
        services.AddLogging();

        // IHttpContextAccessor is required by SignInManager.
        services.AddHttpContextAccessor();

        services.AddHPDAuth(o =>
        {
            o.AppName = appName;
            configure?.Invoke(o);
        });

        return services.BuildServiceProvider();
    }
}
