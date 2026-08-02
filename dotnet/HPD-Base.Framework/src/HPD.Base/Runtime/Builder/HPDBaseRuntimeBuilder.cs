using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

/// <summary>Represents a hpdbase runtime builder.</summary>
public sealed class HPDBaseRuntimeBuilder : IHPDBaseRuntimeBuilder
{
    /// <summary>Initializes a new instance.</summary>
    public HPDBaseRuntimeBuilder(IServiceCollection services, HPDBaseRuntimeOptions options)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Gets the services.</summary>
    public IServiceCollection Services { get; }
    /// <summary>Gets the options.</summary>
    public HPDBaseRuntimeOptions Options { get; }
}
