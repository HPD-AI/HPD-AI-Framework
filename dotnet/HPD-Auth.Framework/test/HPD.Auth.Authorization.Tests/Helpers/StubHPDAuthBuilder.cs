using HPD.Auth.Builder;
using HPD.Auth.Core.Options;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.Auth.Authorization.Tests.Helpers;

/// <summary>
/// Minimal IHPDAuthBuilder for use in unit tests that only need to test
/// the AddAuthorization extension method without standing up the full HPD.Auth stack.
/// </summary>
internal sealed class StubHPDAuthBuilder : IHPDAuthBuilder
{
    public StubHPDAuthBuilder(IServiceCollection services)
    {
        Services = services;
        Options = new HPDAuthOptions();
    }

    public IServiceCollection Services { get; }
    public HPDAuthOptions Options { get; }
}
