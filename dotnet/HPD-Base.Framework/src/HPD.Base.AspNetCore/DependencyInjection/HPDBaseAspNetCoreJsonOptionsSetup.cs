using HPD.Base.AspNetCore.Serialization;
using HPD.Base.Runtime.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore.DependencyInjection;

internal sealed class HPDBaseAspNetCoreJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    private readonly IBaseJsonOptionsProvider _baseJsonOptionsProvider;

    public HPDBaseAspNetCoreJsonOptionsSetup(IBaseJsonOptionsProvider baseJsonOptionsProvider)
    {
        _baseJsonOptionsProvider = baseJsonOptionsProvider;
    }

    public void Configure(JsonOptions options)
    {
        options.SerializerOptions.TypeInfoResolverChain.Insert(0, HPDBaseAspNetCoreJsonSerializerContext.Default);

        var runtimeResolver = _baseJsonOptionsProvider.Options.TypeInfoResolver;
        if (runtimeResolver is not null)
            options.SerializerOptions.TypeInfoResolverChain.Add(runtimeResolver);
    }
}
