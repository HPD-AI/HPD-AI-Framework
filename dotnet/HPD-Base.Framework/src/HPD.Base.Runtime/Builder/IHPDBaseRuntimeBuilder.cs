using HPD.Base.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Builder;

public interface IHPDBaseRuntimeBuilder
{
    IServiceCollection Services { get; }
    HPDBaseRuntimeOptions Options { get; }
}
