using System.Text.Json.Serialization;

namespace HPD.Payments.Serialization.Wire;

/// <summary>Closed source-generated JSON metadata for the static serialization graph.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = false)]
[JsonSerializable(typeof(AuthorityWireDocument))]
[JsonSerializable(typeof(Dictionary<string, System.Text.Json.JsonElement>))]
public partial class PaymentsJsonContext : JsonSerializerContext;
