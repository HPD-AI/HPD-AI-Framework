using System.ComponentModel;
using System.Text.Json.Serialization;

namespace HPD.Agent.Tests.StructuredOutput;

/// <summary>
/// Test model for structured output tests.
/// </summary>
[Description("A simple response with name and value")]
public sealed record TestResponse(
    [property: Description("The name of the item")] string Name,
    [property: Description("The numeric value")] int Value,
    [property: Description("Optional description")] string? Description = null
);

/// <summary>
/// Test model for nested structured output.
/// </summary>
[Description("A response containing nested data")]
public sealed record NestedResponse(
    [property: Description("The title")] string Title,
    [property: Description("Nested response data")] TestResponse Inner
);

/// <summary>
/// Test model with array property.
/// </summary>
[Description("A response with a list of items")]
public sealed record ArrayResponse(
    [property: Description("The title")] string Title,
    [property: Description("List of string items")] List<string> Items
);

/// <summary>
/// Test model with enum property.
/// </summary>
[Description("An animal entity")]
public sealed record Animal(
    [property: Description("Unique identifier")] int Id,
    [property: Description("Full name of the animal")] string FullName,
    [property: Description("The species type")] Species Species
);

/// <summary>
/// Species enum for animal testing.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Species
{
    Unknown,
    Cat,
    Dog,
    Tiger,
    Lion
}

/// <summary>
/// Base type for union type testing.
/// </summary>
[Description("Base API response type")]
public abstract record ApiResponse;

/// <summary>
/// Success response for union type testing.
/// </summary>
[Description("A successful API response")]
public sealed record SuccessResponse(
    [property: Description("The response data")] string Data,
    [property: Description("Response code")] int Code = 200
) : ApiResponse;

/// <summary>
/// Error response for union type testing.
/// </summary>
[Description("An error API response")]
public sealed record ErrorResponse(
    [property: Description("Error code")] string ErrorCode,
    [property: Description("Error message")] string Message
) : ApiResponse;

/// <summary>
/// AOT-compatible JSON serializer context for test models.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TestResponse))]
[JsonSerializable(typeof(NestedResponse))]
[JsonSerializable(typeof(ArrayResponse))]
[JsonSerializable(typeof(Animal))]
[JsonSerializable(typeof(Species))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(ApiResponse))]
[JsonSerializable(typeof(SuccessResponse))]
[JsonSerializable(typeof(ErrorResponse))]
public partial class TestJsonSerializerContext : JsonSerializerContext;
