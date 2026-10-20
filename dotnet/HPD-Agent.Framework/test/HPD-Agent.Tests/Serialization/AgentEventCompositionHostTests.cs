using FluentAssertions;
using HPD.Agent.Serialization;

namespace HPD.Agent.Tests.Serialization;

public sealed class AgentEventCompositionHostTests
{
    [Fact]
    public void GeneratedLibraryModuleIsRegisteredAndComposableWithoutApplicationComposition()
    {
        var coreModule = CoreAgentEventModule.Fragment;

        AgentEventCompositionHost.GetModuleIdentities()
            .Should()
            .Contain(coreModule.ModuleId);
        AgentEventCompositionHost.TryComposeRegisteredModules(out var composition)
            .Should()
            .BeTrue();
        composition.Fragments
            .Should()
            .Contain(fragment => fragment.ModuleId == coreModule.ModuleId);
    }

    [Fact]
    public void RegisteringTheSameGeneratedModuleIsIdempotent()
    {
        var module = CoreAgentEventModule.Fragment;

        AgentEventCompositionHost.RegisterModule(module, "HPD-Agent");
        AgentEventCompositionHost.RegisterModule(module, "HPD-Agent");
    }

    [Fact]
    public void RegisteringAConflictingModuleIdentityFails()
    {
        var module = CoreAgentEventModule.Fragment;
        var conflicting = new AgentEventModuleFragment
        {
            ModuleId = module.ModuleId,
            Events = []
        };

        var action = () => AgentEventCompositionHost.RegisterModule(conflicting, "conflicting-library");

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"Module '{module.ModuleId}'*conflicts*");
    }
}
