using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.AotSmoke;
using Microsoft.Extensions.DependencyInjection;

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
    AotProject.Fields.OrganizationId.Id != "organization-id" ||
    AotProject.Fields.OrganizationId.StoredName != "organizationId" ||
    !AotProject.Fields.Name.Operators.HasFlag(BaseFieldOperator.Order))
{
    throw new InvalidOperationException(
        "Generated application contracts must survive Native AOT.");
}

var services = new ServiceCollection();
services.AddLogging();
services.AddHPDBase(hpd => hpd.AddCollection(collection));
using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateOnBuild = true });
var session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
{
    AuthenticationState = PrincipalAuthenticationState.System,
    SubjectId = "aot"
});
_ = session.Collection(collection);
if (provider.GetRequiredService<HPDBaseInstalledFeatures>().Provider != "volatile"
    || provider.GetRequiredService<IRecordStore>().Capabilities.StoreKind != BaseStoreKinds.Volatile)
{
    throw new InvalidOperationException(
        "The built-in volatile provider must be the Native AOT-safe default.");
}

namespace HPD.Base.AotSmoke
{
    [BaseCollection("aot.projects", typeof(AotApplicationJsonContext))]
    internal sealed partial record AotProject
    {
        [BaseField("organization-id")]
        public required string OrganizationId { get; init; }

        [BaseField("name", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
        public required string Name { get; init; }
    }

    [JsonSerializable(typeof(AotProject))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal sealed partial class AotApplicationJsonContext : JsonSerializerContext;
}
