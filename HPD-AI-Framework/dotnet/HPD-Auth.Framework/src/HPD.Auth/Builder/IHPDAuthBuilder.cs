using HPD.Auth.Core.Options;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Builder;

/// <summary>
/// Fluent builder returned by <see cref="Extensions.HPDAuthServiceCollectionExtensions.AddHPDAuth"/>.
///
/// Allows Phase 2/3 packages (e.g., HPD.Auth.PostgreSQL, HPD.Auth.Admin) to extend
/// the DI registration without requiring the caller to re-pass the options object.
///
/// Pattern: each extension package adds an extension method on IHPDAuthBuilder that
/// registers additional services and returns the same builder, enabling chaining:
///
/// <code>
/// services.AddHPDAuth(options => { ... })
///         .AddPostgreSQL()
///         .AddAdminApi();
/// </code>
/// </summary>
public interface IHPDAuthBuilder
{
    /// <summary>
    /// The <see cref="IServiceCollection"/> that AddHPDAuth() registered services into.
    /// Extension packages use this to register additional services.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The fully-configured <see cref="HPDAuthOptions"/> instance built during
    /// <see cref="Extensions.HPDAuthServiceCollectionExtensions.AddHPDAuth"/>.
    /// Extension packages can read these options to make conditional registrations.
    /// </summary>
    HPDAuthOptions Options { get; }
}
