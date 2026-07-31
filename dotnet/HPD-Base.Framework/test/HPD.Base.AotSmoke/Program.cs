using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.AotSmoke;

var collection = AotProject.Collection;
var value = new AotProject
{
    OrganizationId = "org_aot",
    Name = "AOT",
};

string json = JsonSerializer.Serialize(value, collection.JsonTypeInfo);
var roundTrip = JsonSerializer.Deserialize(json, collection.JsonTypeInfo);

if (roundTrip is null ||
    roundTrip.OrganizationId != "org_aot" ||
    AotProject.Fields.OrganizationId.Path != "organizationId" ||
    !AotProject.Fields.Name.Operators.HasFlag(BaseFieldOperator.Order))
{
    throw new InvalidOperationException(
        "Generated application contracts must survive Native AOT.");
}

namespace HPD.Base.AotSmoke
{
    [BaseCollection("aot.projects", typeof(AotApplicationJsonContext))]
    internal sealed partial record AotProject
    {
        public required string OrganizationId { get; init; }

        [BaseField(Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
        public required string Name { get; init; }
    }

    [JsonSerializable(typeof(AotProject))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal sealed partial class AotApplicationJsonContext : JsonSerializerContext;
}
