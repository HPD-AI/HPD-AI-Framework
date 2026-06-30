using HPD.Base.Realtime.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace HPD.Base.Realtime.AspNetCore.Serialization;

internal sealed class HPDBaseRealtimeAspNetCoreJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.SerializerOptions.TypeInfoResolverChain.Add(HPDBaseRealtimeJsonSerializerContext.Default);
    }
}
