using System.Reflection;
using HPD.Base;

namespace HPD.Base.Tests.Relational.AntiScope;

public sealed class RelationalAbstractionsAntiScopeTests
{
    private static readonly Assembly RelationalAssembly = typeof(RelationalStoreDescriptor).Assembly;

    [Fact]
    public void ProductionAssemblyReferencesOnlyBaseAbstractionsAndFrameworkAssemblies()
    {
        var references = RelationalAssembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("Microsoft.AspNetCore", references);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
        Assert.DoesNotContain("Dapper", references);
        Assert.DoesNotContain("Npgsql", references);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", references);
        Assert.DoesNotContain("System.Data.Common", references);
    }

    [Fact]
    public void PublicTypeNamesDoNotExposeOrmQueryEngineOrProviderSurfaces()
    {
        var bannedFragments = new[]
        {
            "DbContext",
            "DbSet",
            "IQueryable",
            "SqlExecutor",
            "SqlCommand",
            "MigrationRunner",
            "DbConnection",
            "Dapper",
            "Npgsql",
            "Sqlite",
            "AspNetCore",
            "Linq",
            "Entity"
        };

        var publicTypeNames = RelationalAssembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        foreach (var banned in bannedFragments)
        {
            Assert.DoesNotContain(publicTypeNames, name => name.Contains(banned, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ProviderContractsAreReadOnlyMetadataMappingAndExplainOnly()
    {
        var providerMethods = new[]
            {
                typeof(IRelationalMetadataProvider),
                typeof(IRelationalCollectionMappingProvider),
                typeof(IRelationalQueryPlanExplainer)
            }
            .SelectMany(type => type.GetMethods())
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal(
            ["GetStoreAsync", "ListTablesAsync", "ListViewsAsync", "GetMappingAsync", "ListMappingsAsync", "ExplainAsync"],
            providerMethods);

        foreach (var name in providerMethods)
        {
            Assert.DoesNotContain("Execute", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Begin", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Commit", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Rollback", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Apply", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Mutate", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Upsert", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Batch", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PublicMethodsDoNotAcceptOrReturnNativeExecutionTypes()
    {
        var bannedTypeNames = new[]
        {
            "System.Data.Common.DbConnection",
            "System.Data.Common.DbCommand",
            "System.Linq.IQueryable"
        };

        var publicMethods = RelationalAssembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => !method.IsSpecialName)
            .ToArray();

        foreach (var method in publicMethods)
        {
            Assert.DoesNotContain(method.ReturnType.FullName, bannedTypeNames);

            foreach (var parameter in method.GetParameters())
            {
                Assert.DoesNotContain(parameter.ParameterType.FullName, bannedTypeNames);
                Assert.False(
                    parameter.Name?.Contains("sql", StringComparison.OrdinalIgnoreCase) == true &&
                    parameter.ParameterType == typeof(string),
                    $"{method.DeclaringType?.FullName}.{method.Name} accepts a raw SQL-looking string parameter.");
            }
        }
    }

    [Fact]
    public void KernelStoreAndQueryContractsRemainUnchangedByRelationalPackage()
    {
        var recordStoreMethods = typeof(IRecordStore).GetMethods().Select(method => method.Name).ToArray();

        Assert.Equal(
            ["get_Capabilities", "ListAsync", "GetAsync"],
            recordStoreMethods);

        var queryProperties = typeof(RecordQuery).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain("Sql", queryProperties);
        Assert.DoesNotContain("NativeQuery", queryProperties);
        Assert.DoesNotContain("Join", queryProperties);
    }

    [Fact]
    public void DeferredKernelExecutionInterfacesRemainAbsent()
    {
        var loadedTypes = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("HPD.Base", StringComparison.Ordinal) == true)
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.DoesNotContain(loadedTypes, name => name.EndsWith(".INativePolicyRecordStore", StringComparison.Ordinal));
        Assert.DoesNotContain(loadedTypes, name => name.EndsWith(".IRelationalIncludeRecordStore", StringComparison.Ordinal));
        Assert.DoesNotContain(loadedTypes, name => name.EndsWith(".ITransactionalRecordStore", StringComparison.Ordinal));
        Assert.DoesNotContain(loadedTypes, name => name.EndsWith(".ISchemaWriteStore", StringComparison.Ordinal));
        Assert.DoesNotContain(loadedTypes, name => name.EndsWith(".IUpsertRecordStore", StringComparison.Ordinal));
        Assert.DoesNotContain(loadedTypes, name => name.EndsWith(".IBatchRecordStore", StringComparison.Ordinal));
    }
}
