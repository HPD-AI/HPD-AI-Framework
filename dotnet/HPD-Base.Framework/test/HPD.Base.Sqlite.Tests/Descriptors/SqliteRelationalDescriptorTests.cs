using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Descriptors;

public sealed class SqliteRelationalDescriptorTests
{
    [Fact]
    public async Task DescriptorListsTypedPerCollectionTablesAndMappings()
    {
        var services = new ServiceCollection().AddLogging().AddHPDBaseSqliteStore(options =>
        {
            options.StoreId = "sqlite-test";
            options.Collections =
            [
                new HPD.Base.CollectionDefinition
                {
                    Id = "items",
                    Name = "items",
                    Kind = BaseCollectionKinds.Document,
                    SchemaMode = HPD.Base.SchemaMode.Loose,
                    UnknownFields = HPD.Base.UnknownFieldPolicy.Preserve,
                    Fields =
                    [
                        new HPD.Base.FieldDefinition { Id = "title", ApplicationName = "title", WireName = "title", Type = BaseFieldTypes.String }
                    ]
                }
            ];
        });
        await using var provider = services.BuildServiceProvider();
        var descriptors = provider.GetRequiredService<IRelationalMetadataProvider>();
        var store = await descriptors.GetStoreAsync(new OperationContext { Operation = BaseOperationKind.List, CollectionId = "items" }, VisibilityLevel.Admin);

        store.Value!.Provider.Id.Should().Be("sqlite");
        store.Value.Schemas!.Single().NativeName.Should().Be("main");
        store.Value.Tables!.Select(t => t.NativeName).Should().Contain([PhysicalTable("items"), "hpd_base_collections", "hpd_base_provider_state", "hpd_base_mutation_journal"]);
        store.Value.Tables!.Select(t => t.NativeName).Should().NotContain("hpd_base_records");
        store.Value.GeneratedColumns.Should().BeEmpty();
        store.Value.CollectionMappings!.Single().PayloadMappingKind.Should().Be(RelationalPayloadMappingKind.Hybrid);
        store.Value.CollectionMappings!.Single().PayloadJsonColumnRef.Should().Contain("extension_json");
        store.Value.CollectionMappings!.Single().FieldMappingRefs.Should().NotBeNullOrEmpty();
        store.Value.Extensions.Should().ContainKey("relationalCapabilities");
        store.Value.Extensions.Should().ContainKey("schemaPrefix");
        store.Value.Extensions.Should().ContainKey("relationalFieldMappings");
        var mappings = store.Value.Extensions["relationalFieldMappings"].Deserialize(HPDBaseSqliteJsonSerializerContext.Default.RelationalFieldMappingDescriptorArray);
        mappings.Should().NotBeNull();
        mappings!.Select(mapping => mapping.FieldPath).Should().Contain(["id", "revision", "createdAt", "updatedAt", "title"]);
        mappings.Select(mapping => mapping.FieldPath).Should().NotContain("*");
        mappings.Single(mapping => mapping.FieldPath == "title").ColumnRef.Should().Contain(".f_");
        mappings.Single(mapping => mapping.FieldPath == "title").JsonPath.Should().BeNull();
        var capabilities = store.Value.Extensions["relationalCapabilities"].Deserialize(HPDBaseSqliteJsonSerializerContext.Default.RelationalCapabilityDescriptor)!;
        capabilities.Mapping.RelationMappings.Should().BeTrue();
        capabilities.JoinsIncludes.Should().Match<RelationalJoinIncludeCapability>(value => value.Status == CapabilityStatus.Available && value.CallableIncludeExecutionAvailable);
        capabilities.Transactions.Should().Match<RelationalTransactionCapability>(value => value.Status == CapabilityStatus.Available && value.CallableInterfaceAvailable);
        capabilities.SchemaWrite.Should().Match<RelationalSchemaWriteCapability>(value => value.Status == CapabilityStatus.Available && value.CallableInterfaceAvailable && value.DefinitionChangeRunnerAvailable);
    }

    private static string PhysicalTable(string collectionId) => "b_c_" + Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(collectionId)))[..32];
}
