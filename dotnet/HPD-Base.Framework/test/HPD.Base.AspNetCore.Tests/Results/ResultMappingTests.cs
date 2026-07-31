using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore.Tests.Results;

public sealed class ResultMappingTests
{
    [Theory]
    [InlineData(OperationStatus.Ok, 200)]
    [InlineData(OperationStatus.Created, 201)]
    [InlineData(OperationStatus.Updated, 200)]
    [InlineData(OperationStatus.Deleted, 200)]
    [InlineData(OperationStatus.NoContent, 204)]
    public async Task SuccessStatusesMapToHttpStatusCodes(OperationStatus status, int expected)
    {
        await using var app = await TestBaseApp.CreateAsync();
        var mapper = app.Services.GetRequiredService<IBaseHttpResultMapper>();
        var context = new DefaultHttpContext { RequestServices = app.Services };

        var result = status == OperationStatus.NoContent
            ? mapper.ToHttpResult(new OperationResult { Status = status }, context, new HPDBaseHttpResultMappingContext())
            : mapper.ToHttpResult(new OperationResult<string> { Status = status, Value = "ok" }, context, new HPDBaseHttpResultMappingContext());

        await result.ExecuteAsync(context);
        context.Response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(OperationStatus.ValidationFailed, 400)]
    [InlineData(OperationStatus.NotFound, 404)]
    [InlineData(OperationStatus.Conflict, 409)]
    [InlineData(OperationStatus.PolicyDenied, 403)]
    [InlineData(OperationStatus.Unauthorized, 401)]
    [InlineData(OperationStatus.Unsupported, 400)]
    [InlineData(OperationStatus.CapabilityUnavailable, 424)]
    [InlineData(OperationStatus.StoreError, 500)]
    public async Task FailureStatusesMapToProblemDetails(OperationStatus status, int expected)
    {
        await using var app = await TestBaseApp.CreateAsync();
        var mapper = app.Services.GetRequiredService<IBaseHttpResultMapper>();
        var context = new DefaultHttpContext { RequestServices = app.Services };

        await mapper.ToHttpResult(new OperationResult<string>
        {
            Status = status,
            Error = new BaseError { Code = "code", Message = "message", Category = ErrorCategory.Validation }
        }, context, new HPDBaseHttpResultMappingContext { CorrelationId = "corr" }).ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(expected);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be("corr");
    }

    [Fact]
    public async Task HeadersAreMappedFromRevisionEventsAndHints()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var mapper = app.Services.GetRequiredService<IBaseHttpResultMapper>();
        var context = new DefaultHttpContext { RequestServices = app.Services };

        await mapper.ToHttpResult(new OperationResult<string>
        {
            Status = OperationStatus.Created,
            Value = "ok",
            Revision = new RevisionInfo { ETag = "W/\"1\"", Revision = "r1", LastModified = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture) },
            Events = [new EventReference { EventId = "e1", Type = "record.created" }]
        }, context, new HPDBaseHttpResultMappingContext
        {
            Location = "/records/1",
            CorrelationId = "corr",
            RetryAfter = TimeSpan.FromSeconds(2),
            PreferenceApplied = ["return=representation"]
        }).ExecuteAsync(context);

        context.Response.Headers.ETag.ToString().Should().Be("W/\"1\"");
        context.Response.Headers["HPD-Base-Revision"].ToString().Should().Be("r1");
        context.Response.Headers.Location.ToString().Should().Be("/records/1");
        context.Response.Headers["HPD-Base-Event-Ids"].ToString().Should().Be("e1");
        context.Response.Headers["Retry-After"].ToString().Should().Be("2");
        context.Response.Headers["Preference-Applied"].ToString().Should().Be("return=representation");
    }
}
