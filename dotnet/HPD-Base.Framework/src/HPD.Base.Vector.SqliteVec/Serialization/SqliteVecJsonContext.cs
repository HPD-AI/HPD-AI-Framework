using System.Text.Json.Serialization;

namespace HPD.Base.Vector.SqliteVec;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(float[]))]
internal sealed partial class SqliteVecJsonContext : JsonSerializerContext;
