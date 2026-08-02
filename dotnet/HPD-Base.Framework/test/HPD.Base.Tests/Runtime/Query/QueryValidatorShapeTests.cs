using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Query;

public sealed class QueryValidatorShapeTests
{
    [Fact]
    public async Task CompareRequiresFieldAndValue()
    {
        using var provider = Provider();

        var result = await Validate(provider, new FilterExpression
        {
            Kind = FilterNodeKind.Compare,
            Operator = FilterOperator.Equal
        });

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.query.filter.compare.invalid", result.Error!.Code);
    }

    [Fact]
    public async Task BetweenRequiresExactlyTwoValues()
    {
        using var provider = Provider();

        var result = await Validate(provider, new FilterExpression
        {
            Kind = FilterNodeKind.Between,
            Field = "title",
            Values = [new QueryValue { Kind = QueryValueKind.String, String = "a" }]
        });

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.query.filter.between.invalid", result.Error!.Code);
    }

    [Fact]
    public async Task UnsupportedOperatorFailsValidation()
    {
        using var provider = Provider();

        var result = await Validate(provider, new FilterExpression
        {
            Kind = FilterNodeKind.Compare,
            Field = "title",
            Operator = FilterOperator.Contains,
            Value = new QueryValue { Kind = QueryValueKind.String, String = "a" }
        });

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.query.filter.operator.unsupported", result.Error!.Code);
    }

    [Fact]
    public async Task FilterFailsUnsupportedWhenStoreDoesNotSupportFilters()
    {
        using var provider = Provider();

        var result = await Validate(
            provider,
            new RecordQuery { Filter = Compare("title", "hello") },
            Capability(filterSupported: false));

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal("base.runtime.query.filter.unsupported", result.Error!.Code);
    }

    [Fact]
    public async Task SortFailsUnsupportedWhenStoreDoesNotSupportSort()
    {
        using var provider = Provider();

        var result = await Validate(
            provider,
            new RecordQuery { Sort = [new QuerySort("title")] },
            Capability(sortSupported: false));

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal("base.runtime.query.sort.unsupported", result.Error!.Code);
    }

    [Fact]
    public async Task QueryValueMustMatchTaggedBranch()
    {
        using var provider = Provider();

        var result = await Validate(provider, new FilterExpression
        {
            Kind = FilterNodeKind.Compare,
            Field = "title",
            Operator = FilterOperator.Equal,
            Value = new QueryValue
            {
                Kind = QueryValueKind.String,
                Integer = 1
            }
        });

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.query.value.invalidBranch", result.Error!.Code);
    }

    [Fact]
    public async Task DeclaredSchemaRejectsUnknownFilterField()
    {
        using var provider = Provider();

        var result = await Validate(
            provider,
            new RecordQuery { Filter = Compare("missing", "hello") },
            Capability(),
            CollectionWithFields(Field("title")));

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.query.field.unknown", result.Error!.Code);
    }

    [Fact]
    public async Task NestedFilterPathRequiresCapability()
    {
        using var provider = Provider();

        var result = await Validate(
            provider,
            new RecordQuery { Filter = Compare("title.raw", "hello") },
            Capability(),
            CollectionWithFields(Field("title")));

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.query.field.nestedUnsupported", result.Error!.Code);
    }

    [Fact]
    public async Task StableFieldIdContainingDotIsNotTreatedAsNestedPath()
    {
        using var provider = Provider();

        var result = await Validate(
            provider,
            new RecordQuery { Filter = Compare("membership.subject-ref", "subject") },
            Capability(),
            CollectionWithFields(Field("membership.subject-ref")));

        Assert.True(result.IsSuccess(), result.Error?.Message);
    }

    [Fact]
    public async Task IncludePathMustReferenceDeclaredRelation()
    {
        using var provider = Provider();

        var result = await Validate(
            provider,
            new RecordQuery { Include = [new RecordInclude { NavigationId = "title" }] },
            Capability(),
            CollectionWithFields(Field("title")));

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.include.invalid", result.Error!.Code);
    }

    [Fact]
    public async Task SelectSystemFieldRequiresCapability()
    {
        using var provider = Provider();

        var result = await Validate(
            provider,
            new RecordQuery { Select = ["internalId"] },
            Capability(),
            CollectionWithFields(Field("internalId") with { System = true }));

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal("base.runtime.query.field.systemUnsupported", result.Error!.Code);
    }

    [Fact]
    public async Task QueryExtensionRequiresDeclaredOperatorDescriptor()
    {
        using var provider = Provider();

        var result = await Validate(
            provider,
            new RecordQuery
            {
                Extensions =
                [
                    new QueryExtension
                    {
                        ModuleId = "module.search",
                        Name = "rank",
                        Arguments = [new QueryValue { Kind = QueryValueKind.String, String = "term" }]
                    }
                ]
            },
            Capability());

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.query.extension.unsupported", result.Error!.Code);
    }

    [Fact]
    public async Task QueryExtensionArgumentKindsMustMatchDescriptor()
    {
        using var provider = Provider();

        var result = await Validate(
            provider,
            new RecordQuery
            {
                Extensions =
                [
                    new QueryExtension
                    {
                        ModuleId = "module.search",
                        Name = "rank",
                        Arguments = [new QueryValue { Kind = QueryValueKind.Integer, Integer = 1 }]
                    }
                ]
            },
            Capability(operators:
            [
                new QueryOperatorDescriptor
                {
                    ModuleId = "module.search",
                    Name = "rank",
                    Placement = QueryOperatorPlacement.RecordQueryExtension,
                    ArgumentKinds = [QueryValueKind.String]
                }
            ]));

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.query.extension.argumentKind", result.Error!.Code);
    }

    [Fact]
    public async Task FilterExtensionValidatesFieldTypeAndUsage()
    {
        using var provider = Provider();

        var result = await Validate(
            provider,
            new RecordQuery
            {
                Filter = new FilterExpression
                {
                    Kind = FilterNodeKind.Extension,
                    ModuleId = "module.text",
                    Name = "text",
                    Field = "age",
                    Arguments = [new QueryValue { Kind = QueryValueKind.String, String = "old" }]
                }
            },
            Capability(operators:
            [
                new QueryOperatorDescriptor
                {
                    ModuleId = "module.text",
                    Name = "text",
                    Placement = QueryOperatorPlacement.FilterExpression,
                    FieldRequired = true,
                    AllowedFieldTypes = [BaseFieldTypes.String],
                    ArgumentKinds = [QueryValueKind.String],
                    UsageProfiles = [FilterUsage.ExternalQuery]
                }
            ]),
            CollectionWithFields(Field("age") with { Type = BaseFieldTypes.Integer }));

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal("base.runtime.query.filter.extension.fieldTypeUnsupported", result.Error!.Code);
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        return services.BuildServiceProvider();
    }

    private static async Task<OperationResult<ValidatedRecordQuery>> Validate(ServiceProvider provider, FilterExpression filter) =>
        await Validate(provider, new RecordQuery { Filter = filter }, Capability());

    private static async Task<OperationResult<ValidatedRecordQuery>> Validate(
        ServiceProvider provider,
        RecordQuery query,
        QueryCapability capability,
        CollectionDefinition? collection = null) =>
        await provider.GetRequiredService<IBaseQueryValidator>().ValidateAsync(
            collection ?? CollectionWithFields(),
            query,
            capability,
            BaseQueryValidationUsage.ExternalQuery,
            RuntimeTestData.Operation(BaseOperationKind.List));

    private static QueryCapability Capability(
        bool filterSupported = true,
        bool sortSupported = true,
        QueryOperatorDescriptor[]? operators = null) => new()
    {
        Filter = new FilterCapability
        {
            Supported = filterSupported,
            Operators = [FilterOperator.Equal]
        },
        Sort = new SortCapability { Supported = sortSupported },
        Pagination = new PaginationCapability { Page = true, Offset = true, Cursor = true, MaxLimit = 100 },
        Count = new CountCapability { SupportedModes = [QueryCountMode.None, QueryCountMode.IfAvailable] },
        Select = new SelectCapability { PayloadFields = true },
        Include = new QueryIncludeCapability { Supported = true },
        Operators = operators
    };

    private static FilterExpression Compare(string field, string value) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = field,
        Operator = FilterOperator.Equal,
        Value = new QueryValue
        {
            Kind = QueryValueKind.String,
            String = value
        }
    };

    private static CollectionDefinition CollectionWithFields(params FieldDefinition[] fields) => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        Fields = fields.Length == 0 ? [Field("title")] : fields
    };

    private static FieldDefinition Field(string name) => new()
    {
        Id = name,
        Name = name,
        Type = BaseFieldTypes.String
    };
}
