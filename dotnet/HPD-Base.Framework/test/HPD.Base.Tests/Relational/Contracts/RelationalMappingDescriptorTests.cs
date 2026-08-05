using HPD.Base;

namespace HPD.Base.Tests.Relational.Contracts;

public sealed class RelationalMappingDescriptorTests
{
    [Fact]
    public void SupportsTableViewJsonHybridKeylessAndCompositeIdentityShapes()
    {
        var table = Mapping("table", RelationalMappingKind.Table, RelationalPayloadMappingKind.Columns, RelationalRecordIdMappingKind.NativePrimaryKey)
            with { TableRef = "table-orders", RecordIdColumnRefs = ["col-id"] };

        var view = Mapping("view", RelationalMappingKind.View, RelationalPayloadMappingKind.NativeProjection, RelationalRecordIdMappingKind.KeylessUnavailable)
            with { ViewRef = "view-open-orders", ListSupported = true, GetSupported = false };

        var jsonColumn = Mapping("json", RelationalMappingKind.Table, RelationalPayloadMappingKind.JsonColumn, RelationalRecordIdMappingKind.Synthetic)
            with { TableRef = "table-events", PayloadJsonColumnRef = "col-payload" };

        var hybrid = Mapping("hybrid", RelationalMappingKind.Hybrid, RelationalPayloadMappingKind.Hybrid, RelationalRecordIdMappingKind.CompositeKey)
            with { TableRef = "table-lines", RecordIdColumnRefs = ["col-order-id", "col-line-no"] };

        Assert.Equal(RelationalMappingKind.Table, table.MappingKind);
        Assert.Equal(RelationalRecordIdMappingKind.KeylessUnavailable, view.RecordIdMappingKind);
        Assert.Equal(RelationalPayloadMappingKind.JsonColumn, jsonColumn.PayloadMappingKind);
        Assert.Equal(["col-order-id", "col-line-no"], hybrid.RecordIdColumnRefs);
    }

    [Fact]
    public void FieldMappingsCanTargetColumnsOrJsonPathsWithoutExecutableNativeText()
    {
        var direct = new RelationalFieldMappingDescriptor
        {
            Id = "field-name",
            StoreId = "store",
            CollectionId = "profiles",
            FieldPath = "name",
            ColumnRef = "col-name",
            NativeType = new RelationalColumnTypeDescriptor { NativeTypeName = "text", Family = RelationalColumnTypeFamily.Text }
        };

        var json = direct with
        {
            Id = "field-settings-theme",
            FieldPath = "settings.theme",
            ColumnRef = null,
            JsonColumnRef = "json-settings",
            JsonPath = "$.theme",
            ConversionKind = RelationalFieldConversionKind.JsonSerialization
        };

        Assert.Equal("col-name", direct.ColumnRef);
        Assert.Equal("$.theme", json.JsonPath);
        Assert.Equal(RelationalFieldConversionKind.JsonSerialization, json.ConversionKind);
    }

    private static RelationalCollectionMappingDescriptor Mapping(
        string id,
        RelationalMappingKind mappingKind,
        RelationalPayloadMappingKind payloadKind,
        RelationalRecordIdMappingKind idKind) =>
        new()
        {
            Id = id,
            StoreId = "store",
            CollectionId = id,
            MappingKind = mappingKind,
            PayloadMappingKind = payloadKind,
            RecordIdMappingKind = idKind
        };
}
