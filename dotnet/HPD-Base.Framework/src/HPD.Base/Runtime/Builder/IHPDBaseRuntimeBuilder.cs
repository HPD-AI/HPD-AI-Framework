using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

/// <summary>Defines the ihpdbase runtime builder contract.</summary>
public interface IHPDBaseRuntimeBuilder
{
    /// <summary>Gets the services.</summary>
    IServiceCollection Services { get; }
    /// <summary>Gets the options.</summary>
    HPDBaseRuntimeOptions Options { get; }
}
