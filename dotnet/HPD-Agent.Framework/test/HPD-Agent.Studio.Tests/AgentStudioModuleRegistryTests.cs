using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Studio.Tests;

/// <summary>Verifies Agent Studio's frozen semantic and generated-client graph.</summary>
public sealed class AgentStudioModuleRegistryTests
{
    [Fact]
    public void Registry_is_agent_owned_and_uses_shared_studio_contracts()
    {
        StudioModuleRegistration module = AgentStudioModuleRegistry.Create(Snapshot());

        Assert.Equal("agents", module.Identity.ModuleId);
        Assert.Equal(18, module.Pages.Length);
        Assert.Empty(module.Pages.Single(value => value.PageId == "agent.catalog").AcceptedResources);
        Assert.Equal(StudioResourceKind.Application,
            Assert.Single(module.Pages.Single(value => value.PageId == "agent.creation").AcceptedResources));
        Assert.Equal(
            [StudioResourceKind.Application, StudioResourceKind.Agent, StudioResourceKind.AgentSession, StudioResourceKind.AgentThread,
             StudioResourceKind.AgentExecution, StudioResourceKind.ToolHarness, StudioResourceKind.AgentTrace,
             StudioResourceKind.AiFunction, StudioResourceKind.AiFunctionAction],
            module.Resources.Select(static value => value.Kind));
        Assert.Equal(7, module.Links.Length);
        Assert.Equal(StudioFrameworkClientProtocol.FrameworkGeneratedContractV1, module.Clients.Single().Protocol);
        Assert.Equal(14, module.Commands.Length);
        StudioCommandRegistration update = module.Commands.Single(value => value.CommandId == "agent.configuration.update");
        Assert.Equal("agent.configuration.update", update.CommandId);
        Assert.Equal(StudioActionClass.OperationalTransition, update.ActionClass);
        Assert.Equal(
            ["agent.client-tool.respond", "agent.configuration.update", "agent.continuation.respond", "agent.create",
             "agent.execution.cancel", "agent.input.steer", "agent.input.submit",
             "agent.permission.respond", "agent.question.respond", "agent.session.create",
             "agent.session.delete", "agent.thread.create", "agent.thread.delete", "agent.thread.fork"],
            module.Commands.Select(static value => value.CommandId));
        Assert.Equal(StudioActionClass.Routine,
            module.Commands.Single(value => value.CommandId == "agent.session.create").ActionClass);
        Assert.Equal(StudioActionClass.Routine,
            module.Commands.Single(value => value.CommandId == "agent.input.submit").ActionClass);
        Assert.Equal(StudioActionClass.OperationalTransition,
            module.Commands.Single(value => value.CommandId == "agent.input.steer").ActionClass);
        Assert.Equal(StudioActionClass.OperationalTransition,
            module.Commands.Single(value => value.CommandId == "agent.thread.fork").ActionClass);
        Assert.Equal(StudioActionClass.Routine,
            module.Commands.Single(value => value.CommandId == "agent.permission.respond").ActionClass);
        Assert.Equal(StudioActionClass.Routine,
            module.Commands.Single(value => value.CommandId == "agent.question.respond").ActionClass);
        Assert.Equal(StudioActionClass.Routine,
            module.Commands.Single(value => value.CommandId == "agent.continuation.respond").ActionClass);
        Assert.Equal(StudioActionClass.Routine,
            module.Commands.Single(value => value.CommandId == "agent.client-tool.respond").ActionClass);
        Assert.Equal(StudioActionClass.OperationalTransition,
            module.Commands.Single(value => value.CommandId == "agent.create").ActionClass);
        Assert.Equal(StudioActionClass.Destructive,
            module.Commands.Single(value => value.CommandId == "agent.session.delete").ActionClass);
        Assert.Equal(StudioActionClass.Destructive,
            module.Commands.Single(value => value.CommandId == "agent.thread.delete").ActionClass);
        Assert.Equal(module.Pages.Select(static value => value.PageId), module.Frontend.Components.Select(static value => value.PageId));

        using ServiceProvider runtimeServices = new ServiceCollection().BuildServiceProvider();
        StudioModuleRuntimeContribution runtime = new AgentStudioRuntimeContributionFactory(
            runtimeServices, new Inspection()).Create(module);
        Assert.Equal(39, runtime.Producers.Length);
        Assert.Contains(runtime.Methods, value =>
            value.RegisteredMethodId == "agent.studio.resolve.application" &&
            value.Kind == StudioMethodKind.Resolve);
        Assert.Contains(runtime.Methods, value =>
            value.RegisteredMethodId == "agent.studio.subscribe.thread.activity" &&
            value.Kind == StudioMethodKind.InvalidationSubscribe);
        Assert.Contains(runtime.Methods, value =>
            value.RegisteredMethodId == "agent.studio.subscribe.events" &&
            value.Kind == StudioMethodKind.InvalidationSubscribe);
    }

    [Fact]
    public void Extension_registers_the_typed_agent_module_and_asset()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(ConfigureBase);
        services.AddHPDAIPlatform().AddAgentStudio();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal("agents",
            provider.GetRequiredService<StudioEditionAssetCatalogProvider>().GetRequiredCatalog().Single().ModuleId);
    }

    [Fact]
    public void Missing_fixed_grant_rejects_registration()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder => ConfigureBase(builder, "base.studio.bootstrap.read"));
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() =>
            AgentStudioModuleRegistry.Create(provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>()));
    }

    private static HPDBaseStudioAuthoritySnapshot Snapshot()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(ConfigureBase);
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>();
    }

    private static void ConfigureBase(HPDBaseBuilder builder) => ConfigureBase(builder, null);

    private static void ConfigureBase(HPDBaseBuilder builder, string? omit)
    {
        builder.ConfigureSchema(static options => options.ApplicationId = "sample.application");
        foreach (string id in new[]
                 {
                     "base.studio.action.discover", "base.studio.action.execute", "base.studio.action.preview",
                     "base.studio.bootstrap.read", "base.studio.resource.discover", "base.studio.resource.inspect",
                 }
                     .Where(value => value != omit))
            builder.AddStaticGrantAuthority(
                new BaseGrantAuthorityDefinition
                {
                    Id = id,
                    Version = 1,
                    OwningModuleId = "base",
                    SourceContractId = "base.studio.fixed-grant",
                    SourceContractVersion = 1,
                },
                new AccessGrant
                {
                    Id = id,
                    ApplicationId = "sample.application",
                    ModuleId = "base",
                    Audience = HPDBaseEndpointAudience.ControlPlane,
                    Subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = "operator" },
                    Action = id,
                    Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
                });
    }

    private sealed class Inspection : IAgentStudioInspectionAuthority
    {
        public ValueTask<StudioFrameworkSurfaceResponse?> ObserveAsync(
            string operationId, string relativePath, string query, string applicationId,
            System.Security.Claims.ClaimsPrincipal principal, CancellationToken cancellationToken) =>
            ValueTask.FromResult<StudioFrameworkSurfaceResponse?>(null);

        public ValueTask<StudioFrameworkSurfaceResponse?> PreflightAsync(
            string operationId, string relativePath, ReadOnlyMemory<byte> requestBody,
            string applicationId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<StudioFrameworkSurfaceResponse?>(null);

        public ValueTask<bool> ExistsAsync(StudioResourceIdentity resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }
}
