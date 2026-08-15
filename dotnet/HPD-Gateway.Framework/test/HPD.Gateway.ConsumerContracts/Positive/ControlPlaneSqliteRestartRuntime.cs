using System.Collections.Immutable;
using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using HPD.Gateway.ControlPlane.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

string root = Path.Combine(Path.GetTempPath(), $"hpd-gateway-package-restart-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
string database = Path.Combine(root, "gateway.db");
try
{
    var firstActivator = new RecordingActivator();
    using (IHost first = CreateHost(database, firstActivator))
    {
        await first.StartAsync();
        IGatewayManagementStatusReader status = first.Services.GetRequiredService<IGatewayManagementStatusReader>();
        GatewayManagementStatusSnapshot ready = await status.GetCurrentAsync("namespace-a", "node-a", null);
        if (!ready.AuthorityReady || ready.Durability != GatewayAuthorityDurability.RestartDurable)
            throw new InvalidOperationException("The package-only SQLite authority did not become restart-durable and ready.");

        IGatewayManagementCommandCoordinator commands = first.Services.GetRequiredService<IGatewayManagementCommandCoordinator>();
        var actor = new GatewayManagementActor("package-consumer", "system", "package-runtime");
        GatewayManagementCommandResult provisioned = await commands.ProvisionLocalTargetAsync(new(
            "namespace-a", "node-a", "provision-a", actor, "correlation-a"));
        if (!provisioned.IsAccepted) throw new InvalidOperationException(provisioned.Code);
        GatewayManagementCommandResult submitted = await commands.SubmitAsync(new(
            "namespace-a", "node-a", "submit-a", actor, "correlation-b", "package", "fixture", null,
            Configuration().ToCanonicalDocument().Utf8Json, Activate: true));
        if (!submitted.IsAccepted || submitted.ActivationIntentId is null)
            throw new InvalidOperationException(submitted.Code);
        await first.StopAsync();
        if (firstActivator.Count != 0)
            throw new InvalidOperationException("The first process consumed work before the restart boundary was established.");
    }

    var restartedActivator = new RecordingActivator();
    using (IHost restarted = CreateHost(database, restartedActivator))
    {
        await restarted.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (restartedActivator.Count == 0)
            await Task.Delay(25, timeout.Token);
        await restarted.StopAsync();
    }
    Console.WriteLine("HPD Gateway package-only SQLite startup and restart reconciliation passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static IHost CreateHost(string database, IGatewayNodeActivator activator)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Services.AddHpdGateway(gateway => gateway.EnableCoreDeclarations());
    builder.Services.AddHpdGatewayControlPlane(controlPlane => controlPlane.UseSqlite(options =>
    {
        options.DataSource = database;
        options.PlanProtectionKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
        options.TokenProtectionKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
        options.DesiredStateTokenKey = Enumerable.Repeat((byte)0x33, 32).ToArray();
        options.EpochReservationKey = Enumerable.Repeat((byte)0x44, 32).ToArray();
    }));
    builder.Services.Replace(ServiceDescriptor.Singleton<IGatewayNodeActivator>(activator));
    return builder.Build();
}

static GatewayConfiguration Configuration() => new()
{
    SchemaVersion = new GatewaySchemaVersion(1, 0),
    CanonicalizationVersion = 1,
    Upstreams =
    [
        new UpstreamDeclaration
        {
            Id = new UpstreamId("backend"),
            Endpoints = new StaticEndpointSource
            {
                Destinations =
                [
                    new DestinationDeclaration
                    {
                        Id = new DestinationId("primary"),
                        Address = new Uri("http://127.0.0.1:59999"),
                    },
                ],
            },
        },
    ],
    Routes =
    [
        new RouteDeclaration
        {
            Id = new RouteId("api"),
            Match = new HttpRouteMatch { Path = "/api/{**catch-all}" },
            Upstream = new UpstreamId("backend"),
        },
    ],
};

sealed class RecordingActivator : IGatewayNodeActivator
{
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public ValueTask<GatewayNodeActivationResult> ActivateAsync(
        GatewayNodeActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return ValueTask.FromResult(new GatewayNodeActivationResult(
            GatewayNodeActivationState.RejectedBeforePublish,
            null,
            null,
            null,
            ImmutableArray.Create(new GatewayNodeActivationDiagnostic(
                "package-runtime.observed", "$", "Restarted work reached the node activator."))));
    }
}
