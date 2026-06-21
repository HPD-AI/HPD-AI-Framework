using HPD.OpenApi.Core;
using HPD.OpenApi.Core.Model;

namespace HPD.Agent.OpenApi.Tests.Core;

/// <summary>
/// Tests for two schema resolution gaps:
///
/// 1. $ref parameters — operations that reference component parameters via $ref.
///    The library's proxy object has null Type/Format/Items; we must read those from
///    the serialized (inline-resolved) JSON schema instead.
///
/// 2. allOf / anyOf / oneOf request body composition — specs that define request bodies
///    using composition keywords rather than direct properties.
///    We must walk all sub-schemas to collect the full property set.
/// </summary>
public class SchemaResolutionTests
{
    private static readonly string RefParamsSpecPath =
        Path.Combine(AppContext.BaseDirectory, "TestSpecs", "ref-params.json");

    private static readonly string AllOfBodySpecPath =
        Path.Combine(AppContext.BaseDirectory, "TestSpecs", "allof-body.json");

    private readonly OpenApiDocumentParser _parser = new();

    // ────────────────────────────────────────────────────────────
    // $ref parameter resolution
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_RefParam_StringType_TypeIsString()
    {
        // petId is defined as $ref → #/components/parameters/PetId (type: string)
        var spec = await _parser.ParseFromFileAsync(RefParamsSpecPath, new OpenApiCoreConfig());

        var getPet = spec.Operations.First(o => o.Id == "getPet");
        var petId = getPet.Parameters.Single(p => p.Name == "petId");

        petId.Type.Should().Be("string");
    }

    [Fact]
    public async Task ParseFromFile_RefParam_StringFormat_FormatIsUuid()
    {
        // petId $ref has format: uuid
        var spec = await _parser.ParseFromFileAsync(RefParamsSpecPath, new OpenApiCoreConfig());

        var getPet = spec.Operations.First(o => o.Id == "getPet");
        var petId = getPet.Parameters.Single(p => p.Name == "petId");

        petId.Format.Should().Be("uuid");
    }

    [Fact]
    public async Task ParseFromFile_RefParam_IntegerType_TypeIsInteger()
    {
        // limit $ref has type: integer
        var spec = await _parser.ParseFromFileAsync(RefParamsSpecPath, new OpenApiCoreConfig());

        var getPet = spec.Operations.First(o => o.Id == "getPet");
        var limit = getPet.Parameters.Single(p => p.Name == "limit");

        limit.Type.Should().Be("integer");
    }

    [Fact]
    public async Task ParseFromFile_RefParam_IntegerFormat_FormatIsInt32()
    {
        // limit $ref has format: int32
        var spec = await _parser.ParseFromFileAsync(RefParamsSpecPath, new OpenApiCoreConfig());

        var getPet = spec.Operations.First(o => o.Id == "getPet");
        var limit = getPet.Parameters.Single(p => p.Name == "limit");

        limit.Format.Should().Be("int32");
    }

    [Fact]
    public async Task ParseFromFile_RefParam_ArrayType_TypeIsArray()
    {
        // tags $ref has type: array
        var spec = await _parser.ParseFromFileAsync(RefParamsSpecPath, new OpenApiCoreConfig());

        var listPets = spec.Operations.First(o => o.Id == "listPets");
        var tags = listPets.Parameters.Single(p => p.Name == "tags");

        tags.Type.Should().Be("array");
    }

    [Fact]
    public async Task ParseFromFile_RefParam_ArrayItemType_ItemTypeIsString()
    {
        // tags $ref has items.type: string
        var spec = await _parser.ParseFromFileAsync(RefParamsSpecPath, new OpenApiCoreConfig());

        var listPets = spec.Operations.First(o => o.Id == "listPets");
        var tags = listPets.Parameters.Single(p => p.Name == "tags");

        tags.ArrayItemType.Should().Be("string");
    }

    [Fact]
    public async Task ParseFromFile_RefParam_RequiredFlag_PreservedFromRef()
    {
        // petId $ref has required: true, limit has required: false
        var spec = await _parser.ParseFromFileAsync(RefParamsSpecPath, new OpenApiCoreConfig());

        var getPet = spec.Operations.First(o => o.Id == "getPet");
        var petId = getPet.Parameters.Single(p => p.Name == "petId");
        var limit = getPet.Parameters.Single(p => p.Name == "limit");

        petId.IsRequired.Should().BeTrue();
        limit.IsRequired.Should().BeFalse();
    }

    [Fact]
    public async Task ParseFromFile_RefParam_SchemaJson_IsNotNull()
    {
        // The serialized schema JSON must be present even for $ref parameters
        var spec = await _parser.ParseFromFileAsync(RefParamsSpecPath, new OpenApiCoreConfig());

        var getPet = spec.Operations.First(o => o.Id == "getPet");
        getPet.Parameters.Should().AllSatisfy(p =>
            p.Schema.Should().NotBeNull($"parameter '{p.Name}' is a $ref but Schema must still be populated"));
    }

    // ────────────────────────────────────────────────────────────
    // Inline $ref resolution (not from components — inline in spec)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_InlineParam_StringType_TypeIsString()
    {
        // Inline (non-$ref) parameters should still work correctly after refactor
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "Inline", "version": "1.0" },
              "servers": [{ "url": "https://api.example.com" }],
              "paths": {
                "/items/{id}": {
                  "get": {
                    "operationId": "getItem",
                    "parameters": [
                      { "name": "id", "in": "path", "required": true, "schema": { "type": "integer", "format": "int64" } }
                    ],
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig());

        var id = spec.Operations[0].Parameters.Single(p => p.Name == "id");
        id.Type.Should().Be("integer");
        id.Format.Should().Be("int64");
    }

    // ────────────────────────────────────────────────────────────
    // allOf request body composition
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_AllOfBody_PropertiesFromAllSubSchemas()
    {
        // createPet uses allOf: [PetBase (name, status), inline (age, breed)]
        // All 4 properties must be present
        var spec = await _parser.ParseFromFileAsync(AllOfBodySpecPath, new OpenApiCoreConfig());

        var createPet = spec.Operations.First(o => o.Id == "createPet");
        var propNames = createPet.Payload!.Properties.Select(p => p.Name).ToHashSet();

        propNames.Should().Contain("name");
        propNames.Should().Contain("status");
        propNames.Should().Contain("age");
        propNames.Should().Contain("breed");
    }

    [Fact]
    public async Task ParseFromFile_AllOfBody_RequiredMergedAcrossSubSchemas()
    {
        // PetBase requires "name"; inline sub-schema requires "age"
        // Both must be required in the merged result
        var spec = await _parser.ParseFromFileAsync(AllOfBodySpecPath, new OpenApiCoreConfig());

        var createPet = spec.Operations.First(o => o.Id == "createPet");
        var props = createPet.Payload!.Properties.ToDictionary(p => p.Name);

        props["name"].IsRequired.Should().BeTrue();
        props["age"].IsRequired.Should().BeTrue();
        props["status"].IsRequired.Should().BeFalse();
        props["breed"].IsRequired.Should().BeFalse();
    }

    [Fact]
    public async Task ParseFromFile_AllOfBody_PropertyTypesPreserved()
    {
        // age is integer, name is string
        var spec = await _parser.ParseFromFileAsync(AllOfBodySpecPath, new OpenApiCoreConfig());

        var createPet = spec.Operations.First(o => o.Id == "createPet");
        var props = createPet.Payload!.Properties.ToDictionary(p => p.Name);

        props["name"].Type.Should().Be("string");
        props["age"].Type.Should().Be("integer");
    }

    [Fact]
    public async Task ParseFromFile_AllOfBody_PropertyDescriptionsPreserved()
    {
        // descriptions must flow through from sub-schemas
        var spec = await _parser.ParseFromFileAsync(AllOfBodySpecPath, new OpenApiCoreConfig());

        var createPet = spec.Operations.First(o => o.Id == "createPet");
        var props = createPet.Payload!.Properties.ToDictionary(p => p.Name);

        props["name"].Description.Should().Be("Pet name");
        props["age"].Description.Should().Be("Age in years");
    }

    // ────────────────────────────────────────────────────────────
    // anyOf request body composition
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_AnyOfBody_PropertiesFromAllSubSchemas()
    {
        // createOrder uses anyOf: [inline (productId, quantity), inline (sku)]
        var spec = await _parser.ParseFromFileAsync(AllOfBodySpecPath, new OpenApiCoreConfig());

        var createOrder = spec.Operations.First(o => o.Id == "createOrder");
        var propNames = createOrder.Payload!.Properties.Select(p => p.Name).ToHashSet();

        propNames.Should().Contain("productId");
        propNames.Should().Contain("quantity");
        propNames.Should().Contain("sku");
    }

    [Fact]
    public async Task ParseFromFile_AnyOfBody_NoDuplicateProperties()
    {
        // Even if a property appears in multiple sub-schemas, it should only appear once
        var spec = await _parser.ParseFromFileAsync(AllOfBodySpecPath, new OpenApiCoreConfig());

        var createOrder = spec.Operations.First(o => o.Id == "createOrder");

        createOrder.Payload!.Properties
            .GroupBy(p => p.Name)
            .Should().AllSatisfy(g => g.Count().Should().Be(1, $"'{g.Key}' must appear exactly once"));
    }

    // ────────────────────────────────────────────────────────────
    // Direct properties baseline (regression guard)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseFromFile_DirectProperties_StillWork()
    {
        // directProps operation uses plain properties — ensure allOf refactor didn't break it
        var spec = await _parser.ParseFromFileAsync(AllOfBodySpecPath, new OpenApiCoreConfig());

        var direct = spec.Operations.First(o => o.Id == "directProps");
        var propNames = direct.Payload!.Properties.Select(p => p.Name).ToHashSet();

        propNames.Should().Contain("title");
        propNames.Should().Contain("count");
        propNames.Should().HaveCount(2);
    }

    [Fact]
    public async Task ParseFromFile_DirectProperties_RequiredPreserved()
    {
        var spec = await _parser.ParseFromFileAsync(AllOfBodySpecPath, new OpenApiCoreConfig());

        var direct = spec.Operations.First(o => o.Id == "directProps");
        var props = direct.Payload!.Properties.ToDictionary(p => p.Name);

        props["title"].IsRequired.Should().BeTrue();
        props["count"].IsRequired.Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────
    // allOf via inline spec
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_AllOfBody_InlineSpec_PropertiesMerged()
    {
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "AllOf Inline", "version": "1.0" },
              "servers": [{ "url": "https://api.example.com" }],
              "paths": {
                "/things": {
                  "post": {
                    "operationId": "createThing",
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": {
                            "allOf": [
                              {
                                "type": "object",
                                "properties": {
                                  "foo": { "type": "string" }
                                }
                              },
                              {
                                "type": "object",
                                "properties": {
                                  "bar": { "type": "integer" }
                                }
                              }
                            ]
                          }
                        }
                      }
                    },
                    "responses": { "201": { "description": "Created" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig());

        var props = spec.Operations[0].Payload!.Properties.ToDictionary(p => p.Name);
        props.Should().ContainKey("foo");
        props.Should().ContainKey("bar");
        props["foo"].Type.Should().Be("string");
        props["bar"].Type.Should().Be("integer");
    }

    [Fact]
    public async Task ParseAsync_OneOfBody_InlineSpec_PropertiesMerged()
    {
        var specJson = """
            {
              "openapi": "3.0.0",
              "info": { "title": "OneOf Inline", "version": "1.0" },
              "servers": [{ "url": "https://api.example.com" }],
              "paths": {
                "/things": {
                  "post": {
                    "operationId": "createThing",
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": {
                            "oneOf": [
                              {
                                "type": "object",
                                "properties": {
                                  "digitalCode": { "type": "string" }
                                }
                              },
                              {
                                "type": "object",
                                "properties": {
                                  "physicalSku": { "type": "string" }
                                }
                              }
                            ]
                          }
                        }
                      }
                    },
                    "responses": { "201": { "description": "Created" } }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(specJson));

        var spec = await _parser.ParseAsync(stream, new OpenApiCoreConfig());

        var props = spec.Operations[0].Payload!.Properties.ToDictionary(p => p.Name);
        props.Should().ContainKey("digitalCode");
        props.Should().ContainKey("physicalSku");
    }
}
