using HPD.Agent.ToolHarness.Coding.Debugging.Generated;
using HPD.Agent.ToolHarness.Coding.Debugging.Adapters;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using HPD.Agent;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Diagnostics.CodeAnalysis;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public static class DebuggingServiceCollectionExtensions
{
    public static AgentBuilder WithHPDCodingDebuggingRuntime(this AgentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMiddleware(new DebugRuntimeAttachmentMiddleware());
    }

    public static IServiceCollection AddHPDCodingDebugging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        global::CodingHarnessEventSerialization.RegisterEvents();
        services.TryAddSingleton<StandardDebugAdapterFactory>();
        services.TryAddSingleton<DebugPyAdapterFactory>();
        services.TryAddSingleton<CodeLldbAdapterFactory>();
        services.TryAddSingleton<DelveAdapterFactory>();
        services.TryAddSingleton<JavaScriptDebugAdapterFactory>();
        services.TryAddSingleton<IDebugAdapterToolSearchPolicy, CatalogCommandDebugAdapterToolSearchPolicy>();
        services.TryAddSingleton<IDebugAdapterToolResolver, EnvironmentDebugAdapterToolResolver>();
        services.TryAddTransient<DebugRuntimeAttachmentMiddleware>();
        services.TryAddTransient<IDebugTerminalRecordStore, DebugTerminalRecordStore>();
        services.TryAddSingleton(new DebugTerminalRecordStoreOptions());
        services.TryAddSingleton<IDebugAdapterAvailabilityCache, DebugAdapterAvailabilityCache>();
        services.TryAddSingleton<IDebugAdapterTrustPolicy, DenyByDefaultDebugAdapterTrustPolicy>();
        services.TryAddSingleton<IDebugWorkspaceCanonicalizer, LexicalDebugWorkspaceCanonicalizer>();
        services.TryAddSingleton<IWorkspaceRootMarkerResolver, WorkspaceRootMarkerResolver>();
        services.TryAddSingleton<IDebugAdapterConfigurationComposer, BuiltInDebugAdapterConfigurationComposer>();
        services.TryAddSingleton<IDebugEndpointResolver, DenyAllDebugEndpointResolver>();
        services.TryAddSingleton<IDebugAdapterCatalogFailurePolicy, FailStartupDebugAdapterCatalogFailurePolicy>();
        services.TryAddSingleton<DebugAdapterSelector>();
        services.TryAddSingleton<DebugInitializePolicy>();
        services.TryAddSingleton<DebugProtocolTransportFactory>();
        services.TryAddSingleton<DebugProtocolSessionStarter>();
        services.TryAddSingleton<IDebugProtocolSessionStarter>(
            services => services.GetRequiredService<DebugProtocolSessionStarter>());
        services.TryAddSingleton<IDebugAdapterDiagnosticStore, DebugAdapterDiagnosticStore>();
        services.TryAddSingleton<IDotNetDebugProjectEvaluator, DotNetDebugProjectEvaluator>();
        services.TryAddSingleton<
            IDebugExecutableArtifactClassifier,
            DotNetDebugExecutableArtifactClassifier>();
        services.TryAddSingleton<DebugExecutionPlannerServices>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IDebugExecutionTargetPlanner, DirectSourceDebugExecutionTargetPlanner>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IDebugExecutionTargetPlanner, DirectExecutableDebugExecutionTargetPlanner>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IDebugExecutionTargetPlanner, DotNetApplicationDebugExecutionTargetPlanner>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IDebugExecutionTargetPlanner, DotNetTestDebugExecutionTargetPlanner>());
        services.TryAddSingleton<DebugExecutionTargetPlannerRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IDebugHostReadinessParser, VSTestHostDebugReadinessParser>());
        services.TryAddSingleton<DebugHostReadinessParserRegistry>();
        services.TryAddSingleton<IDebugExecutionPlanActivator, DebugExecutionPlanActivator>();
        services.TryAddSingleton<DebugExecutionStartOrchestrator>();
        services.TryAddSingleton<DebugStartResultProjector>();
        services.TryAddSingleton<DebugRuntimeServiceFactory>();
        services.TryAddSingleton<DebugResultFormatter>();
        services.TryAddSingleton<DebugPermissionAuthorizationService>();
        services.TryAddTransient<DebugPermissionMiddleware>();
        services.TryAddSingleton(provider => new DebugExecutionPlanningService(
            provider.GetRequiredService<DebugExecutionTargetPlannerRegistry>(),
            provider.GetRequiredService<DebugAdapterSelector>(),
            provider.GetRequiredService<DebugAdapterCatalog>(),
            provider.GetRequiredService<IWorkspaceRootMarkerResolver>(),
            provider.GetRequiredService<IDebugAdapterConfigurationComposer>(),
            provider.GetRequiredService<IDebugAdapterTrustPolicy>(),
            provider.GetRequiredService<DebugExecutionStartOrchestrator>(),
            provider.GetRequiredService<DebugStartResultProjector>(),
            provider.GetRequiredService<IDebugAdapterDiagnosticStore>(),
            provider.GetService<IDebugHostRequestBroker>(),
            provider.GetService<IDebugChildSessionPlanFactory>()));
        services.TryAddSingleton(provider => new DebugOperationDispatcher(
            provider.GetRequiredService<DebugRuntimeServiceFactory>(),
            provider.GetRequiredService<DebugResultFormatter>(),
            provider.GetRequiredService<DebugPermissionAuthorizationService>(),
            provider.GetRequiredService<DebugExecutionPlanningService>()));
        services.TryAddSingleton<DebugAdapterExtensionRegistry>();
        services.TryAddSingleton<IDebugAdapterExtensionHost>(provider =>
            provider.GetRequiredService<DebugAdapterExtensionRegistry>());
        services.TryAddSingleton(provider => new DebugAdapterCatalog(
            provider.GetServices<IDebugAdapterCatalogProvider>(),
            provider,
            provider.GetRequiredService<IDebugAdapterCatalogFailurePolicy>()));
        return services;
    }

    public static IServiceCollection AddHPDDebugAdapterExtension<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExtension>(this IServiceCollection services)
        where TExtension : class, IDebugAdapterExtensionRegistration
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDebugAdapterExtensionRegistration, TExtension>());
        return services;
    }

    public static IServiceCollection AddHPDBuiltInDebugAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDebugAdapterCatalogProvider, GeneratedDebugAdapterCatalogProvider_HPD_Agent_Harness_Coding>());
        return services;
    }
}
