using System.Text.Json;
using HPD.Base;
using HPD.Base.AspNetCore;
using HPD.Base.AspNetCore.DependencyInjection;
using HPD.Base.InMemory.DependencyInjection;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Stores;
using HPD.Base.Schema;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSingleton<IPolicyEvaluator, SmokePolicyEvaluator>();
builder.Services.AddHPDBaseRuntime()
    .AddHPDBaseAspNetCore()
    .AddHPDBaseInMemoryStore(options =>
    {
        options.StoreId = "primary";
        options.CollectionIds = ["items"];
        options.Collections =
        [
            new CollectionDefinition
            {
                Id = "items",
                Name = "items",
                Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose,
                UnknownFields = UnknownFieldPolicy.Preserve,
                Operations = new CollectionOperationMatrix
                {
                    List = true,
                    Get = true,
                    Create = true,
                    Patch = true,
                    Replace = true,
                    Delete = true
                }
            }
        ];
    });

var app = builder.Build();
app.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(app.Services);
await app.Services.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
app.MapHPDBaseApi();

app.MapGet("/", () => "HPD.Base.AspNetCore AOT smoke");

await app.RunAsync();

internal sealed class SmokePolicyEvaluator : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = request;
        return ValueTask.FromResult(new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = PolicyOutcome.Allowed
        });
    }
}
