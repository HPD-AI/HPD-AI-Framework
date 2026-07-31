using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Xunit;

namespace HPD.Base.Tests.Application.Hosting;

public sealed class ApplicationPublicSurfaceTests
{
    [Fact]
    public void NormalApplicationSurfaceDoesNotExposeCanonicalConstructionPlumbing()
    {
        PropertyInfo[] builderProperties = typeof(HPDBaseBuilder)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);
        builderProperties.Should().NotContain(
            property => property.PropertyType == typeof(IServiceCollection));

        Type[] forbidden =
        [
            typeof(OperationContext),
            typeof(HPD.Base.RecordPayload),
            typeof(HPD.Base.BaseRecordBatchItem),
            typeof(HPD.Base.QueryValue),
            typeof(IFileObjectService),
            typeof(FileOperationContext),
        ];
        Type[] applicationTypes =
        [
            typeof(HPDBaseBuilder),
            typeof(BaseSession),
            typeof(BaseCollection<>),
            typeof(BaseRecord<>),
            typeof(BaseBatchBuilder),
            typeof(BaseSessionLiveQueries),
        ];
        MemberInfo[] publicMembers = applicationTypes
            .SelectMany(PublicMembers)
            .ToArray();

        foreach (Type type in forbidden)
        {
            publicMembers.Should().NotContain(
                member => SignatureTypes(member).Any(candidate => candidate == type),
                $"ordinary application members must not expose {type.FullName}");
        }

        Type[] eventTypes = publicMembers
            .SelectMany(SignatureTypes)
            .Where(type =>
                type.Namespace is string ns &&
                ns.StartsWith("HPD.Events", StringComparison.Ordinal))
            .ToArray();
        eventTypes.Should().BeEmpty();
    }

    [Fact]
    public void GeneratorAssemblyIsNotARuntimeOrProviderDependency()
    {
        Assembly[] runtimeAssemblies =
        [
            typeof(IBaseRecordRuntime).Assembly,
            typeof(SqliteRecordStore).Assembly,
        ];

        runtimeAssemblies.Should().AllSatisfy(assembly =>
            assembly.GetReferencedAssemblies().Should().NotContain(
                reference => reference.Name == "HPD.Base.Generators"));
    }

    private static IEnumerable<MemberInfo> PublicMembers(Type type) =>
        type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
            .Where(member => member.MemberType is
                MemberTypes.Constructor or
                MemberTypes.Method or
                MemberTypes.Property);

    private static IEnumerable<Type> SignatureTypes(MemberInfo member) =>
        member switch
        {
            MethodInfo method =>
                [method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)],
            ConstructorInfo constructor =>
                constructor.GetParameters().Select(parameter => parameter.ParameterType),
            PropertyInfo property => [property.PropertyType],
            _ => [],
        };
}
