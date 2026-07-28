using System.Security.Claims;
using HPD.Base;
using HPD.Base.Auth.HPDAuth;
using HPD.Base.Auth.HPDAuth.Configuration;
using HPD.Base.Auth.HPDAuth.DependencyInjection;
using HPD.Base.Auth.HPDAuth.Policy;
using HPD.Base.Policy;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
services.AddLogging();
services.AddHPDBaseHPDAuth(options =>
{
    options.RequireHPDAuthServices = false;
    options.CollectionRules =
    [
        new HPDAuthBaseCollectionRule
        {
            CollectionId = "items",
            ReadRoles = ["Reader"],
            TenantFieldPath = "tenantId"
        }
    ];
});

using var provider = services.BuildServiceProvider();
var mapper = provider.GetRequiredService<HPDAuthBaseSubjectMapper>();
var principal = mapper.Map(new ClaimsPrincipal(new ClaimsIdentity(
[
    new Claim("sub", "user-1"),
    new Claim("role", "Reader"),
    new Claim("instance_id", "tenant-1")
], "HPD")));

var evaluator = provider.GetRequiredService<IPolicyEvaluator>();
var decision = await evaluator.EvaluateAsync(new PolicyEvaluationRequest
{
    Principal = principal,
    Operation = new OperationContext
    {
        Operation = BaseOperationKind.List,
        CollectionId = "items",
        Now = DateTimeOffset.UnixEpoch
    },
    Collection = new CollectionDefinition
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    },
    Resource = new PolicyResource { Kind = PolicyResourceKind.Query }
});

Require(decision.Effect == PolicyEffect.Allow, "HPD.Auth adapter policy did not allow the smoke principal.");
Require(decision.Constraints?.RecordFilter is not null, "HPD.Auth adapter tenant filter was not emitted.");

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
