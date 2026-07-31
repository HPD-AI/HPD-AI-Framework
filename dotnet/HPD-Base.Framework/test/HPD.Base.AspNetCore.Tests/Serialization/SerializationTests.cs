using Microsoft.AspNetCore.Mvc;

namespace HPD.Base.AspNetCore.Tests.Serialization;

public sealed class SerializationTests
{
    [Fact]
    public async Task RuntimeJsonSerializesWireTypesWithoutReflectionFallback()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var options = app.Services.GetRequiredService<IHPDBaseRuntime>().Json.Options;

        options.GetTypeInfo(typeof(RecordId)).Should().NotBeNull();
        options.GetTypeInfo(typeof(RevisionToken)).Should().NotBeNull();
        options.GetTypeInfo(typeof(RecordQuery)).Should().NotBeNull();
        options.GetTypeInfo(typeof(RecordEnvelope)).Should().NotBeNull();
        options.GetTypeInfo(typeof(RecordPage)).Should().NotBeNull();
    }

    [Fact]
    public async Task AspNetJsonSerializesProblemDetails()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var options = app.HttpJsonOptions().SerializerOptions;

        var json = JsonSerializer.Serialize(new ProblemDetails
        {
            Status = 400,
            Title = "Validation failed",
            Extensions = { ["hpd.error.code"] = "code" }
        }, HPD.Base.AspNetCore.HPDBaseAspNetCoreJsonSerializerContext.Default.ProblemDetails);

        json.Should().Contain("hpd.error.code");
        options.GetTypeInfo(typeof(ProblemDetails)).Should().NotBeNull();
    }

    [Fact]
    public void PrimitiveConvertersUseStringWireShape()
    {
        JsonSerializer.Serialize(new RecordId("abc"), HPDBaseJsonSerializerContext.Default.RecordId).Should().Be("\"abc\"");
        JsonSerializer.Serialize(new RevisionToken("r1"), HPDBaseJsonSerializerContext.Default.RevisionToken).Should().Be("\"r1\"");
    }
}
