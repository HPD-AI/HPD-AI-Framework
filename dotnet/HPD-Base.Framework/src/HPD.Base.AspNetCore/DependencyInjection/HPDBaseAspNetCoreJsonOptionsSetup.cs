using HPD.Base.AspNetCore;
using HPD.Base;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

internal sealed class HPDBaseAspNetCoreJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    private readonly IBaseJsonOptionsProvider _baseJsonOptionsProvider;

    /// <summary>Initializes a new instance.</summary>
    public HPDBaseAspNetCoreJsonOptionsSetup(IBaseJsonOptionsProvider baseJsonOptionsProvider)
    {
        _baseJsonOptionsProvider = baseJsonOptionsProvider;
    }

    /// <summary>Executes the configure operation.</summary>
    public void Configure(JsonOptions options)
    {
        options.SerializerOptions.TypeInfoResolverChain.Insert(0, HPDBaseAspNetCoreJsonSerializerContext.Default);

        var runtimeResolver = _baseJsonOptionsProvider.Options.TypeInfoResolver;
        if (runtimeResolver is not null)
            options.SerializerOptions.TypeInfoResolverChain.Add(runtimeResolver);
    }
}
