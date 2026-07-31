using HPD.Base;
using HPD.Base.Sqlite;
using System.Text.Json.Serialization;

namespace HPD.Base.Sqlite;

[JsonSerializable(typeof(HPDBaseSqliteOptions))]
[JsonSerializable(typeof(RelationalStoreDescriptor))]
[JsonSerializable(typeof(RelationalTableDescriptor[]))]
[JsonSerializable(typeof(RelationalCollectionMappingDescriptor[]))]
[JsonSerializable(typeof(RelationalFieldMappingDescriptor[]))]
[JsonSerializable(typeof(RelationalQueryPlanDescriptor))]
[JsonSerializable(typeof(RelationalCapabilityDescriptor))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
public sealed partial class HPDBaseSqliteJsonSerializerContext : JsonSerializerContext;
