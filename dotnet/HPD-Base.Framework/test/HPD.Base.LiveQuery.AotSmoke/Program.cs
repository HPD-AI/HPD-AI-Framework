using HPD.Base.Dependencies;
using HPD.Base.LiveQuery;
using HPD.Base.LiveQuery.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHPDBaseLiveQuery();
using var provider = services.BuildServiceProvider();
var coordinator = provider.GetRequiredService<IBaseLiveQueryCoordinator>();
await using var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<string>
{
    QueryId = "aot.smoke",
    ExecuteAsync = _ => ValueTask.FromResult(new BaseLiveQueryEvaluation<string>
    {
        Value = "ready",
        Dependencies = new BaseDependencySet
        {
            References =
            [
                new BaseDependencyReference
                {
                    TemplateId = "aot.record",
                    Value = "opaque"
                }
            ]
        }
    })
});

await using var transitions = subscription.Transitions.GetAsyncEnumerator();
if (!await transitions.MoveNextAsync()
    || transitions.Current.Kind != BaseLiveQueryTransitionKind.Snapshot
    || transitions.Current.Value != "ready")
{
    throw new InvalidOperationException("The live-query AOT smoke contract failed.");
}
