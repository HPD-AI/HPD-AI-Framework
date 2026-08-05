using HPD.Base;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore;

internal sealed class HPDBaseFilesAspNetCoreJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    /// <summary>Executes the configure operation.</summary>
    public void Configure(JsonOptions options)
    {
        options.SerializerOptions.TypeInfoResolverChain.Insert(0, HPDBaseFilesJsonSerializerContext.Default);
    }
}
