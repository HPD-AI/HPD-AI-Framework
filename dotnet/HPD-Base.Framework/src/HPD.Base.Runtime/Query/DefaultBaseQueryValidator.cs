using System.Text.Json;
using HPD.Base.Query;
using HPD.Base.Results;
using HPD.Base.Runtime.Configuration;
using HPD.Base.Runtime.Results;
using HPD.Base.Runtime.Serialization;
using HPD.Base.Schema;
using Microsoft.Extensions.Options;

namespace HPD.Base.Runtime.Query;

internal sealed class DefaultBaseQueryValidator : IBaseQueryValidator
{
    private readonly HPDBaseRuntimeLimitOptions _limits;

    public DefaultBaseQueryValidator(IOptions<HPDBaseRuntimeOptions> options)
    {
        _limits = options.Value.Limits;
    }

    public ValueTask<OperationResult<ValidatedRecordQuery>> ValidateAsync(
        CollectionDefinition collection,
        RecordQuery query,
        QueryCapability capability,
        BaseQueryValidationUsage usage,
        OperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = usage;
        _ = operation;
        var fields = CollectionFieldIndex(collection);

        var serializedLength = JsonSerializer.Serialize(query, HPDBaseRuntimeJsonSerializerContext.Default.RecordQuery).Length;
        if (serializedLength > _limits.MaxSerializedQueryLength)
        {
            return ValueTask.FromResult(OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.tooLarge",
                Message = "Serialized query exceeds the runtime limit.",
                Category = ErrorCategory.Validation
            }));
        }

        var sortCount = query.Sort?.Length ?? 0;
        if (sortCount > _limits.MaxSortFields)
        {
            return ValueTask.FromResult(OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.sort.tooMany",
                Message = "Query contains too many sort fields.",
                Category = ErrorCategory.Validation
            }));
        }

        if (sortCount > 0 && !capability.Sort.Supported)
        {
            return ValueTask.FromResult(Unsupported("base.runtime.query.sort.unsupported", "Sort is not supported by the selected store."));
        }

        if (query.Sort?.Any(sort => sort.Nulls != QueryNullOrder.Unspecified) == true && !capability.Sort.NullOrdering)
        {
            return ValueTask.FromResult(Unsupported("base.runtime.query.sort.nullOrderingUnsupported", "Sort null ordering is not supported by the selected store."));
        }

        foreach (var sort in query.Sort ?? [])
        {
            var fieldCheck = ValidateFieldPath(sort.Field, fields, capability.Sort.NestedFieldPaths, allowSystemFields: true);
            if (fieldCheck is not null)
            {
                return ValueTask.FromResult(fieldCheck);
            }
        }

        var selectCount = query.Select?.Length ?? 0;
        if (selectCount > _limits.MaxSelectFields)
        {
            return ValueTask.FromResult(OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.select.tooMany",
                Message = "Query contains too many selected fields.",
                Category = ErrorCategory.Validation
            }));
        }

        if (selectCount > 0 && !capability.Select.PayloadFields)
        {
            return ValueTask.FromResult(Unsupported("base.runtime.query.select.unsupported", "Payload field selection is not supported by the selected store."));
        }

        foreach (var selected in query.Select ?? [])
        {
            var fieldCheck = ValidateFieldPath(selected, fields, capability.Select.NestedFieldPaths, capability.Select.SystemFields);
            if (fieldCheck is not null)
            {
                return ValueTask.FromResult(fieldCheck);
            }
        }

        var includeCount = query.Include?.Length ?? 0;
        if (includeCount > _limits.MaxIncludeCount)
        {
            return ValueTask.FromResult(OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.include.tooMany",
                Message = "Query contains too many include paths.",
                Category = ErrorCategory.Validation
            }));
        }

        if (includeCount > 0 && capability.Include?.Supported != true)
        {
            return ValueTask.FromResult(Unsupported("base.runtime.query.include.unsupported", "Includes are not supported by the selected store."));
        }

        if (query.Include?.Any(include => include.Filter is not null) == true && capability.Include?.IncludeFilters != true)
        {
            return ValueTask.FromResult(Unsupported("base.runtime.query.include.filterUnsupported", "Include filters are not supported by the selected store."));
        }

        if (query.Include?.Any(include => include.Sort is { Length: > 0 }) == true && capability.Include?.IncludeSort != true)
        {
            return ValueTask.FromResult(Unsupported("base.runtime.query.include.sortUnsupported", "Include sort is not supported by the selected store."));
        }

        if (query.Include?.Any(include => include.Limit is not null) == true && capability.Include?.IncludeLimit != true)
        {
            return ValueTask.FromResult(Unsupported("base.runtime.query.include.limitUnsupported", "Include limits are not supported by the selected store."));
        }

        foreach (var include in query.Include ?? [])
        {
            var includeCheck = ValidateInclude(include, fields, capability.Include?.MaxDepth ?? _limits.MaxIncludeDepth);
            if (includeCheck is not null)
            {
                return ValueTask.FromResult(includeCheck);
            }
        }

        if (query.Page?.Limit is { } limit && limit > _limits.MaxPageSize)
        {
            return ValueTask.FromResult(OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.page.tooLarge",
                Message = "Query page size exceeds the runtime limit.",
                Category = ErrorCategory.Validation
            }));
        }

        if (query.Page is not null)
        {
            var pageCheck = ValidatePageCapability(query.Page, capability.Pagination);
            if (pageCheck is not null)
            {
                return ValueTask.FromResult(pageCheck);
            }
        }

        if (query.Count != QueryCountMode.None
            && capability.Count.SupportedModes is { Length: > 0 } countModes
            && !countModes.Contains(query.Count))
        {
            return ValueTask.FromResult(Unsupported("base.runtime.query.count.unsupported", "Requested count mode is not supported by the selected store."));
        }

        if (query.Filter is not null)
        {
            if (!capability.Filter.Supported)
            {
                return ValueTask.FromResult(Unsupported("base.runtime.query.filter.unsupported", "Filters are not supported by the selected store."));
            }

            var counter = FilterCounter.Count(query.Filter, _limits.MaxFilterDepth, _limits.MaxFilterNodes);
            if (!counter.Succeeded)
            {
                return ValueTask.FromResult(OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
                {
                    Code = counter.Code,
                    Message = counter.Message,
                    Category = ErrorCategory.Validation
                }));
            }

            var shape = FilterShapeValidator.Validate(query.Filter, capability, _limits.MaxInValues, fields, usage);
            if (!shape.Succeeded)
            {
                return ValueTask.FromResult(OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
                {
                    Code = shape.Code,
                    Message = shape.Message,
                    Category = ErrorCategory.Validation
                }));
            }
        }

        foreach (var extension in query.Extensions ?? [])
        {
            var extensionResult = ValidateQueryExtension(extension, capability);
            if (!extensionResult.Succeeded)
            {
                return ValueTask.FromResult(OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
                {
                    Code = extensionResult.Code,
                    Message = extensionResult.Message,
                    Category = ErrorCategory.Validation
                }));
            }

            if ((extension.Arguments?.Length ?? 0) > _limits.MaxExtensionArguments)
            {
                return ValueTask.FromResult(OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
                {
                    Code = "base.runtime.query.extension.tooManyArguments",
                    Message = "Query extension contains too many arguments.",
                    Category = ErrorCategory.Validation
                }));
            }

            foreach (var argument in extension.Arguments ?? [])
            {
                var argumentResult = FilterShapeValidator.ValidateValue(argument);
                if (!argumentResult.Succeeded)
                {
                    return ValueTask.FromResult(OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
                    {
                        Code = argumentResult.Code,
                        Message = argumentResult.Message,
                        Category = ErrorCategory.Validation
                    }));
                }
            }
        }

        return ValueTask.FromResult(OperationResults.Ok(new ValidatedRecordQuery(query)));
    }

    private static FilterShapeResult ValidateQueryExtension(
        QueryExtension extension,
        QueryCapability capability)
    {
        if (string.IsNullOrWhiteSpace(extension.ModuleId) || string.IsNullOrWhiteSpace(extension.Name))
        {
            return new FilterShapeResult(false, "base.runtime.query.extension.invalid", "Query extensions require module id and name.");
        }

        var descriptor = capability.Operators?.FirstOrDefault(operatorDescriptor =>
            operatorDescriptor.Placement == QueryOperatorPlacement.RecordQueryExtension
            && string.Equals(operatorDescriptor.ModuleId, extension.ModuleId, StringComparison.Ordinal)
            && string.Equals(operatorDescriptor.Name, extension.Name, StringComparison.Ordinal));
        if (descriptor is null)
        {
            return new FilterShapeResult(false, "base.runtime.query.extension.unsupported", "Query extension is not supported by the selected store.");
        }

        return ValidateExtensionArguments(extension.Arguments, descriptor);
    }

    private static OperationResult<ValidatedRecordQuery>? ValidatePageCapability(QueryPage page, PaginationCapability capability)
    {
        if (page.Mode == QueryPaginationMode.Page && !capability.Page)
        {
            return Unsupported("base.runtime.query.page.modeUnsupported", "Page pagination is not supported by the selected store.");
        }

        if (page.Mode == QueryPaginationMode.Offset && !capability.Offset)
        {
            return Unsupported("base.runtime.query.page.modeUnsupported", "Offset pagination is not supported by the selected store.");
        }

        if (page.Mode == QueryPaginationMode.Cursor && !capability.Cursor)
        {
            return Unsupported("base.runtime.query.page.modeUnsupported", "Cursor pagination is not supported by the selected store.");
        }

        if (page.Limit is { } limit && capability.MaxLimit > 0 && limit > capability.MaxLimit)
        {
            return OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.page.storeLimitExceeded",
                Message = "Query page size exceeds the selected store limit.",
                Category = ErrorCategory.Validation
            });
        }

        return null;
    }

    private static OperationResult<ValidatedRecordQuery> Unsupported(string code, string message) =>
        OperationResults.Unsupported<ValidatedRecordQuery>(new BaseError
        {
            Code = code,
            Message = message,
            Category = ErrorCategory.Unsupported
        });

    private static FieldIndex CollectionFieldIndex(CollectionDefinition collection)
    {
        var fields = collection.Fields ?? [];
        return new FieldIndex(
            fields.Length == 0,
            fields.SelectMany(field => new[] { field.Id, field.Name })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal),
            fields.Where(field => field.System)
                .SelectMany(field => new[] { field.Id, field.Name })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal),
            fields.Where(field => field.Relation is not null)
                .SelectMany(field => new[] { field.Id, field.Name })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal),
            fields.SelectMany(field => new[] { (field.Id, field.Type), (field.Name, field.Type) })
                .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
                .GroupBy(item => item.Item1, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Type, StringComparer.Ordinal));
    }

    private static OperationResult<ValidatedRecordQuery>? ValidateFieldPath(
        string? fieldPath,
        FieldIndex fields,
        bool nestedSupported,
        bool allowSystemFields)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.field.required",
                Message = "Query field path must be non-empty.",
                Category = ErrorCategory.Validation
            });
        }

        var topLevel = TopLevelFieldPath(fieldPath);
        if (!fields.SchemaOpen && !fields.All.Contains(topLevel))
        {
            return OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.field.unknown",
                Message = "Query references a field that is not declared by the collection schema.",
                Category = ErrorCategory.Validation,
                Target = fieldPath
            });
        }

        if (!allowSystemFields && fields.System.Contains(topLevel))
        {
            return OperationResults.Unsupported<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.field.systemUnsupported",
                Message = "System field selection is not supported by the selected store.",
                Category = ErrorCategory.Unsupported,
                Target = fieldPath
            });
        }

        if (fieldPath.Contains('.', StringComparison.Ordinal) && !nestedSupported)
        {
            return Unsupported("base.runtime.query.field.nestedUnsupported", "Nested field paths are not supported by the selected store.");
        }

        return null;
    }

    private static OperationResult<ValidatedRecordQuery>? ValidateInclude(
        QueryInclude include,
        FieldIndex fields,
        int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(include.Path))
        {
            return OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.include.pathRequired",
                Message = "Include path must be non-empty.",
                Category = ErrorCategory.Validation
            });
        }

        var depth = include.Path.Split('.', StringSplitOptions.RemoveEmptyEntries).Length;
        if (maxDepth > 0 && depth > maxDepth)
        {
            return OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.include.tooDeep",
                Message = "Include path exceeds the maximum depth.",
                Category = ErrorCategory.Validation,
                Target = include.Path
            });
        }

        var topLevel = TopLevelFieldPath(include.Path);
        if (!fields.SchemaOpen && !fields.Relations.Contains(topLevel))
        {
            return OperationResults.ValidationFailed<ValidatedRecordQuery>(new BaseError
            {
                Code = "base.runtime.query.include.notRelation",
                Message = "Include path must reference a declared relation field.",
                Category = ErrorCategory.Validation,
                Target = include.Path
            });
        }

        return null;
    }

    private static string TopLevelFieldPath(string fieldPath)
    {
        var dotIndex = fieldPath.IndexOf('.');
        return dotIndex < 0 ? fieldPath : fieldPath[..dotIndex];
    }

    private static FilterShapeResult ValidateExtensionArguments(
        QueryValue[]? arguments,
        QueryOperatorDescriptor descriptor)
    {
        if (descriptor.ArgumentKinds is { } expectedKinds)
        {
            var actual = arguments ?? [];
            if (actual.Length != expectedKinds.Length)
            {
                return new FilterShapeResult(false, "base.runtime.query.extension.argumentCount", "Query extension argument count does not match the declared operator descriptor.");
            }

            for (var index = 0; index < actual.Length; index++)
            {
                if (actual[index].Kind != expectedKinds[index])
                {
                    return new FilterShapeResult(false, "base.runtime.query.extension.argumentKind", "Query extension argument kind does not match the declared operator descriptor.");
                }
            }
        }

        foreach (var argument in arguments ?? [])
        {
            var value = FilterShapeValidator.ValidateValue(argument);
            if (!value.Succeeded)
            {
                return value;
            }
        }

        return new FilterShapeResult(true, string.Empty, string.Empty);
    }

    private static FilterUsage UsageProfile(BaseQueryValidationUsage usage) =>
        usage switch
        {
            BaseQueryValidationUsage.ExternalQuery => FilterUsage.ExternalQuery,
            BaseQueryValidationUsage.PolicyConstraint => FilterUsage.PolicyConstraint,
            BaseQueryValidationUsage.PolicyWriteCheck => FilterUsage.PolicyWriteCheck,
            BaseQueryValidationUsage.IncludeFilter => FilterUsage.IncludeFilter,
            _ => FilterUsage.ExternalQuery
        };

    private readonly record struct FilterCounterResult(bool Succeeded, string Code, string Message);
    private readonly record struct FilterShapeResult(bool Succeeded, string Code, string Message);
    private readonly record struct FieldIndex(
        bool SchemaOpen,
        HashSet<string> All,
        HashSet<string> System,
        HashSet<string> Relations,
        Dictionary<string, string> Types);

    private static class FilterCounter
    {
        public static FilterCounterResult Count(FilterExpression filter, int maxDepth, int maxNodes)
        {
            var nodes = 0;
            return Visit(filter, depth: 1, maxDepth, maxNodes, ref nodes);
        }

        private static FilterCounterResult Visit(
            FilterExpression filter,
            int depth,
            int maxDepth,
            int maxNodes,
            ref int nodes)
        {
            if (depth > maxDepth)
            {
                return new FilterCounterResult(false, "base.runtime.query.filter.tooDeep", "Query filter exceeds the maximum depth.");
            }

            nodes++;
            if (nodes > maxNodes)
            {
                return new FilterCounterResult(false, "base.runtime.query.filter.tooManyNodes", "Query filter exceeds the maximum node count.");
            }

            foreach (var child in filter.Children ?? [])
            {
                var result = Visit(child, depth + 1, maxDepth, maxNodes, ref nodes);
                if (!result.Succeeded)
                {
                    return result;
                }
            }

            return new FilterCounterResult(true, string.Empty, string.Empty);
        }
    }

    private static class FilterShapeValidator
    {
        public static FilterShapeResult Validate(
            FilterExpression filter,
            QueryCapability capability,
            int maxInValues,
            FieldIndex fields,
            BaseQueryValidationUsage usage)
        {
            var result = ValidateNode(filter, capability, maxInValues, fields, usage);
            return result.Succeeded ? new FilterShapeResult(true, string.Empty, string.Empty) : result;
        }

        private static FilterShapeResult ValidateNode(
            FilterExpression filter,
            QueryCapability capability,
            int maxInValues,
            FieldIndex fields,
            BaseQueryValidationUsage usage)
        {
            var result = filter.Kind switch
            {
                FilterNodeKind.True or FilterNodeKind.False => Success(),
                FilterNodeKind.Compare => ValidateCompare(filter, capability, fields),
                FilterNodeKind.In => ValidateIn(filter, maxInValues, capability, fields),
                FilterNodeKind.Between => ValidateBetween(filter, capability, fields),
                FilterNodeKind.IsNull or FilterNodeKind.IsDefined => string.IsNullOrWhiteSpace(filter.Field)
                    ? Fail("base.runtime.query.filter.fieldRequired", "Filter node requires a field.")
                    : ValidateFilterField(filter.Field, capability, fields),
                FilterNodeKind.Not => filter.Children is not { Length: 1 }
                    ? Fail("base.runtime.query.filter.not.invalidChildren", "Not filter requires exactly one child.")
                    : Success(),
                FilterNodeKind.And or FilterNodeKind.Or => filter.Children is not { Length: > 0 }
                    ? Fail("base.runtime.query.filter.boolean.invalidChildren", "Boolean filter requires at least one child.")
                    : Success(),
                FilterNodeKind.Extension => ValidateFilterExtension(filter, capability, fields, usage),
                _ => Fail("base.runtime.query.filter.invalidKind", "Filter node kind is invalid.")
            };

            if (!result.Succeeded)
            {
                return result;
            }

            foreach (var child in filter.Children ?? [])
            {
                var childResult = ValidateNode(child, capability, maxInValues, fields, usage);
                if (!childResult.Succeeded)
                {
                    return childResult;
                }
            }

            return Success();
        }

        private static FilterShapeResult ValidateCompare(FilterExpression filter, QueryCapability capability, FieldIndex fields)
        {
            if (string.IsNullOrWhiteSpace(filter.Field) || filter.Value is null)
            {
                return Fail("base.runtime.query.filter.compare.invalid", "Compare filter requires a field and value.");
            }

            var field = ValidateFilterField(filter.Field, capability, fields);
            if (!field.Succeeded)
            {
                return field;
            }

            var value = ValidateValue(filter.Value);
            if (!value.Succeeded)
            {
                return value;
            }

            if (capability.Filter.Operators is { Length: > 0 } operators
                && !operators.Contains(filter.Operator))
            {
                return Fail("base.runtime.query.filter.operator.unsupported", "Filter operator is not supported.");
            }

            return Success();
        }

        private static FilterShapeResult ValidateIn(FilterExpression filter, int maxInValues, QueryCapability capability, FieldIndex fields)
        {
            if (string.IsNullOrWhiteSpace(filter.Field) || filter.Values is not { Length: > 0 })
            {
                return Fail("base.runtime.query.filter.in.invalid", "In filter requires a field and one or more values.");
            }

            var field = ValidateFilterField(filter.Field, capability, fields);
            if (!field.Succeeded)
            {
                return field;
            }

            if (filter.Values.Length > maxInValues)
            {
                return Fail("base.runtime.query.filter.in.tooMany", "In filter contains too many values.");
            }

            foreach (var value in filter.Values)
            {
                var valueResult = ValidateValue(value);
                if (!valueResult.Succeeded)
                {
                    return valueResult;
                }
            }

            return Success();
        }

        private static FilterShapeResult ValidateBetween(FilterExpression filter, QueryCapability capability, FieldIndex fields)
        {
            if (string.IsNullOrWhiteSpace(filter.Field) || filter.Values is not { Length: 2 })
            {
                return Fail("base.runtime.query.filter.between.invalid", "Between filter requires a field and exactly two values.");
            }

            var field = ValidateFilterField(filter.Field, capability, fields);
            if (!field.Succeeded)
            {
                return field;
            }

            foreach (var value in filter.Values)
            {
                var valueResult = ValidateValue(value);
                if (!valueResult.Succeeded)
                {
                    return valueResult;
                }
            }

            return Success();
        }

        public static FilterShapeResult ValidateValue(QueryValue value)
        {
            var activeBranches = 0;
            activeBranches += value.String is null ? 0 : 1;
            activeBranches += value.Boolean is null ? 0 : 1;
            activeBranches += value.Integer is null ? 0 : 1;
            activeBranches += value.Number is null ? 0 : 1;
            activeBranches += value.Decimal is null ? 0 : 1;
            activeBranches += value.DateTime is null ? 0 : 1;
            activeBranches += value.Id is null ? 0 : 1;
            activeBranches += value.Array is null ? 0 : 1;

            var valid = value.Kind switch
            {
                QueryValueKind.Null => activeBranches == 0,
                QueryValueKind.String => activeBranches == 1 && value.String is not null,
                QueryValueKind.Boolean => activeBranches == 1 && value.Boolean is not null,
                QueryValueKind.Integer => activeBranches == 1 && value.Integer is not null,
                QueryValueKind.Number => activeBranches == 1 && value.Number is not null,
                QueryValueKind.Decimal => activeBranches == 1 && value.Decimal is not null,
                QueryValueKind.DateTime => activeBranches == 1 && value.DateTime is not null,
                QueryValueKind.Id => activeBranches == 1 && value.Id is not null,
                QueryValueKind.Array => activeBranches == 1 && value.Array is not null,
                _ => false
            };

            if (!valid)
            {
                return Fail("base.runtime.query.value.invalidBranch", "Query value must have exactly one active branch matching its kind.");
            }

            foreach (var child in value.Array ?? [])
            {
                var childResult = ValidateValue(child);
                if (!childResult.Succeeded)
                {
                    return childResult;
                }
            }

            return Success();
        }

        private static FilterShapeResult Success() => new(true, string.Empty, string.Empty);
        private static FilterShapeResult Fail(string code, string message) => new(false, code, message);

        private static FilterShapeResult ValidateFilterField(
            string fieldPath,
            QueryCapability capability,
            FieldIndex fields)
        {
            var topLevel = TopLevelFieldPath(fieldPath);
            if (!fields.SchemaOpen && !fields.All.Contains(topLevel))
            {
                return Fail("base.runtime.query.field.unknown", "Query references a field that is not declared by the collection schema.");
            }

            if (fieldPath.Contains('.', StringComparison.Ordinal) && !capability.Filter.NestedFieldPaths)
            {
                return Fail("base.runtime.query.field.nestedUnsupported", "Nested field paths are not supported by the selected store.");
            }

            return Success();
        }

        private static FilterShapeResult ValidateFilterExtension(
            FilterExpression filter,
            QueryCapability capability,
            FieldIndex fields,
            BaseQueryValidationUsage usage)
        {
            if (string.IsNullOrWhiteSpace(filter.ModuleId) || string.IsNullOrWhiteSpace(filter.Name))
            {
                return Fail("base.runtime.query.filter.extension.invalid", "Extension filters require module id and name.");
            }

            var descriptor = capability.Operators?.FirstOrDefault(operatorDescriptor =>
                operatorDescriptor.Placement == QueryOperatorPlacement.FilterExpression
                && string.Equals(operatorDescriptor.ModuleId, filter.ModuleId, StringComparison.Ordinal)
                && string.Equals(operatorDescriptor.Name, filter.Name, StringComparison.Ordinal));
            if (descriptor is null)
            {
                return Fail("base.runtime.query.filter.extension.unsupported", "Extension filter is not supported by the selected store.");
            }

            if (descriptor.UsageProfiles is { Length: > 0 } usageProfiles
                && !usageProfiles.Contains(UsageProfile(usage)))
            {
                return Fail("base.runtime.query.filter.extension.usageUnsupported", "Extension filter is not supported for this validation usage.");
            }

            if (descriptor.FieldRequired)
            {
                if (string.IsNullOrWhiteSpace(filter.Field))
                {
                    return Fail("base.runtime.query.filter.extension.fieldRequired", "Extension filter requires a field.");
                }

                var field = ValidateFilterField(filter.Field, capability, fields);
                if (!field.Succeeded)
                {
                    return field;
                }

                if (descriptor.AllowedFieldTypes is { Length: > 0 })
                {
                    var topLevel = TopLevelFieldPath(filter.Field);
                    var fieldType = fields.Types.TryGetValue(topLevel, out var type) ? type : null;
                    if (fieldType is not null && !descriptor.AllowedFieldTypes.Contains(fieldType, StringComparer.Ordinal))
                    {
                        return Fail("base.runtime.query.filter.extension.fieldTypeUnsupported", "Extension filter does not support the referenced field type.");
                    }
                }
            }

            return ValidateExtensionArguments(filter.Arguments, descriptor);
        }
    }
}
