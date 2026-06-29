using System.Text.Json.Serialization;
using HPD.Base.Records;

namespace HPD.Base.Abstractions.AotSmoke;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SampleAppPayload))]
[JsonSerializable(typeof(RecordEnvelope<SampleAppPayload>))]
public partial class SampleAppJsonSerializerContext : JsonSerializerContext;
