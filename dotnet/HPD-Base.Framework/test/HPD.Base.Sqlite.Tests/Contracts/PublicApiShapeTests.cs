using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;

namespace HPD.Base.Sqlite.Tests.Contracts;

public sealed class PublicApiShapeTests
{
    [Fact]
    public void ProductionAssemblyDoesNotReferenceAntiScopePackages()
    {
        var names = typeof(SqliteRecordStore).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        names.Should().NotContain("Microsoft.EntityFrameworkCore");
        names.Should().NotContain("Dapper");
        names.Should().NotContain("Microsoft.AspNetCore");
        names.Any(name => name is not null && name.StartsWith("HPD.Auth", StringComparison.Ordinal)).Should().BeFalse();
        names.Should().NotContain(typeof(Expression).Assembly.GetName().Name);
    }

    [Fact]
    public void PublicApiDoesNotExposeProviderNativeConnectionOrRawSqlMethods()
    {
        var publicTypes = typeof(SqliteRecordStore).Assembly.GetExportedTypes();
        publicTypes.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => method.Name)
            .Should().NotContain(name => name.Contains("ExecuteSql", StringComparison.Ordinal) || name.Contains("Migration", StringComparison.Ordinal));
        publicTypes.Select(type => type.FullName).Any(name => name is not null && name.Contains("SqliteConnection", StringComparison.Ordinal)).Should().BeFalse();
        publicTypes.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .Any(type => type == typeof(DbConnection)
                || type == typeof(DbTransaction)
                || type.FullName?.Contains("SqliteConnection", StringComparison.Ordinal) == true
                || type.FullName?.Contains("SqliteTransaction", StringComparison.Ordinal) == true
                || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQueryable<>))
            .Should().BeFalse();
    }

    [Fact]
    public void RecordStoreHasOnlyTheExplicitOptionsAndLoggerFactoryConstructor()
    {
        var constructors = typeof(SqliteRecordStore).GetConstructors();

        constructors.Should().ContainSingle();
        constructors[0].GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(
            typeof(HPDBaseSqliteOptions),
            typeof(Microsoft.Extensions.Logging.ILoggerFactory));
    }

    [Fact]
    public void RecordStoreExposesOnlyTheExecutorMutationContracts()
    {
        typeof(SqliteRecordStore).GetInterfaces().Should().Contain(typeof(IRecordMutationStore));
        typeof(SqliteRecordStore).GetInterfaces().Should().Contain(typeof(IAtomicRecordStore));
        typeof(SqliteRecordStore).GetInterfaces()
            .Select(type => type.Name)
            .Should().NotContain("IRevisionedRecordStore");

        var obsoleteMutationMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "CreateAsync",
            "PatchAsync",
            "PatchIfRevisionAsync",
            "ReplaceAsync",
            "ReplaceIfRevisionAsync",
            "DeleteAsync"
        };
        typeof(SqliteRecordStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Should().NotContain(name => obsoleteMutationMethods.Contains(name));
    }

    [Fact]
    public async Task DependencyInjectionRegistersOnlyTheFinalMutationInterfaces()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseSqliteStore();

        services.Select(descriptor => descriptor.ServiceType.Name)
            .Should().NotContain("IRevisionedRecordStore");
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IRecordMutationStore));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IAtomicRecordStore));

        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRecordMutationStore>()
            .Should().BeSameAs(provider.GetRequiredService<IAtomicRecordStore>());
    }
}
