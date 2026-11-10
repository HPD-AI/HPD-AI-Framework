using System.Reflection;
using FluentAssertions;
using HPD.Agent;
using HPD.MultiAgent;

namespace HPD.MultiAgent.Tests;

public sealed class PublicSurfaceTests
{
    [Fact]
    public void PublicApiDoesNotExposeHpdGraphTypes()
    {
        var leaks = typeof(MultiAgent).Assembly
            .GetExportedTypes()
            .SelectMany(type => PublicMembers(type)
                .SelectMany(ReferencedTypes)
                .Append(type)
                .Append(type.BaseType)
                .Concat(type.GetInterfaces()))
            .Where(static type => type is not null)
            .Select(static type => Normalize(type!))
            .Where(static type =>
                type.Namespace?.StartsWith(
                    "HPD.Graph",
                    StringComparison.Ordinal) == true)
            .Distinct()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        leaks.Should().BeEmpty(
            "HPD.Graph is an internal execution dependency; public " +
            "HPD.MultiAgent contracts must use multi-agent-owned types");
    }

    [Fact]
    public void PublicApiDoesNotExposeHpdEventsImplementationTypes()
    {
        string[] implementationTypes =
        [
            "Event",
            "IEventCoordinator",
            "EventInbox`1",
            "EventInboxOptions",
            "EventSubscriptionOptions",
            "PendingRequestSnapshot",
            "RespondResult",
            "RespondStatus"
        ];

        var leaks = typeof(MultiAgent).Assembly
            .GetExportedTypes()
            .SelectMany(type => PublicMembers(type).SelectMany(ReferencedTypes))
            .Where(static type => type.Namespace == "HPD.Events")
            .Where(type => implementationTypes.Contains(type.Name, StringComparer.Ordinal))
            .Distinct()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        leaks.Should().BeEmpty(
            "HPD.Events remains a runtime dependency, but MultiAgent public " +
            "contracts must use AgentEvent and multi-agent-owned event types");
    }

    [Fact]
    public void MultiAgentWorkflowContractStreamsAgentEvents()
    {
        var execute = typeof(HPD.Agent.IMultiAgentWorkflow)
            .GetMethod(nameof(HPD.Agent.IMultiAgentWorkflow.ExecuteStreamingAsync));

        execute.Should().NotBeNull();
        var streamType = execute!.ReturnType;

        streamType.IsGenericType.Should().BeTrue();
        streamType.GetGenericTypeDefinition().Should().Be(typeof(IAsyncEnumerable<>));
        streamType.GetGenericArguments().Should().ContainSingle()
            .Which.Should().Be(typeof(AgentEvent));
    }

    private static IEnumerable<MemberInfo> PublicMembers(Type type) =>
        type.GetMembers(
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly);

    private static IEnumerable<Type> ReferencedTypes(MemberInfo member)
    {
        IEnumerable<Type> declared = member switch
        {
            MethodBase method =>
                method switch
                {
                    MethodInfo methodInfo =>
                        [methodInfo.ReturnType, .. methodInfo.GetParameters()
                            .Select(static parameter => parameter.ParameterType)],
                    ConstructorInfo constructor =>
                        constructor.GetParameters()
                            .Select(static parameter => parameter.ParameterType),
                    _ => []
                },
            PropertyInfo property => [property.PropertyType],
            FieldInfo field => [field.FieldType],
            EventInfo @event when @event.EventHandlerType is { } handler =>
                [handler],
            _ => []
        };

        return declared.SelectMany(Expand);
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var nested in Expand(element))
                yield return nested;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var nested in Expand(argument))
                    yield return nested;
            }
        }
    }

    private static Type Normalize(Type type) =>
        type is { IsGenericParameter: true, BaseType: { } baseType }
            ? baseType
            : type;
}
