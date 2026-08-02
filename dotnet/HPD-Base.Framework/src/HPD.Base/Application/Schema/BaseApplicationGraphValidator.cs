namespace HPD.Base;

internal static class BaseApplicationGraphValidator
{
    internal static void Validate(
        CollectionDefinition[] collections,
        IEnumerable<IBaseReadRegistration> readRegistrations,
        HPDBaseRelationalOptions relational,
        HPDBaseSchemaOptions schema)
    {
        IBaseReadRegistration[] reads = readRegistrations.OrderBy(static read => read.Id, StringComparer.Ordinal).ToArray();
        if (collections.Length > schema.MaxCollections || reads.Length > schema.MaxReadDefinitions)
            throw Invalid("The BASE application graph exceeds its configured schema limits.");

        var collectionById = Unique(collections, static collection => collection.Id, "collection");
        var globalFieldIds = new HashSet<string>(StringComparer.Ordinal);
        var globalRelationIds = new HashSet<string>(StringComparer.Ordinal);
        var globalIndexIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (CollectionDefinition collection in collections)
        {
            BaseApplicationId.Validate(collection.Id, nameof(collection.Id));
            FieldDefinition[] fields = collection.Fields ?? [];
            if (fields.Length > schema.MaxFieldsPerCollection)
                throw Invalid($"Collection '{collection.Id}' exceeds the configured field limit.");
            var fieldById = Unique(fields, static field => field.Id, "field");
            Unique(fields, static field => field.Name, "stored field name");
            foreach (FieldDefinition field in fields)
            {
                BaseApplicationId.Validate(field.Id, nameof(field.Id));
                if (!globalFieldIds.Add(field.Id))
                    throw Invalid($"Stable field identifier '{field.Id}' is duplicated across the application graph.");
                if (field.Relation is { } relation)
                    ValidateRelation(collection, field, relation, collectionById, globalRelationIds);
            }

            foreach (IndexDefinition index in collection.Indexes ?? [])
            {
                if (!globalIndexIds.Add(index.Id))
                    throw Invalid($"Stable index identifier '{index.Id}' is duplicated across the application graph.");
                if (!string.Equals(index.CollectionId, collection.Id, StringComparison.Ordinal) ||
                    index.Parts is null || index.Parts.Length == 0 ||
                    index.Parts.Any(component => component.Kind != IndexPartKind.Field ||
                        component.FieldId is null || !fieldById.ContainsKey(component.FieldId)))
                    throw Invalid($"Index '{index.Id}' has an invalid collection or field reference.");
            }
        }

        if (globalRelationIds.Count > schema.MaxRelations || globalIndexIds.Count > schema.MaxIndexes)
            throw Invalid("The BASE application graph exceeds its configured relation or index limit.");
        foreach (IBaseReadRegistration read in reads)
            ValidateRead(read, collectionById, relational);
    }

    private static void ValidateRelation(
        CollectionDefinition source,
        FieldDefinition field,
        RelationDefinition relation,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        HashSet<string> relationIds)
    {
        if (!relationIds.Add(relation.Id) ||
            !string.Equals(relation.SourceCollectionId, source.Id, StringComparison.Ordinal) ||
            !string.Equals(relation.SourceFieldId, field.Id, StringComparison.Ordinal) ||
            !collections.ContainsKey(relation.TargetCollectionId) ||
            !string.Equals(relation.TargetFieldId, "base.recordId", StringComparison.Ordinal) ||
            relation.OwningSide != BaseRelationOwningSide.Source ||
            relation.DeleteBehavior != BaseRelationDeleteBehavior.Restrict ||
            relation.ExistenceEnforcement != EnforcementOwner.Runtime)
            throw Invalid($"Relation '{relation.Id}' is not a valid executable L35 relation.");
        if (relation.Required != field.Required || (relation.Required && field.Nullable) ||
            relation.LocalMultiplicity == BaseRelationMultiplicity.ExactlyOne && !relation.Required ||
            relation.LocalMultiplicity == BaseRelationMultiplicity.ZeroOrOne && relation.Required ||
            relation.LocalMultiplicity != BaseRelationMultiplicity.Many && (relation.MinimumCount is not null || relation.MaximumCount is not null) ||
            relation.MinimumCount is < 0 || relation.MaximumCount is < 0 ||
            relation.MinimumCount > relation.MaximumCount || relation.MaximumCount > 10_000 ||
            relation.Include is { MaxDepth: < 1 or > 32 } ||
            relation.Include is { Allowed: false, FilterAllowed: true } or { Allowed: false, SortAllowed: true })
            throw Invalid($"Relation '{relation.Id}' has inconsistent required, nullable, or multiplicity semantics.");
    }

    private static void ValidateRead(
        IBaseReadRegistration registration,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        HPDBaseRelationalOptions options)
    {
        BaseRelationalReadPlan plan = registration.Plan;
        if (!string.Equals(registration.Id, plan.Id, StringComparison.Ordinal) ||
            plan.Sources.Length == 0 || plan.Sources.Length > options.MaxSources ||
            plan.Joins.Length != plan.Sources.Length - 1 || plan.Joins.Length > options.MaxJoins || plan.Parameters.Length > options.MaxParameters ||
            plan.GroupKeys.Length > options.MaxGroupKeys || plan.Aggregates.Length > options.MaxAggregates ||
            plan.Projection.Length == 0 || plan.Projection.Length > options.MaxProjectionFields ||
            plan.Sort.Length > options.MaxSortFields ||
            plan.Sources.Any(source => !collections.ContainsKey(source.CollectionId)) ||
            plan.Consistency != BaseReadConsistency.Snapshot || plan.DependencyMode != BaseReadDependencyMode.Complete ||
            plan.Budgets.MaxResultRows < 1 || plan.Budgets.MaxResultRows > options.MaxResultRows ||
            plan.Budgets.MaxResultBytes < 1 || plan.Budgets.MaxResultBytes > options.MaxResultBytes || plan.Budgets.MaxOperations < 1)
            throw Invalid($"Read '{registration.Id}' has an invalid or over-limit topology.");
        Dictionary<string, BaseRelationalReadSource> sources = Unique(plan.Sources, static source => source.Id, "read source");
        Unique(plan.Projection, static projection => projection.FieldId, "read projection");
        Dictionary<string, BaseRelationalReadAggregate> aggregates = Unique(plan.Aggregates, static aggregate => aggregate.Id, "read aggregate");
        Dictionary<string, BaseRelationalReadParameter> parameters = Unique(plan.Parameters, static parameter => parameter.Id, "read parameter");
        if (parameters.Values.Any(parameter => !ValidParameter(parameter, options)))
            throw Invalid($"Read '{registration.Id}' contains an invalid parameter definition.");
        int nodes = Count(plan.Predicate) + Count(plan.Having);
        if (nodes > options.MaxPredicateNodes || Depth(plan.Predicate) > options.MaxPredicateDepth || Depth(plan.Having) > options.MaxPredicateDepth)
            throw Invalid($"Read '{registration.Id}' exceeds predicate limits.");
        for (int index = 0; index < plan.Joins.Length; index++)
        {
            BaseRelationalReadJoin join = plan.Joins[index];
            string introduced = plan.Sources[index + 1].Id;
            ValidateOperand(join.Left, sources, collections, parameters, aggregates, allowAggregate: false);
            ValidateOperand(join.Right, sources, collections, parameters, aggregates, allowAggregate: false);
            if (!ReferencesSource(join.Left, introduced) && !ReferencesSource(join.Right, introduced))
                throw Invalid($"Read '{registration.Id}' has a join that does not reference its introduced source.");
            if (!Compatible(OperandKind(join.Left, sources, collections, parameters, aggregates),
                    OperandKind(join.Right, sources, collections, parameters, aggregates)))
                throw Invalid($"Read '{registration.Id}' has incompatible join operands.");
        }
        ValidatePredicate(plan.Predicate, sources, collections, parameters, aggregates, allowAggregate: false);
        foreach (BaseRelationalOperand key in plan.GroupKeys) ValidateOperand(key, sources, collections, parameters, aggregates, allowAggregate: false);
        foreach (BaseRelationalReadAggregate aggregate in plan.Aggregates)
        {
            if (aggregate.Operand is null && aggregate.Kind != BaseAggregateKind.Count)
                throw Invalid($"Read '{registration.Id}' has an aggregate without a required operand.");
            if (aggregate.Operand is not null) ValidateOperand(aggregate.Operand, sources, collections, parameters, aggregates, allowAggregate: false);
            QueryValueKind? input = aggregate.Operand is null ? null : OperandKind(aggregate.Operand, sources, collections, parameters, aggregates);
            if (aggregate.Kind is BaseAggregateKind.Sum or BaseAggregateKind.Average && !Numeric(input) ||
                aggregate.Kind is BaseAggregateKind.Any or BaseAggregateKind.All && input != QueryValueKind.Boolean ||
                aggregate.Kind is BaseAggregateKind.Minimum or BaseAggregateKind.Maximum && !Ordered(input))
                throw Invalid($"Read '{registration.Id}' has an invalid aggregate/type combination.");
        }
        ValidatePredicate(plan.Having, sources, collections, parameters, aggregates, allowAggregate: true);
        HashSet<BaseRelationalOperand> groupKeys = plan.GroupKeys.ToHashSet();
        if (PredicateOperands(plan.Having).Any(operand =>
                operand.Kind is BaseRelationalOperandKind.SourceField or BaseRelationalOperandKind.RecordId &&
                !groupKeys.Contains(operand)))
            throw Invalid($"Read '{registration.Id}' has a having operand that is not a group key or aggregate.");
        foreach (BaseRelationalReadProjection projection in plan.Projection) ValidateOperand(projection.Operand, sources, collections, parameters, aggregates, allowAggregate: true);
        foreach (BaseRelationalReadSort sort in plan.Sort) ValidateOperand(sort.Operand, sources, collections, parameters, aggregates, allowAggregate: true);
    }

    private static void ValidatePredicate(
        BaseRelationalPredicate? predicate,
        IReadOnlyDictionary<string, BaseRelationalReadSource> sources,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        IReadOnlyDictionary<string, BaseRelationalReadParameter> parameters,
        IReadOnlyDictionary<string, BaseRelationalReadAggregate> aggregates,
        bool allowAggregate)
    {
        if (predicate is null) return;
        if (predicate.Left is not null) ValidateOperand(predicate.Left, sources, collections, parameters, aggregates, allowAggregate);
        if (predicate.Right is not null) ValidateOperand(predicate.Right, sources, collections, parameters, aggregates, allowAggregate);
        foreach (BaseRelationalPredicate child in predicate.Children ?? []) ValidatePredicate(child, sources, collections, parameters, aggregates, allowAggregate);
        bool validShape = predicate.Kind switch
        {
            FilterNodeKind.True or FilterNodeKind.False => predicate.Left is null && predicate.Right is null && predicate.Children is null or { Length: 0 },
            FilterNodeKind.Not => predicate.Left is null && predicate.Right is null && predicate.Children is { Length: 1 },
            FilterNodeKind.And or FilterNodeKind.Or => predicate.Left is null && predicate.Right is null && predicate.Children is { Length: > 0 },
            FilterNodeKind.IsNull or FilterNodeKind.IsDefined => predicate.Left is not null && predicate.Right is null && predicate.Children is null or { Length: 0 },
            FilterNodeKind.Compare or FilterNodeKind.In or FilterNodeKind.Between => predicate.Left is not null && predicate.Right is not null && predicate.Children is null or { Length: 0 },
            _ => false,
        };
        if (!validShape) throw Invalid("A registered read contains an invalid predicate shape.");
        QueryValueKind? left = predicate.Left is null ? null : OperandKind(predicate.Left, sources, collections, parameters, aggregates);
        QueryValueKind? right = predicate.Right is null ? null : OperandKind(predicate.Right, sources, collections, parameters, aggregates);
        if (predicate.Kind == FilterNodeKind.Compare &&
                (!Compatible(left, right) || predicate.Operator is not (FilterOperator.Equal or FilterOperator.NotEqual) && (!Ordered(left) || !Ordered(right))) ||
            predicate.Kind is FilterNodeKind.In or FilterNodeKind.Between && right is not (null or QueryValueKind.Array) ||
            predicate.Kind == FilterNodeKind.Between && !Ordered(left))
            throw Invalid("A registered read contains an unsupported predicate/type combination.");
    }

    private static void ValidateOperand(
        BaseRelationalOperand operand,
        IReadOnlyDictionary<string, BaseRelationalReadSource> sources,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        IReadOnlyDictionary<string, BaseRelationalReadParameter> parameters,
        IReadOnlyDictionary<string, BaseRelationalReadAggregate> aggregates,
        bool allowAggregate)
    {
        bool valid = operand.Kind switch
        {
            BaseRelationalOperandKind.RecordId => operand.SourceId is { } sourceId && sources.ContainsKey(sourceId) &&
                operand.FieldId is null or "base.recordId" && operand.ParameterId is null && operand.AggregateId is null && operand.Literal is null,
            BaseRelationalOperandKind.SourceField => operand.SourceId is { } sourceId && sources.TryGetValue(sourceId, out BaseRelationalReadSource? source) &&
                operand.FieldId is { } fieldId && (collections[source.CollectionId].Fields ?? []).Any(field => field.Id == fieldId) &&
                operand.ParameterId is null && operand.AggregateId is null && operand.Literal is null,
            BaseRelationalOperandKind.Parameter => operand.ParameterId is { } parameterId && parameters.ContainsKey(parameterId) &&
                operand.SourceId is null && operand.FieldId is null && operand.AggregateId is null && operand.Literal is null,
            BaseRelationalOperandKind.Aggregate => allowAggregate && operand.AggregateId is { } aggregateId && aggregates.ContainsKey(aggregateId) &&
                operand.SourceId is null && operand.FieldId is null && operand.ParameterId is null && operand.Literal is null,
            BaseRelationalOperandKind.Literal => operand.Literal is not null && operand.SourceId is null && operand.FieldId is null &&
                operand.ParameterId is null && operand.AggregateId is null,
            _ => false,
        };
        if (!valid) throw Invalid("A registered read contains an invalid operand reference.");
        if (operand.Kind == BaseRelationalOperandKind.SourceField &&
            OperandKind(operand, sources, collections, parameters, aggregates) is null)
            throw Invalid("A registered read references a non-scalar source field.");
    }

    private static bool ReferencesSource(BaseRelationalOperand operand, string sourceId) =>
        string.Equals(operand.SourceId, sourceId, StringComparison.Ordinal);

    private static QueryValueKind? OperandKind(
        BaseRelationalOperand operand,
        IReadOnlyDictionary<string, BaseRelationalReadSource> sources,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        IReadOnlyDictionary<string, BaseRelationalReadParameter> parameters,
        IReadOnlyDictionary<string, BaseRelationalReadAggregate> aggregates) => operand.Kind switch
    {
        BaseRelationalOperandKind.RecordId => QueryValueKind.Id,
        BaseRelationalOperandKind.SourceField => FieldKind((collections[sources[operand.SourceId!].CollectionId].Fields ?? [])
            .Single(field => field.Id == operand.FieldId)),
        BaseRelationalOperandKind.Parameter when parameters.TryGetValue(operand.ParameterId!, out BaseRelationalReadParameter? parameter) =>
            parameter.Kind == QueryValueKind.Array ? QueryValueKind.Array : parameter.Kind,
        BaseRelationalOperandKind.Literal => operand.Literal!.Kind,
        BaseRelationalOperandKind.Aggregate => AggregateKind(aggregates[operand.AggregateId!], sources, collections, parameters, aggregates),
        _ => null,
    };

    private static QueryValueKind? AggregateKind(
        BaseRelationalReadAggregate aggregate,
        IReadOnlyDictionary<string, BaseRelationalReadSource> sources,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        IReadOnlyDictionary<string, BaseRelationalReadParameter> parameters,
        IReadOnlyDictionary<string, BaseRelationalReadAggregate> aggregates) => aggregate.Kind switch
    {
        BaseAggregateKind.Count or BaseAggregateKind.CountDistinct => QueryValueKind.Integer,
        BaseAggregateKind.Any or BaseAggregateKind.All => QueryValueKind.Boolean,
        BaseAggregateKind.Average => OperandKind(aggregate.Operand!, sources, collections, parameters, aggregates) == QueryValueKind.Number
            ? QueryValueKind.Number : QueryValueKind.Decimal,
        BaseAggregateKind.Sum => OperandKind(aggregate.Operand!, sources, collections, parameters, aggregates) switch
        {
            QueryValueKind.Integer => QueryValueKind.Integer,
            QueryValueKind.Number => QueryValueKind.Number,
            _ => QueryValueKind.Decimal,
        },
        _ => OperandKind(aggregate.Operand!, sources, collections, parameters, aggregates),
    };

    private static QueryValueKind? FieldKind(FieldDefinition field) => field.Format == "date-time"
        ? QueryValueKind.DateTime
        : field.Type switch
    {
        "string" => QueryValueKind.String,
        "boolean" => QueryValueKind.Boolean,
        "integer" => QueryValueKind.Integer,
        "number" => QueryValueKind.Number,
        "decimal" => QueryValueKind.Decimal,
        "dateTime" => QueryValueKind.DateTime,
        "id" => QueryValueKind.Id,
        _ => null,
    };

    private static bool Numeric(QueryValueKind? kind) => kind is QueryValueKind.Integer or QueryValueKind.Number or QueryValueKind.Decimal;
    private static bool Ordered(QueryValueKind? kind) => kind is QueryValueKind.String or QueryValueKind.Integer or QueryValueKind.Number or QueryValueKind.Decimal or QueryValueKind.DateTime or QueryValueKind.Id;
    private static bool Compatible(QueryValueKind? left, QueryValueKind? right) => left is null || right is null || left == right || Numeric(left) && Numeric(right) || left == QueryValueKind.Null || right == QueryValueKind.Null;

    private static bool ValidParameter(BaseRelationalReadParameter parameter, HPDBaseRelationalOptions options)
    {
        if (string.IsNullOrWhiteSpace(parameter.Id) || parameter.Id.Length > 128) return false;
        if (parameter.Kind == QueryValueKind.Array)
            return parameter.ElementKind is not null and not QueryValueKind.Array and not QueryValueKind.Null &&
                parameter.MaxItems is > 0 && parameter.MaxItems <= options.MaxParameterArrayItems &&
                (parameter.ElementKind is QueryValueKind.String or QueryValueKind.Id
                    ? parameter.MaxLength is > 0 && parameter.MaxLength <= options.MaxParameterStringLength
                    : parameter.MaxLength is null);
        return parameter.ElementKind is null && parameter.MaxItems is null &&
            (parameter.Kind is QueryValueKind.String or QueryValueKind.Id
                ? parameter.MaxLength is > 0 && parameter.MaxLength <= options.MaxParameterStringLength
                : parameter.MaxLength is null);
    }

    private static IEnumerable<BaseRelationalOperand> PredicateOperands(BaseRelationalPredicate? predicate)
    {
        if (predicate?.Left is not null) yield return predicate.Left;
        if (predicate?.Right is not null) yield return predicate.Right;
        foreach (BaseRelationalPredicate child in predicate?.Children ?? [])
            foreach (BaseRelationalOperand operand in PredicateOperands(child)) yield return operand;
    }

    private static int Count(BaseRelationalPredicate? predicate) => predicate is null ? 0 : 1 + (predicate.Children?.Sum(Count) ?? 0);
    private static int Depth(BaseRelationalPredicate? predicate) => predicate is null ? 0 : 1 + (predicate.Children?.Select(Depth).DefaultIfEmpty(0).Max() ?? 0);

    private static Dictionary<string, T> Unique<T>(IEnumerable<T> values, Func<T, string> id, string kind)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (T value in values)
            if (!result.TryAdd(id(value), value)) throw Invalid($"A stable {kind} identifier is duplicated.");
        return result;
    }

    private static InvalidOperationException Invalid(string message) => new(message);
}
