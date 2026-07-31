using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

public interface IHPDBaseRuntimeBuilder
{
    IServiceCollection Services { get; }
    HPDBaseRuntimeOptions Options { get; }
}
