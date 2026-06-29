using HPD.Base.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Builder;

public sealed class HPDBaseRuntimeBuilder : IHPDBaseRuntimeBuilder
{
    public HPDBaseRuntimeBuilder(IServiceCollection services, HPDBaseRuntimeOptions options)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IServiceCollection Services { get; }
    public HPDBaseRuntimeOptions Options { get; }
}
