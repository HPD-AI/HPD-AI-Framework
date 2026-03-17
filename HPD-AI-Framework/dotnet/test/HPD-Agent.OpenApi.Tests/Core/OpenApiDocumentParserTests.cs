using HPD.OpenApi.Core;
using HPD.OpenApi.Core.Model;

namespace HPD.Agent.OpenApi.Tests.Core;

public class OpenApiDocumentParserTests
{
    private static readonly string PetstoreSpecPath =
        Path.Combine(AppContext.BaseDirectory, "TestSpecs", "petstore.json");

    private static readonly string WeatherSpecPath =
        Path.Combine(AppContext.BaseDirectory, "TestSpecs", "weather.json");

    private static readonly string RelativeServerSpecPath =
        Path.Combine(AppContext.BaseDirectory, "TestSpecs", "petstore-relative-server.json");

    private static readonly string MultiServerSpecPath =
        Path.Combine(AppContext.BaseDirectory, "TestSpecs", "multi-server.json");

    private static readonly string ArrayParamsSpecPath =
        Path.Combine(AppContext.BaseDirectory, "TestSpecs", "array-params.json");

    private readonly OpenApiDocumentParser _parser = new();

    // ────────────────────────────────────────────────────────────
    // Basic parsing
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_PetstoreSpec_ParsesAllOperations()
    {
        var spec = await _parser.ParseFromFileAsync(PetstoreSpecPath, new OpenApiCoreConfig());

        spec.Operations.Should().HaveCount(5);
        spec.Operations.Select(o => o.Id).Should().Contain(
            ["listPets", "createPet", "getPetById", "deletePet", "updatePetStatus"]);
    }

    [Fact]
    public async Task ParseFromFile_PetstoreSpec_ExtractsInfo()
    {
        var spec = await _parser.ParseFromFileAsync(PetstoreSpecPath, new OpenApiCoreConfig());

        spec.Info.Title.Should().Be("Petstore");
        spec.Info.Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task ParseFromFile_WeatherSpec_ParsesAllOperations()
    {
        var spec = await _parser.ParseFromFileAsync(WeatherSpecPath, new OpenApiCoreConfig());

        spec.Operations.Should().HaveCount(2);
        spec.Operations.Select(o => o.Id).Should().Contain(["getForecast", "getAlerts"]);
    }

    // ────────────────────────────────────────────────────────────
    // Parameter extraction
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_PathParameter_HasCorrectLocationAndRequiredFlag()
    {
        var spec = await _parser.ParseFromFileAsync(PetstoreSpecPath, new OpenApiCoreConfig());

        var getPet = spec.Operations.First(o => o.Id == "getPetById");
        var petId = getPet.Parameters.Single(p => p.Name == "petId");

        petId.Location.Should().Be(RestApiParameterLocation.Path);
        petId.IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task ParseFromFile_QueryParameter_HasCorrectLocation()
    {
        var spec = await _parser.ParseFromFileAsync(PetstoreSpecPath, new OpenApiCoreConfig());

        var listPets = spec.Operations.First(o => o.Id == "listPets");
        var limit = listPets.Parameters.Single(p => p.Name == "limit");

        limit.Location.Should().Be(RestApiParameterLocation.Query);
        limit.IsRequired.Should().BeFalse();
    }

    [Fact]
    public async Task ParseFromFile_HeaderParameter_HasCorrectLocation()
    {
        var spec = await _parser.ParseFromFileAsync(WeatherSpecPath, new OpenApiCoreConfig());

        var forecast = spec.Operations.First(o => o.Id == "getForecast");
        var apiKey = forecast.Parameters.Single(p => p.Name == "X-Api-Key");

        apiKey.Location.Should().Be(RestApiParameterLocation.Header);
    }

    // ────────────────────────────────────────────────────────────
    // Request body / payload
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_RequestBody_FlattensPropertiesCorrectly()
    {
        var spec = await _parser.ParseFromFileAsync(PetstoreSpecPath, new OpenApiCoreConfig());

        var createPet = spec.Operations.First(o => o.Id == "createPet");
        createPet.Payload.Should().NotBeNull();
        createPet.Payload!.MediaType.Should().Be("application/json");
        createPet.Payload.Properties.Should().HaveCount(2);
        createPet.Payload.Properties.Select(p => p.Name).Should()
            .BeEquivalentTo(["name", "tag"]);
    }

    [Fact]
    public async Task ParseFromFile_RequiredPayloadProperty_HasIsRequiredTrue()
    {
        var spec = await _parser.ParseFromFileAsync(PetstoreSpecPath, new OpenApiCoreConfig());

        var createPet = spec.Operations.First(o => o.Id == "createPet");
        var nameProp = createPet.Payload!.Properties.Single(p => p.Name == "name");
        var tagProp = createPet.Payload.Properties.Single(p => p.Name == "tag");

        nameProp.IsRequired.Should().BeTrue();
        tagProp.IsRequired.Should().BeFalse();
    }

    [Fact]
    public async Task ParseFromFile_GetOperation_HasNullPayload()
    {
        var spec = await _parser.ParseFromFileAsync(PetstoreSpecPath, new OpenApiCoreConfig());

        var listPets = spec.Operations.First(o => o.Id == "listPets");

        listPets.Payload.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // Server URL
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_GlobalServerUrl_SetOnAllOperations()
    {
        var spec = await _parser.ParseFromFileAsync(PetstoreSpecPath, new OpenApiCoreConfig());

        spec.Operations.Should().AllSatisfy(op =>
            op.ServerUrl.Should().Be("https://petstore.example.com/v1"));
    }

    // ────────────────────────────────────────────────────────────
    // Operation filtering
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_OperationsToExclude_ExcludesNamedOperations()
    {
        var config = new OpenApiCoreConfig
        {
            OperationsToExclude = ["deletePet", "createPet"]
        };

        var spec = await _parser.ParseFromFileAsync(PetstoreSpecPath, config);

        spec.Operations.Select(o => o.Id).Should().NotContain(["deletePet", "createPet"]);
        spec.Operations.Should().HaveCount(3);
    }

    [Fact]
    public async Task ParseFromFile_OperationSelectionPredicate_FiltersOperations()
    {
        var config = new OpenApiCoreConfig
        {
            OperationSelectionPredicate = ctx =>
                ctx.Method!.Equals("GET", StringComparison.OrdinalIgnoreCase)
        };

        var spec = await _parser.ParseFromFileAsync(PetstoreSpecPath, config);

        spec.Operations.Should().AllSatisfy(op =>
            op.Method.Should().Be(HttpMethod.Get));
        spec.Operations.Should().HaveCount(2); // listPets, getPetById
    }

    // ────────────────────────────────────────────────────────────
    // Error handling
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_InvalidJson_ThrowsException()
    {
        using var stream = new MemoryStream("not json at all"u8.ToArray());
        var config = new OpenApiCoreConfig();

        Exception? thrown = null;
        try { await _parser.ParseAsync(stream, config); }
        catch (Exception ex) { thrown = ex; }

        // The parser throws OpenApiParseException for null-deserialized docs,
        // but raw JSON parse failures surface as JsonException from System.Text.Json.
        thrown.Should().NotBeNull();
        (thrown is System.Text.Json.JsonException or OpenApiParseException).Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_SpecWith3_1Version_DowngradedAndParsesSuccessfully()
    {
        // A minimal 3.1 spec — should be auto-downgraded to 3.0.1
        var specJson = """
            {
              "openapi": "3.1.0",
              "info": { "title": "Test", "version": "1.0" },
              "paths": {
                "/test": {
                  "get": {
                    "operationId": "getTest",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));
        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig());

        spec.Operations.Should().HaveCount(1);
        spec.Operations[0].Id.Should().Be("getTest");
    }

    [Fact]
    public async Task ParseAsync_SpecWithErrors_IgnoreNonCompliantErrorsFalse_Throws()
    {
        // Missing required "responses" on operation — violates OpenAPI spec
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Bad", "version": "1.0" },
              "paths": {
                "/bad": {
                  "get": {
                    "operationId": "badOp"
                  }
                }
              }
            }
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));
        var config = new OpenApiCoreConfig { IgnoreNonCompliantErrors = false };

        var act = () => _parser.ParseAsync(stream, config);

        await act.Should().ThrowAsync<OpenApiParseException>();
    }

    [Fact]
    public async Task ParseAsync_SpecWithErrors_IgnoreNonCompliantErrorsTrue_ReturnsPartial()
    {
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Bad", "version": "1.0" },
              "paths": {
                "/bad": {
                  "get": {
                    "operationId": "badOp"
                  }
                }
              }
            }
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));
        var config = new OpenApiCoreConfig { IgnoreNonCompliantErrors = true };

        // Should not throw
        var spec = await _parser.ParseAsync(stream, config);

        spec.Should().NotBeNull();
    }

    [Fact]
    public async Task ParseAsync_ParameterWithNoInLocation_ThrowsOpenApiParseException()
    {
        // Parameters with no "in" field are illegal in OpenAPI
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Bad Params", "version": "1.0" },
              "paths": {
                "/test": {
                  "get": {
                    "operationId": "getTest",
                    "parameters": [
                      { "name": "broken" }
                    ],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));
        var config = new OpenApiCoreConfig { IgnoreNonCompliantErrors = true };

        var act = () => _parser.ParseAsync(stream, config);

        await act.Should().ThrowAsync<OpenApiParseException>()
            .WithMessage("*location*undefined*");
    }

    [Fact]
    public async Task ParseFromFile_NoServersField_OperationServerUrlIsNull()
    {
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "No Servers", "version": "1.0" },
              "paths": {
                "/ping": {
                  "get": {
                    "operationId": "ping",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));
        var config = new OpenApiCoreConfig { IgnoreNonCompliantErrors = true };

        var spec = await _parser.ParseAsync(stream, config);

        spec.Operations[0].ServerUrl.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // Relative URL resolution (regression: was returning file://)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_RelativeServerUrl_ResolvedAgainstHttpBaseUri()
    {
        // Simulates fetching a spec from https://petstore3.swagger.io/api/v3/openapi.json
        // where the spec contains "servers": [{"url": "/api/v3"}]
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Relative", "version": "1.0" },
              "servers": [{ "url": "/api/v3" }],
              "paths": {
                "/pet": {
                  "get": {
                    "operationId": "getPet",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));
        var baseUri = new Uri("https://petstore3.swagger.io/api/v3/openapi.json");

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig(), baseUri);

        spec.Operations[0].ServerUrl.Should().Be("https://petstore3.swagger.io/api/v3");
    }

    [Fact]
    public async Task ParseAsync_RelativeServerUrl_DoesNotProduceFileScheme()
    {
        // Regression: without baseUri the runner was constructing file:///api/v3 URLs
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Relative", "version": "1.0" },
              "servers": [{ "url": "/api/v3" }],
              "paths": {
                "/pet": {
                  "get": {
                    "operationId": "getPet",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));
        var baseUri = new Uri("https://example.com/specs/openapi.json");

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig(), baseUri);

        spec.Operations[0].ServerUrl.Should().NotStartWith("file://");
        spec.Operations[0].ServerUrl.Should().StartWith("https://");
    }

    [Fact]
    public async Task ParseFromFile_RelativeServerSpec_ResolvedAgainstFileBaseUri()
    {
        // When spec is loaded from disk, relative server URL resolves against the file's URI.
        // The result should be an absolute file:// URL (not the https case, but not broken).
        var spec = await _parser.ParseFromFileAsync(RelativeServerSpecPath, new OpenApiCoreConfig());

        // Must be absolute — either file:// (from disk) or https:// (never broken)
        spec.Operations.Should().AllSatisfy(op =>
        {
            op.ServerUrl.Should().NotBeNull();
            Uri.TryCreate(op.ServerUrl, UriKind.Absolute, out _).Should().BeTrue(
                $"ServerUrl '{op.ServerUrl}' should be an absolute URI");
        });
    }

    [Fact]
    public async Task ParseAsync_AbsoluteHttpsServerUrl_PassesThroughUnchanged()
    {
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Absolute", "version": "1.0" },
              "servers": [{ "url": "https://api.stripe.com" }],
              "paths": {
                "/v1/charges": {
                  "get": {
                    "operationId": "listCharges",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));
        var baseUri = new Uri("https://files.stripe.com/openapi.json");

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig(), baseUri);

        // Absolute URL must not be modified by baseUri resolution
        spec.Operations[0].ServerUrl.Should().Be("https://api.stripe.com");
    }

    [Fact]
    public async Task ParseAsync_AbsoluteHttpServerUrl_PassesThroughUnchanged()
    {
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "HTTP", "version": "1.0" },
              "servers": [{ "url": "http://localhost:8080" }],
              "paths": {
                "/health": {
                  "get": {
                    "operationId": "health",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));
        var baseUri = new Uri("https://registry.example.com/spec.json");

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig(), baseUri);

        spec.Operations[0].ServerUrl.Should().Be("http://localhost:8080");
    }

    [Fact]
    public async Task ParseAsync_RelativeServerUrl_NullBaseUri_ReturnedAsIs()
    {
        // Without a baseUri, relative URLs can't be resolved — returned as-is, no crash
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Relative No Base", "version": "1.0" },
              "servers": [{ "url": "/api/v1" }],
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

        // No baseUri passed — should not throw
        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig());

        spec.Operations[0].ServerUrl.Should().Be("/api/v1");
    }

    [Fact]
    public async Task ParseFromUri_PetstoreSpec_ServerUrlIsAbsoluteHttps()
    {
        // Integration: ParseFromUriAsync passes the spec URI as baseUri.
        // petstore-relative-server.json has servers: [{url: "/api/v3"}]
        // When loaded from a URI, the resolved URL must be absolute https.
        // We use a local file spec served via ParseFromFileAsync as a proxy —
        // but to test the URI path we use ParseAsync directly with an http baseUri.
        var specBytes = await File.ReadAllBytesAsync(RelativeServerSpecPath);
        using var stream = new MemoryStream(specBytes);
        var specBaseUri = new Uri("https://petstore3.swagger.io/api/v3/openapi.json");

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig(), specBaseUri);

        spec.Operations.Should().AllSatisfy(op =>
            op.ServerUrl.Should().Be("https://petstore3.swagger.io/api/v3"));
    }

    // ────────────────────────────────────────────────────────────
    // Server URL hierarchy (global → path → operation)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_MultipleGlobalServers_FirstServerUsed()
    {
        var spec = await _parser.ParseFromFileAsync(MultiServerSpecPath, new OpenApiCoreConfig());

        var listItems = spec.Operations.First(o => o.Id == "listItems");
        listItems.ServerUrl.Should().Be("https://global.example.com");
    }

    [Fact]
    public async Task ParseFromFile_PathLevelServer_OverridesGlobalForAffectedOperations()
    {
        var spec = await _parser.ParseFromFileAsync(MultiServerSpecPath, new OpenApiCoreConfig());

        var getSpecial = spec.Operations.First(o => o.Id == "getSpecial");
        getSpecial.ServerUrl.Should().Be("https://path-level.example.com");
    }

    [Fact]
    public async Task ParseFromFile_OperationLevelServer_OverridesPathLevel()
    {
        var spec = await _parser.ParseFromFileAsync(MultiServerSpecPath, new OpenApiCoreConfig());

        var createSpecial = spec.Operations.First(o => o.Id == "createSpecialOverride");
        createSpecial.ServerUrl.Should().Be("https://operation-level.example.com");
    }

    [Fact]
    public async Task ParseFromFile_GlobalServerUnaffectedOperations_StillGetGlobalServer()
    {
        var spec = await _parser.ParseFromFileAsync(MultiServerSpecPath, new OpenApiCoreConfig());

        // listItems is on /items, which has no path-level server — gets global
        var listItems = spec.Operations.First(o => o.Id == "listItems");
        listItems.ServerUrl.Should().Be("https://global.example.com");
    }

    // ────────────────────────────────────────────────────────────
    // Parameter type fidelity
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_IntegerParameter_TypeIsInteger()
    {
        var spec = await _parser.ParseFromFileAsync(ArrayParamsSpecPath, new OpenApiCoreConfig());

        var typed = spec.Operations.First(o => o.Id == "getTyped");
        var count = typed.Parameters.Single(p => p.Name == "count");

        count.Type.Should().Be("integer");
    }

    [Fact]
    public async Task ParseFromFile_NumberParameter_TypeIsNumber()
    {
        var spec = await _parser.ParseFromFileAsync(ArrayParamsSpecPath, new OpenApiCoreConfig());

        var typed = spec.Operations.First(o => o.Id == "getTyped");
        var ratio = typed.Parameters.Single(p => p.Name == "ratio");

        ratio.Type.Should().Be("number");
    }

    [Fact]
    public async Task ParseFromFile_BooleanParameter_TypeIsBoolean()
    {
        var spec = await _parser.ParseFromFileAsync(ArrayParamsSpecPath, new OpenApiCoreConfig());

        var typed = spec.Operations.First(o => o.Id == "getTyped");
        var active = typed.Parameters.Single(p => p.Name == "active");

        active.Type.Should().Be("boolean");
    }

    [Fact]
    public async Task ParseFromFile_ArrayParameter_TypeIsArrayWithItemType()
    {
        var spec = await _parser.ParseFromFileAsync(ArrayParamsSpecPath, new OpenApiCoreConfig());

        var listItems = spec.Operations.First(o => o.Id == "listItems");
        var status = listItems.Parameters.Single(p => p.Name == "status");

        status.Type.Should().Be("array");
        status.ArrayItemType.Should().Be("string");
    }

    [Fact]
    public async Task ParseFromFile_ArrayParameter_ExplodeFlagPreserved()
    {
        var spec = await _parser.ParseFromFileAsync(ArrayParamsSpecPath, new OpenApiCoreConfig());

        var listItems = spec.Operations.First(o => o.Id == "listItems");
        var statusExploded = listItems.Parameters.Single(p => p.Name == "status");
        var tagsNotExploded = listItems.Parameters.Single(p => p.Name == "tags");

        statusExploded.Expand.Should().BeTrue();
        tagsNotExploded.Expand.Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────
    // Edge cases
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_EmptyPaths_ReturnsEmptyOperations()
    {
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Empty", "version": "1.0" },
              "paths": {}
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig { IgnoreNonCompliantErrors = true });

        spec.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_OperationWithNoOperationId_NameDerivedFromMethodAndPath()
    {
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "No ID", "version": "1.0" },
              "servers": [{ "url": "https://api.example.com" }],
              "paths": {
                "/widgets/{id}": {
                  "get": {
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig { IgnoreNonCompliantErrors = true });

        spec.Operations.Should().HaveCount(1);
        spec.Operations[0].Id.Should().BeNull();
        spec.Operations[0].Path.Should().Be("/widgets/{id}");
        spec.Operations[0].Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task ParseAsync_PathItemParameters_InheritedByOperations()
    {
        // Parameters defined at path item level are shared by all operations on that path
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Path Params", "version": "1.0" },
              "servers": [{ "url": "https://api.example.com" }],
              "paths": {
                "/pets/{petId}": {
                  "parameters": [
                    { "name": "petId", "in": "path", "required": true, "schema": { "type": "string" } }
                  ],
                  "get": {
                    "operationId": "getPet",
                    "responses": { "200": { "description": "OK" } }
                  },
                  "delete": {
                    "operationId": "deletePet",
                    "responses": { "204": { "description": "No Content" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig { IgnoreNonCompliantErrors = true });

        var getPet = spec.Operations.First(o => o.Id == "getPet");
        var deletePet = spec.Operations.First(o => o.Id == "deletePet");

        getPet.Parameters.Should().ContainSingle(p => p.Name == "petId" && p.Location == RestApiParameterLocation.Path);
        deletePet.Parameters.Should().ContainSingle(p => p.Name == "petId" && p.Location == RestApiParameterLocation.Path);
    }

    [Fact]
    public async Task ParseAsync_OperationParameterOverridesPathItemParameter()
    {
        // If operation defines a param with the same name+location as path item, operation wins
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Override", "version": "1.0" },
              "servers": [{ "url": "https://api.example.com" }],
              "paths": {
                "/pets/{petId}": {
                  "parameters": [
                    { "name": "petId", "in": "path", "required": true, "description": "path-level", "schema": { "type": "string" } }
                  ],
                  "get": {
                    "operationId": "getPet",
                    "parameters": [
                      { "name": "petId", "in": "path", "required": true, "description": "operation-level", "schema": { "type": "string" } }
                    ],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig { IgnoreNonCompliantErrors = true });

        var getPet = spec.Operations.First(o => o.Id == "getPet");
        // The ParameterComparer deduplicates by (Name, In) — should have exactly one petId
        getPet.Parameters.Where(p => p.Name == "petId").Should().HaveCount(1);
        getPet.Parameters.Single(p => p.Name == "petId").Description.Should().Be("operation-level");
    }
}
