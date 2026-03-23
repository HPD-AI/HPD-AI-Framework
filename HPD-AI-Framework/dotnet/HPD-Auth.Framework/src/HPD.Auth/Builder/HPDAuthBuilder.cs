using HPD.Auth.Core.Options;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Builder;

/// <summary>
/// Default implementation of <see cref="IHPDAuthBuilder"/>.
///
/// Acts as a simple carrier: stores the <see cref="IServiceCollection"/> and the
/// resolved <see cref="HPDAuthOptions"/> so that downstream extension packages can
/// access them without re-parsing configuration. Contains no registration logic of
/// its own — all registration is performed by
/// <see cref="Extensions.HPDAuthServiceCollectionExtensions.AddHPDAuth"/>.
/// </summary>
internal sealed class HPDAuthBuilder : IHPDAuthBuilder
{
    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public HPDAuthOptions Options { get; }

    /// <summary>
    /// Initialises the builder with the service collection and options instance
    /// produced by <see cref="Extensions.HPDAuthServiceCollectionExtensions.AddHPDAuth"/>.
    /// </summary>
    /// <param name="services">The application's <see cref="IServiceCollection"/>.</param>
    /// <param name="options">The fully-configured <see cref="HPDAuthOptions"/>.</param>
    public HPDAuthBuilder(IServiceCollection services, HPDAuthOptions options)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }
}
