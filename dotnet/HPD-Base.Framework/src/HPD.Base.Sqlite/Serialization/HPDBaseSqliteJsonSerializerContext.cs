using HPD.Base.Relational.Descriptors;
using HPD.Base.Relational.Capabilities;
using HPD.Base.Relational.Planning;
using HPD.Base.Sqlite.Configuration;
using System.Text.Json.Serialization;

namespace HPD.Base.Sqlite.Serialization;

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
