using FluentAssertions;
using HPD.Base.Relational.Descriptors;
using HPD.Base.Relational.Providers;
using HPD.Base.Runtime;
using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.DependencyInjection;
using HPD.Base.Sqlite.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Descriptors;

public sealed class SqliteRelationalDescriptorTests
{
    [Fact]
    public async Task DescriptorListsProviderOwnedUniversalTablesAndMappings()
    {
        var services = new ServiceCollection().AddLogging().AddHPDBaseSqliteStore(options =>
        {
            options.StoreId = "sqlite-test";
            options.CollectionIds = ["items"];
            options.Collections =
            [
                new HPD.Base.Schema.CollectionDefinition
                {
                    Id = "items",
                    Name = "items",
                    Kind = BaseCollectionKinds.Document,
                    SchemaMode = HPD.Base.Schema.SchemaMode.Loose,
                    UnknownFields = HPD.Base.Schema.UnknownFieldPolicy.Preserve,
                    Fields =
                    [
                        new HPD.Base.Schema.FieldDefinition { Id = "title", Name = "title", Type = BaseFieldTypes.String }
                    ]
                }
            ];
        });
        await using var provider = services.BuildServiceProvider();
        var descriptors = provider.GetRequiredService<IRelationalMetadataProvider>();
        var store = await descriptors.GetStoreAsync(new OperationContext { Operation = BaseOperationKind.List, CollectionId = "items" }, VisibilityLevel.Admin);

        store.Value!.Provider.Id.Should().Be("sqlite");
        store.Value.Schemas!.Single().NativeName.Should().Be("main");
        store.Value.Tables!.Select(t => t.NativeName).Should().Contain(["hpd_base_records", "hpd_base_collections", "hpd_base_provider_state"]);
        store.Value.GeneratedColumns.Should().BeEmpty();
        store.Value.CollectionMappings!.Single().PayloadJsonColumnRef.Should().Contain("payload_json");
        store.Value.CollectionMappings!.Single().FieldMappingRefs.Should().NotBeNullOrEmpty();
        store.Value.Extensions.Should().ContainKey("relationalCapabilities");
        store.Value.Extensions.Should().ContainKey("schemaPrefix");
        store.Value.Extensions.Should().ContainKey("relationalFieldMappings");
        var mappings = store.Value.Extensions["relationalFieldMappings"].Deserialize(HPDBaseSqliteJsonSerializerContext.Default.RelationalFieldMappingDescriptorArray);
        mappings.Should().NotBeNull();
        mappings!.Select(mapping => mapping.FieldPath).Should().Contain(["id", "revision", "createdAt", "updatedAt", "*", "title"]);
        mappings.Single(mapping => mapping.FieldPath == "title").JsonPath.Should().Be("$.title");
    }
}
