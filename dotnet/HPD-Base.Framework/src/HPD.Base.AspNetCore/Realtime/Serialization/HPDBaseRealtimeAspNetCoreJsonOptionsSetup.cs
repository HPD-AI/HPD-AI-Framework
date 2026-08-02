using HPD.Base;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

internal sealed class HPDBaseRealtimeAspNetCoreJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    /// <summary>Executes the configure operation.</summary>
    public void Configure(JsonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.SerializerOptions.TypeInfoResolverChain.Add(HPDBaseRealtimeJsonSerializerContext.Default);
    }
}
