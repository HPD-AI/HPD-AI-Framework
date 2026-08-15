using System.Text.Json.Serialization;

namespace HPD.Base.Sqlite;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(float[]))]
internal sealed partial class SqliteVecJsonContext : JsonSerializerContext;
