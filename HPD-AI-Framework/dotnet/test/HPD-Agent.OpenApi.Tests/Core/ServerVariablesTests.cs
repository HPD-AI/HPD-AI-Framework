using System.Net;
using HPD.OpenApi.Core;
using HPD.OpenApi.Core.Model;

namespace HPD.Agent.OpenApi.Tests.Core;

/// <summary>
/// Tests for OpenAPI server variable support:
/// parsing variables from the spec, substitution into URL templates,
/// ArgumentName lookup, enum validation, and default fallback.
/// </summary>
public class ServerVariablesTests
{
    private static readonly string ServerVarsSpecPath =
        Path.Combine(AppContext.BaseDirectory, "TestSpecs", "server-variables.json");

    private readonly OpenApiDocumentParser _parser = new();

    // ────────────────────────────────────────────────────────────
    // Parsing: RestApiServerVariable from spec
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_ServerVariables_ParsedOnAllOperations()
    {
        var spec = await _parser.ParseFromFileAsync(ServerVarsSpecPath, new OpenApiCoreConfig());

        spec.Operations.Should().AllSatisfy(op =>
            op.ServerVariables.Should().HaveCount(2));
    }

    [Fact]
    public async Task ParseFromFile_ServerVariable_DefaultValuePreserved()
    {
        var spec = await _parser.ParseFromFileAsync(ServerVarsSpecPath, new OpenApiCoreConfig());

        var op = spec.Operations.First();
        op.ServerVariables["region"].Default.Should().Be("us-east");
        op.ServerVariables["version"].Default.Should().Be("v2");
    }

    [Fact]
    public async Task ParseFromFile_ServerVariable_EnumValuesPreserved()
    {
        var spec = await _parser.ParseFromFileAsync(ServerVarsSpecPath, new OpenApiCoreConfig());

        var op = spec.Operations.First();
        op.ServerVariables["region"].Enum.Should().BeEquivalentTo(
            ["us-east", "eu-west", "ap-south"]);
        op.ServerVariables["version"].Enum.Should().BeEquivalentTo(["v1", "v2", "v3"]);
    }

    [Fact]
    public async Task ParseFromFile_ServerVariable_NoEnum_EnumIsNull()
    {
        // A variable with no enum defined should have null Enum
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "No Enum", "version": "1.0" },
              "servers": [{
                "url": "https://api.example.com/{version}",
                "variables": {
                  "version": { "default": "v1" }
                }
              }],
              "paths": {
                "/items": {
                  "get": {
                    "operationId": "listItems",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig());

        spec.Operations[0].ServerVariables["version"].Enum.Should().BeNull();
    }

    [Fact]
    public async Task ParseFromFile_NoServerVariables_EmptyDictionary()
    {
        // The petstore spec has no server variables — dictionary should be empty, not null
        var specPath = Path.Combine(AppContext.BaseDirectory, "TestSpecs", "petstore.json");
        var spec = await _parser.ParseFromFileAsync(specPath, new OpenApiCoreConfig());

        spec.Operations.Should().AllSatisfy(op =>
            op.ServerVariables.Should().BeEmpty());
    }

    // ────────────────────────────────────────────────────────────
    // IsValid helper on RestApiServerVariable
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void IsValid_NullEnum_AlwaysTrue()
    {
        var variable = new RestApiServerVariable { Default = "v1", Enum = null };

        variable.IsValid("anything").Should().BeTrue();
        variable.IsValid("v1").Should().BeTrue();
        variable.IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithEnum_TrueForMember()
    {
        var variable = new RestApiServerVariable
        {
            Default = "v1",
            Enum = new[] { "v1", "v2", "v3" }
        };

        variable.IsValid("v1").Should().BeTrue();
        variable.IsValid("v2").Should().BeTrue();
        variable.IsValid("v3").Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithEnum_FalseForNonMember()
    {
        var variable = new RestApiServerVariable
        {
            Default = "v1",
            Enum = new[] { "v1", "v2" }
        };

        variable.IsValid("v3").Should().BeFalse();
        variable.IsValid("V1").Should().BeFalse();  // case-sensitive
        variable.IsValid("").Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────
    // Runner: variable substitution
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_ServerVariable_DefaultUsed_WhenNoArgProvided()
    {
        // {version} has default "v2" — no argument supplied → uses default
        var op = MakeOp("https://api.example.com/{version}", new()
        {
            ["version"] = new RestApiServerVariable { Default = "v2" }
        });
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        captured[0].RequestUri!.Host.Should().Be("api.example.com");
        captured[0].RequestUri!.PathAndQuery.Should().StartWith("/v2/items");
    }

    [Fact]
    public async Task Run_ServerVariable_ValidArgOverridesDefault()
    {
        // {version} enum: v1, v2, v3 — arg "v3" is valid → used
        var op = MakeOp("https://api.example.com/{version}", new()
        {
            ["version"] = new RestApiServerVariable
            {
                Default = "v2",
                Enum = new[] { "v1", "v2", "v3" }
            }
        });
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["version"] = "v3" },
            null, default);

        captured[0].RequestUri!.PathAndQuery.Should().StartWith("/v3/items");
    }

    [Fact]
    public async Task Run_ServerVariable_InvalidEnumValue_FallsBackToDefault()
    {
        // {version} enum: v1, v2 — arg "v99" is invalid → falls back to default "v1"
        var op = MakeOp("https://api.example.com/{version}", new()
        {
            ["version"] = new RestApiServerVariable
            {
                Default = "v1",
                Enum = new[] { "v1", "v2" }
            }
        });
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["version"] = "v99" },
            null, default);

        captured[0].RequestUri!.PathAndQuery.Should().StartWith("/v1/items");
    }

    [Fact]
    public async Task Run_ServerVariable_ArgumentName_UsedForLookup()
    {
        // ArgumentName = "api_version" — argument supplied under that name → used
        var op = MakeOp("https://api.example.com/{version}", new()
        {
            ["version"] = new RestApiServerVariable
            {
                Default = "v1",
                ArgumentName = "api_version"
            }
        });
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["api_version"] = "v3" },
            null, default);

        captured[0].RequestUri!.PathAndQuery.Should().StartWith("/v3/items");
    }

    [Fact]
    public async Task Run_ServerVariable_ArgumentNameNotFound_FallsBackToVariableName()
    {
        // ArgumentName = "alt_version" but argument is keyed by "version" → still works
        var op = MakeOp("https://api.example.com/{version}", new()
        {
            ["version"] = new RestApiServerVariable
            {
                Default = "v1",
                ArgumentName = "alt_version"
            }
        });
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["version"] = "v2" },
            null, default);

        captured[0].RequestUri!.PathAndQuery.Should().StartWith("/v2/items");
    }

    [Fact]
    public async Task Run_ServerVariable_ArgumentName_InvalidEnum_FallsBackToDefault()
    {
        // ArgumentName = "alt_version", enum ["v1","v2"], arg is invalid → default "v1"
        var op = MakeOp("https://api.example.com/{version}", new()
        {
            ["version"] = new RestApiServerVariable
            {
                Default = "v1",
                Enum = new[] { "v1", "v2" },
                ArgumentName = "alt_version"
            }
        });
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["alt_version"] = "v99" },
            null, default);

        captured[0].RequestUri!.PathAndQuery.Should().StartWith("/v1/items");
    }

    [Fact]
    public async Task Run_MultipleServerVariables_AllSubstituted()
    {
        // Both {region} and {version} in template → both replaced
        var op = MakeOp("https://{region}.api.example.com/{version}", new()
        {
            ["region"] = new RestApiServerVariable
            {
                Default = "us-east",
                Enum = new[] { "us-east", "eu-west" }
            },
            ["version"] = new RestApiServerVariable
            {
                Default = "v2",
                Enum = new[] { "v1", "v2", "v3" }
            }
        });
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["region"] = "eu-west", ["version"] = "v3" },
            null, default);

        captured[0].RequestUri!.Host.Should().Be("eu-west.api.example.com");
        captured[0].RequestUri!.PathAndQuery.Should().StartWith("/v3/items");
    }

    [Fact]
    public async Task Run_MultipleServerVariables_MixOfArgAndDefault()
    {
        // {region} provided, {version} not → region from arg, version from default
        var op = MakeOp("https://{region}.api.example.com/{version}", new()
        {
            ["region"] = new RestApiServerVariable
            {
                Default = "us-east",
                Enum = new[] { "us-east", "eu-west" }
            },
            ["version"] = new RestApiServerVariable { Default = "v2" }
        });
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?> { ["region"] = "eu-west" },
            null, default);

        captured[0].RequestUri!.Host.Should().Be("eu-west.api.example.com");
        captured[0].RequestUri!.PathAndQuery.Should().StartWith("/v2/items");
    }

    [Fact]
    public async Task Run_ServerVariable_NoDefaultAndNoArg_ThrowsInvalidOperationException()
    {
        var op = MakeOp("https://api.example.com/{version}", new()
        {
            ["version"] = new RestApiServerVariable { Default = "" }
        });
        var (runner, _) = MakeRunner();

        // No arg, empty default → must throw
        var act = () => runner.RunAsync(op, new Dictionary<string, object?>(), null, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*version*");
    }

    [Fact]
    public async Task Run_ServerUrlOverride_BypassesVariableSubstitution()
    {
        // When ServerUrlOverride is set, variables are irrelevant — override wins
        var op = MakeOp("https://{region}.api.example.com/{version}", new()
        {
            ["region"] = new RestApiServerVariable { Default = "us-east" },
            ["version"] = new RestApiServerVariable { Default = "v2" }
        });
        var (runner, captured) = MakeRunner();

        await runner.RunAsync(op,
            new Dictionary<string, object?>(),
            new Uri("https://override.example.com"),
            default);

        captured[0].RequestUri!.Host.Should().Be("override.example.com");
    }

    [Fact]
    public async Task ParseFromFile_ServerVariables_TemplateUrlPreservedBeforeSubstitution()
    {
        // The ServerUrl stored on the operation is the raw template, not yet substituted
        var spec = await _parser.ParseFromFileAsync(ServerVarsSpecPath, new OpenApiCoreConfig());

        spec.Operations.Should().AllSatisfy(op =>
            op.ServerUrl.Should().Be("https://{region}.api.example.com/{version}"));
    }

    // ────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────

    private static RestApiOperation MakeOp(
        string serverUrl,
        Dictionary<string, RestApiServerVariable> variables) => new()
        {
            Id = "listItems",
            Path = "/items",
            Method = HttpMethod.Get,
            ServerUrl = serverUrl,
            ServerVariables = variables
        };

    private static (OpenApiOperationRunner runner, List<HttpRequestMessage> captured) MakeRunner(
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var captured = new List<HttpRequestMessage>();
        var handler = new FakeHttpMessageHandler(req =>
        {
            captured.Add(req);
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        });
        var runner = new OpenApiOperationRunner(new HttpClient(handler));
        return (runner, captured);
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
