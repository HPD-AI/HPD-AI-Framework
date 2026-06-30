using HPD.Base.Files.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace HPD.Base.Files.AspNetCore.Serialization;

internal sealed class HPDBaseFilesAspNetCoreJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        options.SerializerOptions.TypeInfoResolverChain.Insert(0, HPDBaseFilesJsonSerializerContext.Default);
    }
}
