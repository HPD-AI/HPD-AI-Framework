using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace HPD.Base.AspNetCore.Tests.DependencyInjection;

public sealed class JsonOptionsRegistrationTests
{
    [Fact]
    public async Task HttpJsonOptionsComposeAspNetAndRuntimeResolvers()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var options = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        options.TypeInfoResolverChain.Should().NotBeEmpty();
        options.GetTypeInfo(typeof(RecordCreateRequest)).Should().NotBeNull();
        options.GetTypeInfo(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails)).Should().NotBeNull();
    }
}
