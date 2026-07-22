using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Skills;

public sealed class CapabilityGraphTests
{
    [Fact]
    public void Create_BuildsStableIdAndModelNameIndexes()
    {
        var activation = Node("skill:data", "data_analysis", HPDCapabilityKind.SkillActivation,
            children: [CapabilityId.Create("function:validate")]);
        var function = Node("function:validate", "validate_dataset", HPDCapabilityKind.Function,
            parents: [activation.Id]);

        var graph = CapabilityGraph.Create([activation, function]);

        graph.Nodes.Should().ContainKeys(activation.Id, function.Id);
        graph.ModelNames["validate_dataset"].Should().Be(function.Id);
    }

    [Fact]
    public void Create_RejectsDuplicateModelNames()
    {
        var first = Node("function:first", "duplicate", HPDCapabilityKind.Function);
        var second = Node("function:second", "duplicate", HPDCapabilityKind.Function);

        var act = () => CapabilityGraph.Create([first, second]);

        act.Should().Throw<CapabilityGraphValidationException>()
            .WithMessage("*Duplicate model-facing capability name 'duplicate'*");
    }

    [Fact]
    public void Create_RejectsMissingParents()
    {
        var node = Node("function:child", "child", HPDCapabilityKind.Function,
            parents: [CapabilityId.Create("skill:missing")]);

        var act = () => CapabilityGraph.Create([node]);

        act.Should().Throw<CapabilityGraphValidationException>()
            .WithMessage("*missing parent 'skill:missing'*");
    }

    [Fact]
    public void Create_RejectsRevealCycles()
    {
        var firstId = CapabilityId.Create("skill:first");
        var secondId = CapabilityId.Create("skill:second");
        var first = Node(firstId.Value, "first", HPDCapabilityKind.SkillActivation, children: [secondId]);
        var second = Node(secondId.Value, "second", HPDCapabilityKind.SkillActivation, children: [firstId]);

        var act = () => CapabilityGraph.Create([first, second]);

        act.Should().Throw<CapabilityGraphValidationException>()
            .WithMessage("*contains a cycle*");
    }

    [Fact]
    public void IsVisible_UsesOrSemanticsAcrossAlternativeParents()
    {
        var firstParent = CapabilityId.Create("skill:first");
        var secondParent = CapabilityId.Create("skill:second");
        var child = Node("function:shared", "shared", HPDCapabilityKind.Function,
            parents: [firstParent, secondParent]);

        CapabilityGraph.IsVisible(child, ImmutableHashSet.Create(secondParent)).Should().BeTrue();
        CapabilityGraph.IsVisible(child, ImmutableHashSet<CapabilityId>.Empty).Should().BeFalse();
    }

    [Fact]
    public void IsVisible_HidesAnActiveActivationContainer()
    {
        var activation = Node("skill:data", "data_analysis", HPDCapabilityKind.SkillActivation);

        CapabilityGraph.IsVisible(activation, ImmutableHashSet.Create(activation.Id)).Should().BeFalse();
    }

    private static CapabilityNode Node(
        string id,
        string name,
        HPDCapabilityKind kind,
        ImmutableArray<CapabilityId> parents = default,
        ImmutableArray<CapabilityId> children = default) =>
        new()
        {
            Id = CapabilityId.Create(id),
            Function = AIFunctionFactory.Create(() => "ok", name, $"Executes {name}."),
            Kind = kind,
            ParentContainerIds = parents.IsDefault ? [] : parents,
            Children = children.IsDefault ? [] : children
        };
}
