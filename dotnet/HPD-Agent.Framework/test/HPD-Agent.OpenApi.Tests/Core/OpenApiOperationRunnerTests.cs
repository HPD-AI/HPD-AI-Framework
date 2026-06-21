using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HPD.OpenApi.Core;
using HPD.OpenApi.Core.Model;

namespace HPD.Agent.OpenApi.Tests.Core;

public class OpenApiOperationRunnerTests
{
    // ────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────

    private static RestApiOperation GetOperation(
        string path = "/pets",
        HttpMethod? method = null,
        string? serverUrl = "https://api.example.com",
        string? operationId = "listPets",
        List<RestApiParameter>? parameters = null,
        RestApiPayload? payload = null) => new()
    {
        Id = operationId,
        Path = path,
        Method = method ?? HttpMethod.Get,
        ServerUrl = serverUrl,
        Parameters = parameters ?? [],
        Payload = payload
    };

    private static RestApiParameter PathParam(string name, bool required = true) => new()
    {
        Name = name,
        Type = "string",
        IsRequired = required,
        Location = RestApiParameterLocation.Path
    };

    private static RestApiParameter QueryParam(string name, bool required = false) => new()
    {
        Name = name,
        Type = "string",
        IsRequired = required,
        Location = RestApiParameterLocation.Query
    };

    private static RestApiParameter HeaderParam(string name, bool required = false) => new()
    {
        Name = name,
        Type = "string",
        IsRequired = required,
        Location = RestApiParameterLocation.Header
    };

    private static (OpenApiOperationRunner runner, List<HttpRequestMessage> captured) MakeRunner(
        HttpStatusCode status = HttpStatusCode.OK,
        string responseBody = "{}",
        Func<HttpRequestMessage, CancellationToken, Task>? authCallback = null,
        string? userAgent = null,
        bool enableDynamicPayload = true,
        bool enablePayloadNamespacing = false,
        Func<HttpResponseMessage, string?, OpenApiErrorResponse?>? errorDetector = null)
    {
        var captured = new List<HttpRequestMessage>();
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured.Add(req);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody)
            };
        });
        var client = new HttpClient(handler);
        var runner = new OpenApiOperationRunner(
            client, authCallback, userAgent, enableDynamicPayload, enablePayloadNamespacing, errorDetector);
        return (runner, captured);
    }

    /// <summary>
    /// Handler that captures request body string before the request is disposed.
    /// Needed for payload tests where the request is disposed after SendAsync returns.
    /// </summary>
    private static (OpenApiOperationRunner runner, List<string?> capturedBodies, List<string?> capturedContentTypes) MakeBodyCapturingRunner(
        HttpStatusCode status = HttpStatusCode.OK,
        string responseBody = "{}",
        bool enableDynamicPayload = true,
        bool enablePayloadNamespacing = false)
    {
        var bodies = new List<string?>();
        var contentTypes = new List<string?>();
        var handler = new AsyncFakeHttpMessageHandler(async req =>
        {
            // Read body before request is disposed
            if (req.Content != null)
            {
                bodies.Add(await req.Content.ReadAsStringAsync());
                contentTypes.Add(req.Content.Headers.ContentType?.MediaType);
            }
            else
            {
                bodies.Add(null);
                contentTypes.Add(null);
            }
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        });
        var client = new HttpClient(handler);
        var runner = new OpenApiOperationRunner(client, null, null, enableDynamicPayload, enablePayloadNamespacing);
        return (runner, bodies, contentTypes);
    }

    // ────────────────────────────────────────────────────────────
    // URL building
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_PathParameter_ReplacedAndUriEscaped()
    {
        var op = GetOperation(
            path: "/pets/{petId}",
            parameters: [PathParam("petId")]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op, new Dictionary<string, object?> { ["petId"] = "my pet" }, null, default);

        captured[0].RequestUri!.PathAndQuery.Should().Contain("/pets/my%20pet");
    }

    [Fact]
    public async Task Run_QueryParameter_AppendedToUrl()
    {
        var op = GetOperation(parameters: [QueryParam("limit")]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op, new Dictionary<string, object?> { ["limit"] = "10" }, null, default);

        captured[0].RequestUri!.Query.Should().Be("?limit=10");
    }

    [Fact]
    public async Task Run_MultipleQueryParameters_AllJoinedWithAmpersand()
    {
        var op = GetOperation(parameters: [QueryParam("limit"), QueryParam("status")]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["limit"] = "5", ["status"] = "active" },
            null, default);

        var query = captured[0].RequestUri!.Query;
        query.Should().Contain("limit=5");
        query.Should().Contain("status=active");
        query.Should().Contain("&");
    }

    [Fact]
    public async Task Run_SpecialCharactersInQueryValue_UriEscaped()
    {
        var op = GetOperation(parameters: [QueryParam("q")]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["q"] = "hello world & more" }, null, default);

        captured[0].RequestUri!.Query.Should().Contain("hello%20world%20%26%20more");
    }

    [Fact]
    public async Task Run_ServerUrlOverride_TakesPrecedenceOverOperationServerUrl()
    {
        var op = GetOperation(serverUrl: "https://original.example.com");
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op, new Dictionary<string, object?>(),
            new Uri("https://override.example.com"), default);

        captured[0].RequestUri!.Host.Should().Be("override.example.com");
    }

    [Fact]
    public async Task Run_NoServerUrlAndNoOverride_ThrowsInvalidOperationException()
    {
        var op = GetOperation(serverUrl: null);
        var (runner, _) = MakeRunner();

        var act = () => runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No server URL*");
    }

    [Fact]
    public async Task Run_MissingQueryParameterValue_NotAppendedToUrl()
    {
        var op = GetOperation(parameters: [QueryParam("limit")]);
        var (runner, captured) = MakeRunner();

        // "limit" arg not provided
        await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        captured[0].RequestUri!.Query.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────
    // Payload building
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_EnableDynamicPayload_BuildsJsonBody()
    {
        var op = GetOperation(
            path: "/pets",
            method: HttpMethod.Post,
            payload: new RestApiPayload
            {
                MediaType = "application/json",
                Properties =
                [
                    new RestApiPayloadProperty { Name = "name", Type = "string", IsRequired = true },
                    new RestApiPayloadProperty { Name = "tag", Type = "string" }
                ]
            });
        var (runner, bodies, _) = MakeBodyCapturingRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["name"] = "Fluffy", ["tag"] = "cat" },
            null, default);

        var json = JsonDocument.Parse(bodies[0]!).RootElement;
        json.GetProperty("name").GetString().Should().Be("Fluffy");
        json.GetProperty("tag").GetString().Should().Be("cat");
    }

    [Fact]
    public async Task Run_EnableDynamicPayloadFalse_SendsRawPayloadString()
    {
        var op = GetOperation(
            path: "/pets",
            method: HttpMethod.Post,
            payload: new RestApiPayload
            {
                MediaType = "application/json",
                Properties = []
            });
        var (runner, bodies, _) = MakeBodyCapturingRunner(enableDynamicPayload: false);

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["payload"] = """{"name":"Rex"}""" },
            null, default);

        bodies[0].Should().Be("""{"name":"Rex"}""");
    }

    [Fact]
    public async Task Run_RequiredPayloadPropertyMissing_ThrowsInvalidOperationException()
    {
        var op = GetOperation(
            path: "/pets",
            method: HttpMethod.Post,
            payload: new RestApiPayload
            {
                MediaType = "application/json",
                Properties =
                [
                    new RestApiPayloadProperty { Name = "name", Type = "string", IsRequired = true }
                ]
            });
        var (runner, _) = MakeRunner();

        var act = () => runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Required payload property 'name' is missing*");
    }

    [Fact]
    public async Task Run_EnablePayloadNamespacingTrue_UsesNamespacedKey()
    {
        var op = GetOperation(
            path: "/pets",
            method: HttpMethod.Post,
            payload: new RestApiPayload
            {
                MediaType = "application/json",
                Properties =
                [
                    new RestApiPayloadProperty
                    {
                        Name = "address",
                        Type = "object",
                        Properties =
                        [
                            new RestApiPayloadProperty { Name = "city", Type = "string" }
                        ]
                    }
                ]
            });
        var (runner, bodies, _) = MakeBodyCapturingRunner(enablePayloadNamespacing: true);

        // With namespacing, the arg key for "city" inside "address" is "address.city"
        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["address.city"] = "London" }, null, default);

        var json = JsonDocument.Parse(bodies[0]!).RootElement;
        json.GetProperty("address").GetProperty("city").GetString().Should().Be("London");
    }

    [Fact]
    public async Task Run_TextPlainMediaType_SendsStringBody()
    {
        var op = GetOperation(
            path: "/notes",
            method: HttpMethod.Post,
            payload: new RestApiPayload
            {
                MediaType = "text/plain",
                Properties = []
            });
        var (runner, bodies, contentTypes) = MakeBodyCapturingRunner(enableDynamicPayload: false);

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["payload"] = "plain text body" },
            null, default);

        contentTypes[0].Should().Be("text/plain");
        bodies[0].Should().Be("plain text body");
    }

    // ────────────────────────────────────────────────────────────
    // Header parameters
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_HeaderParameter_AppendedToRequest()
    {
        var op = GetOperation(parameters: [HeaderParam("X-Api-Key")]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["X-Api-Key"] = "my-secret" }, null, default);

        captured[0].Headers.TryGetValues("X-Api-Key", out var values).Should().BeTrue();
        values!.First().Should().Be("my-secret");
    }

    // ────────────────────────────────────────────────────────────
    // Response processing
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_Http200WithValidJson_ReturnsOpenApiOperationResponseWithJsonContent()
    {
        var op = GetOperation();
        var (runner, _) = MakeRunner(responseBody: """[{"id":1,"name":"Fluffy"}]""");

        var result = await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        result.Should().BeOfType<OpenApiOperationResponse>();
        var response = (OpenApiOperationResponse)result!;
        response.StatusCode.Should().Be(200);
        response.Content.Should().BeOfType<JsonElement>();
        ((JsonElement)response.Content!).ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Run_Http200WithNonJsonBody_ReturnsOpenApiOperationResponseWithStringContent()
    {
        var op = GetOperation();
        var (runner, _) = MakeRunner(responseBody: "plain text response");

        var result = await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        result.Should().BeOfType<OpenApiOperationResponse>();
        var response = (OpenApiOperationResponse)result!;
        response.Content.Should().Be("plain text response");
    }

    [Fact]
    public async Task Run_Http200WithEmptyBody_ReturnsOpenApiOperationResponseWithNullContent()
    {
        var op = GetOperation();
        var (runner, _) = MakeRunner(responseBody: "");

        var result = await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        result.Should().BeOfType<OpenApiOperationResponse>();
        var response = (OpenApiOperationResponse)result!;
        response.Content.Should().BeNull();
    }

    [Fact]
    public async Task Run_Http404_ReturnsOpenApiErrorResponse()
    {
        var op = GetOperation();
        var (runner, _) = MakeRunner(
            status: HttpStatusCode.NotFound,
            responseBody: """{"message":"Not Found"}""");

        var result = await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        result.Should().BeOfType<OpenApiErrorResponse>();
        var error = (OpenApiErrorResponse)result!;
        error.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Run_Http500_ReturnsOpenApiErrorResponseNotThrows()
    {
        var op = GetOperation();
        var (runner, _) = MakeRunner(status: HttpStatusCode.InternalServerError, responseBody: "Server Error");

        var act = () => runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        // Runner itself does NOT throw — it returns the error as data
        await act.Should().NotThrowAsync();
        var result = await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);
        result.Should().BeOfType<OpenApiErrorResponse>();
        ((OpenApiErrorResponse)result!).StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Run_RetryAfterDeltaHeader_PopulatedOnErrorResponse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("rate limited")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        var runner = new OpenApiOperationRunner(new HttpClient(handler));

        var result = await runner.RunAsync(GetOperation(), new Dictionary<string, object?>(), null, default);

        result.Should().BeOfType<OpenApiErrorResponse>();
        var error = (OpenApiErrorResponse)result!;
        error.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Run_RetryAfterDateHeader_ComputedAsOffset()
    {
        var futureDate = DateTimeOffset.UtcNow.AddSeconds(45);
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("rate limited")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(futureDate);
            return response;
        });
        var runner = new OpenApiOperationRunner(new HttpClient(handler));

        var result = await runner.RunAsync(GetOperation(), new Dictionary<string, object?>(), null, default);

        var error = (OpenApiErrorResponse)result!;
        error.RetryAfter.Should().BeGreaterThan(TimeSpan.Zero);
        error.RetryAfter.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(46));
    }

    [Fact]
    public async Task Run_ErrorDetectorReturnsNonNull_ReturnsDetectedError()
    {
        var detectedError = new OpenApiErrorResponse { StatusCode = 200, Body = "logical error" };
        OpenApiErrorResponse? Detector(HttpResponseMessage r, string? body) => detectedError;

        var op = GetOperation();
        var (runner, _) = MakeRunner(errorDetector: Detector);

        var result = await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        result.Should().BeSameAs(detectedError);
    }

    [Fact]
    public async Task Run_ErrorDetectorReturnsNull_TreatsAsSuccess()
    {
        OpenApiErrorResponse? Detector(HttpResponseMessage r, string? body) => null;

        var op = GetOperation();
        var (runner, _) = MakeRunner(responseBody: """{"ok":true}""", errorDetector: Detector);

        var result = await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        result.Should().BeOfType<OpenApiOperationResponse>();
        ((OpenApiOperationResponse)result!).Content.Should().BeOfType<JsonElement>();
    }

    [Fact]
    public async Task Run_AuthCallback_InvokedBeforeRequest()
    {
        var authCalled = false;
        Task Auth(HttpRequestMessage req, CancellationToken ct)
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "token");
            authCalled = true;
            return Task.CompletedTask;
        }

        var (runner, captured) = MakeRunner(authCallback: Auth);

        await runner.RunAsync(GetOperation(), new Dictionary<string, object?>(), null, default);

        authCalled.Should().BeTrue();
        captured[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured[0].Headers.Authorization!.Parameter.Should().Be("token");
    }

    [Fact]
    public async Task Run_UserAgent_PresentOnOutgoingRequest()
    {
        var (runner, captured) = MakeRunner(userAgent: "HPD-Agent/1.0");

        await runner.RunAsync(GetOperation(), new Dictionary<string, object?>(), null, default);

        captured[0].Headers.UserAgent.ToString().Should().Contain("HPD-Agent/1.0");
    }

    // ────────────────────────────────────────────────────────────
    // Array query parameter serialization
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_ArrayQueryParam_Explode_ProducesRepeatedKeys()
    {
        // explode: true → status=available&status=pending
        var param = new RestApiParameter
        {
            Name = "status",
            Type = "array",
            ArrayItemType = "string",
            IsRequired = false,
            Location = RestApiParameterLocation.Query,
            Expand = true
        };
        var op = GetOperation(parameters: [param]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["status"] = new[] { "available", "pending" } },
            null, default);

        var query = captured[0].RequestUri!.Query;
        query.Should().Contain("status=available");
        query.Should().Contain("status=pending");
    }

    [Fact]
    public async Task Run_ArrayQueryParam_NoExplode_ProducesCommaDelimited()
    {
        // explode: false → tags=cat,dog
        var param = new RestApiParameter
        {
            Name = "tags",
            Type = "array",
            ArrayItemType = "string",
            IsRequired = false,
            Location = RestApiParameterLocation.Query,
            Expand = false
        };
        var op = GetOperation(parameters: [param]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["tags"] = new[] { "cat", "dog" } },
            null, default);

        var query = Uri.UnescapeDataString(captured[0].RequestUri!.Query);
        query.Should().Contain("tags=cat,dog");
    }

    [Fact]
    public async Task Run_EmptyArrayQueryParam_NotAppendedToUrl()
    {
        var param = new RestApiParameter
        {
            Name = "status",
            Type = "array",
            ArrayItemType = "string",
            IsRequired = false,
            Location = RestApiParameterLocation.Query,
            Expand = true
        };
        var op = GetOperation(parameters: [param]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["status"] = Array.Empty<string>() },
            null, default);

        captured[0].RequestUri!.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_SingleElementArrayQueryParam_Explode_SingleKeyValue()
    {
        var param = new RestApiParameter
        {
            Name = "status",
            Type = "array",
            ArrayItemType = "string",
            IsRequired = false,
            Location = RestApiParameterLocation.Query,
            Expand = true
        };
        var op = GetOperation(parameters: [param]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["status"] = new[] { "available" } },
            null, default);

        captured[0].RequestUri!.Query.Should().Contain("status=available");
    }

    // ────────────────────────────────────────────────────────────
    // URL encoding edge cases
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_PathParameterWithSlash_PercentEncoded()
    {
        var op = GetOperation(
            path: "/pets/{petId}",
            parameters: [PathParam("petId")]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["petId"] = "foo/bar" }, null, default);

        captured[0].RequestUri!.PathAndQuery.Should().Contain("foo%2Fbar");
    }

    [Fact]
    public async Task Run_PathParameterWithColon_PercentEncoded()
    {
        var op = GetOperation(
            path: "/pets/{petId}",
            parameters: [PathParam("petId")]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["petId"] = "foo:bar" }, null, default);

        captured[0].RequestUri!.PathAndQuery.Should().Contain("foo%3Abar");
    }

    [Fact]
    public async Task Run_QueryValueWithAmpersand_PercentEncoded()
    {
        var op = GetOperation(parameters: [QueryParam("q")]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["q"] = "cats&dogs" }, null, default);

        // & must be encoded so it doesn't split the query string
        captured[0].RequestUri!.Query.Should().Contain("%26");
        captured[0].RequestUri!.Query.Should().NotContain("q=cats&dogs");
    }

    [Fact]
    public async Task Run_QueryValueWithEquals_PercentEncoded()
    {
        var op = GetOperation(parameters: [QueryParam("filter")]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["filter"] = "name=fluffy" }, null, default);

        captured[0].RequestUri!.Query.Should().Contain("%3D");
    }

    [Fact]
    public async Task Run_QueryValueWithSpace_PercentEncoded()
    {
        var op = GetOperation(parameters: [QueryParam("city")]);
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["city"] = "New York" }, null, default);

        captured[0].RequestUri!.Query.Should().Contain("New%20York");
    }

    // ────────────────────────────────────────────────────────────
    // Server URL trailing slash handling
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_ServerUrlWithTrailingSlash_NoDoubleSlashInPath()
    {
        var op = GetOperation(
            path: "/pets",
            serverUrl: "https://api.example.com/");
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        captured[0].RequestUri!.PathAndQuery.Should().Be("/pets");
    }

    [Fact]
    public async Task Run_ServerUrlWithoutTrailingSlash_PathJoinedCorrectly()
    {
        var op = GetOperation(
            path: "/pets",
            serverUrl: "https://api.example.com");
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        captured[0].RequestUri!.PathAndQuery.Should().Be("/pets");
    }

    // ────────────────────────────────────────────────────────────
    // Helper: fake HTTP handler
    // ────────────────────────────────────────────────────────────

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    private sealed class AsyncFakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }
}
