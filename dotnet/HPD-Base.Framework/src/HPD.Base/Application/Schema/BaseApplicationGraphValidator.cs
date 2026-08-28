namespace HPD.Base;

internal static class BaseApplicationGraphValidator
{
    internal static void Validate(
        CollectionDefinition[] collections,
        IEnumerable<IBaseReadRegistration> readRegistrations,
        BaseSubjectContractRegistry subjects,
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
            if (collection.System && string.IsNullOrWhiteSpace(collection.SystemOwnerModuleId)
                || !collection.System && collection.SystemOwnerModuleId is not null)
                throw new InvalidOperationException(BaseConfidentialityErrorCodes.ContractInvalid);
            if (collection.SystemOwnerModuleId is not null)
                BaseApplicationId.Validate(collection.SystemOwnerModuleId, nameof(collection.SystemOwnerModuleId));
            FieldDefinition[] fields = collection.Fields ?? [];
            if (fields.Length > schema.MaxFieldsPerCollection)
                throw Invalid($"Collection '{collection.Id}' exceeds the configured field limit.");
            var fieldById = Unique(fields, static field => field.Id, "field");
            Unique(fields, static field => field.WireName, "stored field name");
            foreach (FieldDefinition field in fields)
            {
                BaseApplicationId.Validate(field.Id, nameof(field.Id));
                BaseFieldDisclosurePolicy disclosure;
                try { disclosure = BaseConfidentialityPolicy.Normalize(field.Confidentiality, field.Disclosure); }
                catch (InvalidOperationException) { throw Invalid($"Field '{field.Id}' has an invalid confidentiality contract."); }
                bool binary = string.Equals(field.Format, "base64", StringComparison.Ordinal);
                if (binary != (field.MaximumBytes is not null)
                    || binary && (field.MinimumBytes is null or < 0 || field.MaximumBytes is < 1 or > 1_048_576
                        || field.MinimumBytes > field.MaximumBytes)
                    || !binary && field.MinimumBytes is not null)
                    throw Invalid($"Field '{field.Id}' has an invalid binary contract.");
                int fieldOrdinal = Array.FindIndex(fields, candidate => string.Equals(candidate.Id, field.Id, StringComparison.Ordinal));
                if (disclosure.Indexing == BaseIndexDisclosure.Forbidden && (collection.Indexes ?? []).Any(index => index.Parts.Any(part => part.FieldOrdinal == fieldOrdinal)))
                    throw Invalid($"Field '{field.Id}' cannot influence an index.");
                if (!globalFieldIds.Add(field.Id))
                    throw Invalid($"Stable field identifier '{field.Id}' is duplicated across the application graph.");
                if (field.Relation is not null && field.SubjectReference is not null)
                    throw Invalid($"Field '{field.Id}' cannot be both a relation and an exported-subject reference.");
                if (field.Relation is { } relation)
                    ValidateRelation(collection, field, relation, collectionById, globalRelationIds);
            }

            foreach (BaseLogicalIndexDefinition index in collection.Indexes ?? [])
            {
                if (!globalIndexIds.Add(index.Id.ToString()))
                    throw Invalid($"Stable index identifier '{index.Id}' is duplicated across the application graph.");
                if (!string.Equals(index.CollectionId, collection.Id, StringComparison.Ordinal) ||
                    index.Parts.IsDefaultOrEmpty || index.Parts.Any(component => component.FieldOrdinal < 0 || component.FieldOrdinal >= fields.Length))
                    throw Invalid($"Index '{index.Id}' has an invalid collection or field reference.");
            }
        }

        if (globalRelationIds.Count > schema.MaxRelations || globalIndexIds.Count > schema.MaxIndexes)
            throw Invalid("The BASE application graph exceeds its configured relation or index limit.");
        foreach (IBaseReadRegistration read in reads)
            ValidateRead(read, collectionById, subjects, relational);
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
        if (relation.Required != (field.Presence == BaseFieldPresence.Required) || (relation.Required && field.Nullability == BaseFieldNullability.Nullable) ||
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
        BaseSubjectContractRegistry subjects,
        HPDBaseRelationalOptions options)
    {
        BaseRelationalReadPlan plan = registration.Plan;
        ValidateReadConfidentiality(registration, collections);
        bool compound = plan.Topology == BaseRelationalReadTopology.CompoundCount;
        bool paginationValid = plan.Window is null && plan.Pagination is not null && plan.Pagination.Mode switch
        {
            BaseRegisteredReadPaginationMode.PageOnly => plan.Pagination.MaximumOffset == 0,
            BaseRegisteredReadPaginationMode.PageAndOffset => !compound && plan.Pagination.MaximumOffset is >= 0 and <= 1_000_000,
            _ => false,
        };
        bool topologyValid = compound
            ? ValidCompoundTopology(registration, plan, collections, options)
            : plan.Topology == BaseRelationalReadTopology.Ordinary && plan.CompoundCountBranches.Length == 0
                && plan.CompoundChecksum is null && plan.Budgets.MaxCompoundBranches == 0 && plan.Budgets.MaxCompoundOperations == 0
                && plan.Joins.Length == plan.Sources.Length - 1;
        if (!topologyValid || !paginationValid || !string.Equals(registration.Id, plan.Id, StringComparison.Ordinal) ||
            plan.Sources.Length == 0 || plan.Sources.Length > options.MaxSources ||
            plan.Joins.Length > options.MaxJoins || plan.Parameters.Length > options.MaxParameters ||
            plan.GroupKeys.Length > options.MaxGroupKeys || plan.Aggregates.Length > options.MaxAggregates ||
            (!compound && plan.Projection.Length == 0) || plan.Projection.Length > options.MaxProjectionFields ||
            plan.Sort.Length > options.MaxSortFields ||
            plan.Sources.Any(source => !collections.ContainsKey(source.CollectionId)) ||
            plan.Consistency != BaseReadConsistency.Snapshot || plan.DependencyMode != BaseReadDependencyMode.Complete ||
            plan.Budgets.MaxResultRows < 1 || plan.Budgets.MaxResultRows > options.MaxResultRows ||
            plan.Budgets.MaxResultBytes < 1 || plan.Budgets.MaxResultBytes > options.MaxRegisteredReadResultBytes || plan.Budgets.MaxOperations < 1 ||
            plan.Budgets.MaxExecutionMilliseconds < 1 || plan.Budgets.MaxExecutionMilliseconds > options.MaxExecutionDuration.TotalMilliseconds)
            throw Invalid($"Read '{registration.Id}' has an invalid or over-limit topology.");
        Dictionary<string, BaseRelationalReadSource> sources = Unique(plan.Sources, static source => source.Id, "read source");
        Unique(plan.Projection, static projection => projection.FieldId, "read projection");
        Dictionary<string, BaseRelationalReadAggregate> aggregates = Unique(plan.Aggregates, static aggregate => aggregate.Id, "read aggregate");
        Dictionary<string, BaseRelationalReadParameter> parameters = Unique(plan.Parameters, static parameter => parameter.Id, "read parameter");
        if (parameters.Values.Any(parameter => !ValidParameter(parameter, options)))
            throw Invalid($"Read '{registration.Id}' contains an invalid parameter definition.");
        ValidateBinaryReadContracts(registration, plan, sources, collections, parameters);
        int nodes = Count(plan.Predicate) + Count(plan.Having) + plan.CompoundCountBranches.Sum(static branch => Count(branch.Predicate));
        if (nodes > options.MaxPredicateNodes || Depth(plan.Predicate) > options.MaxPredicateDepth || Depth(plan.Having) > options.MaxPredicateDepth)
            throw Invalid($"Read '{registration.Id}' exceeds predicate limits.");
        foreach (BaseRelationalCompoundCountBranch branch in plan.CompoundCountBranches)
        {
            if (Depth(branch.Predicate) > options.MaxPredicateDepth)
                throw Invalid($"Read '{registration.Id}' exceeds predicate limits.");
            ValidatePredicate(branch.Predicate,
                new Dictionary<string, BaseRelationalReadSource>(StringComparer.Ordinal) { [branch.Source.Id] = branch.Source },
                collections, parameters, aggregates, allowAggregate: false);
        }
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
                operand.Kind is BaseRelationalOperandKind.SourceField or BaseRelationalOperandKind.RecordId or BaseRelationalOperandKind.RecordRevision &&
                !groupKeys.Contains(operand)))
            throw Invalid($"Read '{registration.Id}' has a having operand that is not a group key or aggregate.");
        foreach (BaseRelationalReadProjection projection in plan.Projection)
        {
            if (projection.Operand.Kind == BaseRelationalOperandKind.SubjectReference)
                ValidateSubjectReferenceProjection(projection.Operand, sources);
            else if (projection.Operand.Kind == BaseRelationalOperandKind.StoredSubjectReference)
                ValidateStoredSubjectReferenceProjection(registration, projection, sources, collections, subjects);
            else
                ValidateOperand(projection.Operand, sources, collections, parameters, aggregates, allowAggregate: true);
        }
        ValidateCanonicalJsonReadContracts(registration, plan, sources, collections, parameters);
        foreach (BaseRelationalReadSort sort in plan.Sort) ValidateOperand(sort.Operand, sources, collections, parameters, aggregates, allowAggregate: true);
    }

    private static bool ValidCompoundTopology(
        IBaseReadRegistration registration,
        BaseRelationalReadPlan plan,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        HPDBaseRelationalOptions options)
    {
        BaseRelationalCompoundCountBranch[] branches = plan.CompoundCountBranches;
        int compoundOperations = branches.Length + branches.Sum(static branch => Count(branch.Predicate));
        if (branches.Length is < 1 or > 32 || branches.Length > options.MaxCompoundReadBranches
            || plan.CompoundChecksum is not { IsValid: true }
            || plan.Budgets.MaxCompoundBranches < branches.Length || plan.Budgets.MaxCompoundBranches > options.MaxCompoundReadBranches
            || plan.Budgets.MaxCompoundOperations < branches.Length || plan.Budgets.MaxCompoundOperations > options.MaxCompoundReadOperations
            || compoundOperations > plan.Budgets.MaxCompoundOperations || compoundOperations > plan.Budgets.MaxOperations
            || branches.Length > options.MaxAggregates
            || plan.Budgets.MaxResultRows != branches.Length
            || plan.Joins.Length != 0 || plan.Predicate is not null || plan.GroupKeys.Length != 0 || plan.Aggregates.Length != 0
            || plan.Having is not null || plan.Projection.Length != 0 || plan.Distinct || plan.Sort.Length != 0
            || plan.Sources.Length != branches.Length || registration.ClientContract.Row.Count != 2)
            return false;
        string discriminatorField = branches[0].DiscriminatorOutputFieldId;
        string countField = branches[0].CountOutputFieldId;
        if (registration.ClientContract.Row.Count(field => string.Equals(field.Id, discriminatorField, StringComparison.Ordinal)
                && field.Kind == QueryValueKind.String && !field.Array && !field.Nullable) != 1
            || registration.ClientContract.Row.Count(field => string.Equals(field.Id, countField, StringComparison.Ordinal)
                && field.Kind == QueryValueKind.Integer && !field.Array && !field.Nullable) != 1)
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal); var discriminators = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;
        for (int index = 0; index < branches.Length; index++)
        {
            BaseRelationalCompoundCountBranch branch = branches[index];
            if (!branch.BranchChecksum.IsValid || !ids.Add(branch.Id) || !discriminators.Add(branch.Discriminator)
                || !string.Equals(branch.Source.Id, branch.Id + ".source", StringComparison.Ordinal)
                || !string.Equals(plan.Sources[index].Id, branch.Source.Id, StringComparison.Ordinal)
                || !string.Equals(plan.Sources[index].CollectionId, branch.Source.CollectionId, StringComparison.Ordinal)
                || !collections.ContainsKey(branch.Source.CollectionId)
                || !string.Equals(branch.DiscriminatorOutputFieldId, discriminatorField, StringComparison.Ordinal)
                || !string.Equals(branch.CountOutputFieldId, countField, StringComparison.Ordinal)
                || previous is not null && StringComparer.Ordinal.Compare(previous, branch.Discriminator) >= 0
                || string.Equals(branch.Discriminator, branch.Id, StringComparison.Ordinal)
                || string.Equals(branch.Discriminator, branch.Source.Id, StringComparison.Ordinal)
                || collections.ContainsKey(branch.Discriminator)) return false;
            previous = branch.Discriminator;
        }
        return true;
    }

    private static void ValidateStoredSubjectReferenceProjection(
        IBaseReadRegistration registration,
        BaseRelationalReadProjection projection,
        IReadOnlyDictionary<string, BaseRelationalReadSource> sources,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        BaseSubjectContractRegistry subjects)
    {
        BaseRelationalOperand operand = projection.Operand;
        if (operand.SourceId is not { } sourceId || !sources.TryGetValue(sourceId, out BaseRelationalReadSource? source)
            || operand.FieldId is not { } fieldId || operand.SubjectContractId is not { } contractId
            || operand.SubjectContractVersion is not > 0
            || operand.ParameterId is not null || operand.AggregateId is not null || operand.Literal is not null)
            throw Invalid("A stored subject-reference projection is invalid.");
        FieldDefinition? field = (collections[source.CollectionId].Fields ?? [])
            .SingleOrDefault(candidate => string.Equals(candidate.Id, fieldId, StringComparison.Ordinal));
        BaseSubjectReferenceDefinition? reference = field?.SubjectReference;
        BaseReadClientProperty? output = registration.ClientContract.Row
            .SingleOrDefault(candidate => string.Equals(candidate.Id, projection.FieldId, StringComparison.Ordinal));
        if (reference is null || output is null || output.Kind != QueryValueKind.SubjectReference
            || !string.Equals(reference.ContractId, contractId, StringComparison.Ordinal)
            || reference.ContractVersion != operand.SubjectContractVersion
            || subjects.Find(contractId, operand.SubjectContractVersion.Value) is null
            || field!.Presence == BaseFieldPresence.Optional && !output.Nullable
            || field.Nullability == BaseFieldNullability.Nullable && !output.Nullable)
            throw Invalid("A stored subject-reference projection does not match its finalized field and output authority.");
    }

    private static void ValidateSubjectReferenceProjection(
        BaseRelationalOperand operand,
        IReadOnlyDictionary<string, BaseRelationalReadSource> sources)
    {
        if (operand.SourceId is not { } sourceId || !sources.ContainsKey(sourceId)
            || operand.SubjectContractId is not { } contractId || operand.SubjectContractVersion is not > 0
            || operand.FieldId is not null || operand.ParameterId is not null || operand.AggregateId is not null || operand.Literal is not null)
            throw Invalid("A registered subject-acquisition projection is invalid.");
        BaseApplicationId.Validate(contractId, nameof(operand.SubjectContractId));
    }

    private static void ValidateReadConfidentiality(
        IBaseReadRegistration registration,
        IReadOnlyDictionary<string, CollectionDefinition> collections)
    {
        BaseRelationalReadPlan plan = registration.Plan;
        string[] sources = plan.Sources.Select(static source => source.CollectionId).Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        string[] system = sources.Where(id => collections[id].System).ToArray();
        string[] declaredSystem = registration.SystemSourceIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        if (registration.SourceAuthority == BaseRegisteredReadSourceAuthority.Ordinary && system.Length != 0 ||
            registration.SourceAuthority == BaseRegisteredReadSourceAuthority.System &&
                (!system.SequenceEqual(declaredSystem, StringComparer.Ordinal) || system.Length != sources.Length))
            throw Invalid($"Read '{registration.Id}' has invalid system-source authority.");

        HashSet<string> projected = plan.Projection.Select(static item => item.FieldId).ToHashSet(StringComparer.Ordinal);
        HashSet<string> confidential = registration.ConfidentialOutputFieldIds.ToHashSet(StringComparer.Ordinal);
        HashSet<string> secret = registration.SecretOutputFieldIds.ToHashSet(StringComparer.Ordinal);
        if (!confidential.IsSubsetOf(projected) || !secret.IsSubsetOf(projected) || confidential.Overlaps(secret) ||
            registration.Disclosure == BaseRegisteredReadDisclosure.Ordinary && (confidential.Count != 0 || secret.Count != 0) ||
            registration.Disclosure == BaseRegisteredReadDisclosure.ConfidentialProjection && secret.Count != 0 ||
            string.IsNullOrEmpty(registration.RequiredGrantId))
            throw Invalid($"Read '{registration.Id}' has invalid disclosure authority.");

        Dictionary<string, string> sourceCollections = plan.Sources.ToDictionary(static source => source.Id, static source => source.CollectionId, StringComparer.Ordinal);
        foreach (BaseRelationalReadProjection output in plan.Projection)
        {
            if (output.Operand.Kind is not (BaseRelationalOperandKind.SourceField or BaseRelationalOperandKind.StoredSubjectReference)
                || output.Operand.SourceId is null || output.Operand.FieldId is null)
                continue;
            FieldDefinition? field = collections[sourceCollections[output.Operand.SourceId]].Fields?.SingleOrDefault(item => item.Id == output.Operand.FieldId);
            if (field?.Confidentiality == BaseFieldConfidentiality.Confidential && !confidential.Contains(output.FieldId) ||
                field?.Confidentiality == BaseFieldConfidentiality.Secret && !secret.Contains(output.FieldId))
                throw Invalid($"Read '{registration.Id}' projects a protected field without exact disclosure authority.");
        }
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
            throw Invalid($"A registered read contains an unsupported predicate/type combination ({predicate.Kind}, {predicate.Operator}, {left}, {right}).");
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
            BaseRelationalOperandKind.RecordRevision => operand.SourceId is { } sourceId && sources.ContainsKey(sourceId) &&
                operand.FieldId is null or "base.revision" && operand.ParameterId is null && operand.AggregateId is null && operand.Literal is null,
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
            throw Invalid($"A registered read references non-scalar source field '{operand.FieldId}'.");
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
        BaseRelationalOperandKind.RecordRevision => QueryValueKind.String,
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

    private static QueryValueKind? FieldKind(FieldDefinition field) => field.ScalarKind is BaseScalarKind.Guid or BaseScalarKind.RecordId
        ? QueryValueKind.Id
        : field.ScalarKind == BaseScalarKind.CanonicalJson
        ? QueryValueKind.CanonicalJson
        : field.ScalarKind == BaseScalarKind.ModuleGeneration
        ? QueryValueKind.String
        : field.Format == "date-time"
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
        bool binary = parameter.MaximumBinaryBytes is not null;
        if (binary
            ? parameter.MinimumBinaryBytes is < 0 || parameter.MaximumBinaryBytes is < 1 or > 1_048_576 ||
                parameter.MinimumBinaryBytes > parameter.MaximumBinaryBytes || parameter.MaxLength is not null ||
                parameter.Kind != QueryValueKind.String &&
                (parameter.Kind != QueryValueKind.Array || parameter.ElementKind != QueryValueKind.String)
            : parameter.MinimumBinaryBytes is not null)
            return false;
        if (parameter.Kind == QueryValueKind.Array)
            return parameter.ElementKind is not null and not QueryValueKind.Array and not QueryValueKind.Null &&
                parameter.MaxItems is > 0 && parameter.MaxItems <= options.MaxParameterArrayItems &&
                (parameter.ElementKind is QueryValueKind.String or QueryValueKind.Id
                    ? binary || parameter.MaxLength is > 0 && parameter.MaxLength <= options.MaxParameterStringLength
                    : parameter.MaxLength is null);
        return parameter.ElementKind is null && parameter.MaxItems is null &&
            (parameter.Kind is QueryValueKind.String or QueryValueKind.Id
                ? binary || parameter.MaxLength is > 0 && parameter.MaxLength <= options.MaxParameterStringLength
                : parameter.MaxLength is null) &&
            (parameter.Kind == QueryValueKind.CanonicalJson
                ? parameter.CanonicalJsonAuthority is { } authority && BaseReadCanonicalJsonAuthorityContract.Valid(authority)
                : parameter.CanonicalJsonAuthority is null);
    }

    private static void ValidateBinaryReadContracts(
        IBaseReadRegistration registration,
        BaseRelationalReadPlan plan,
        IReadOnlyDictionary<string, BaseRelationalReadSource> sources,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        IReadOnlyDictionary<string, BaseRelationalReadParameter> parameters)
    {
        IReadOnlyDictionary<string, BaseReadClientProperty> parameterProperties = registration.ClientContract.Parameters
            .ToDictionary(static value => value.Id, StringComparer.Ordinal);
        if (parameterProperties.Count != parameters.Count || parameters.Any(pair =>
                !parameterProperties.TryGetValue(pair.Key, out BaseReadClientProperty? property) ||
                property.MinimumBinaryBytes != pair.Value.MinimumBinaryBytes ||
                property.MaximumBinaryBytes != pair.Value.MaximumBinaryBytes))
            throw Invalid("A registered-read parameter scalar contract does not match its generated client contract.");

        IReadOnlyDictionary<string, BaseReadClientProperty> rowProperties = registration.ClientContract.Row
            .ToDictionary(static value => value.Id, StringComparer.Ordinal);
        foreach (BaseReadClientProperty property in rowProperties.Values)
        {
            bool binary = property.MaximumBinaryBytes is not null;
            if (binary
                ? property.Kind != QueryValueKind.String || property.Array || property.MinimumBinaryBytes is < 0 ||
                    property.MaximumBinaryBytes is < 1 or > 1_048_576 || property.MinimumBinaryBytes > property.MaximumBinaryBytes
                : property.MinimumBinaryBytes is not null)
                throw Invalid("A registered-read result scalar contract is invalid.");
        }
        foreach (BaseRelationalReadProjection projection in plan.Projection)
        {
            if (!rowProperties.TryGetValue(projection.FieldId, out BaseReadClientProperty? output))
                throw Invalid("A registered-read projection is absent from its generated client contract.");
            if (output.MaximumBinaryBytes is null) continue;
            if (projection.Operand.Kind != BaseRelationalOperandKind.SourceField ||
                !sources.TryGetValue(projection.Operand.SourceId!, out BaseRelationalReadSource? source))
                throw Invalid("A binary registered-read result must be an exact source-field projection.");
            FieldDefinition field = collections[source.CollectionId].Fields!.Single(candidate =>
                string.Equals(candidate.Id, projection.Operand.FieldId, StringComparison.Ordinal));
            if (field.ScalarKind != BaseScalarKind.Binary ||
                field.ScalarConstraints?.MinimumBinaryBytes != output.MinimumBinaryBytes ||
                field.ScalarConstraints?.MaximumBinaryBytes != output.MaximumBinaryBytes)
                throw Invalid("A binary registered-read result does not match its installed source-field bounds.");
        }
    }

    private static void ValidateCanonicalJsonReadContracts(
        IBaseReadRegistration registration,
        BaseRelationalReadPlan plan,
        IReadOnlyDictionary<string, BaseRelationalReadSource> sources,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        IReadOnlyDictionary<string, BaseRelationalReadParameter> parameters)
    {
        foreach (BaseRelationalReadParameter parameter in parameters.Values.Where(static value => value.Kind == QueryValueKind.CanonicalJson))
        {
            BaseReadCanonicalJsonAuthority authority = parameter.CanonicalJsonAuthority!;
            BaseRelationalReadSource[] matchingSources = sources.Values.Where(source =>
                string.Equals(source.CollectionId, authority.CollectionId, StringComparison.Ordinal)).ToArray();
            FieldDefinition? field = (collections.GetValueOrDefault(authority.CollectionId)?.Fields ?? [])
                .SingleOrDefault(candidate => string.Equals(candidate.Id, authority.FieldId, StringComparison.Ordinal));
            if (matchingSources.Length == 0 || field is null ||
                BaseReadCanonicalJsonAuthorityContract.Create(authority.CollectionId, field) != authority ||
                parameter.Nullable && field.Presence == BaseFieldPresence.Required && field.Nullability == BaseFieldNullability.NonNullable)
                throw Invalid("A canonical-JSON parameter authority does not match its installed source field.");

            BaseRelationalPredicate[] occurrences = PredicateNodes(plan.Predicate)
                .Where(node => node.Left?.Kind == BaseRelationalOperandKind.SourceField
                    && node.Right?.Kind == BaseRelationalOperandKind.Parameter
                    && string.Equals(node.Right.ParameterId, parameter.Id, StringComparison.Ordinal)).ToArray();
            if (occurrences.Length == 0 || occurrences.Any(node =>
                node.Kind != FilterNodeKind.Compare || node.Operator is not (FilterOperator.Equal or FilterOperator.NotEqual)
                || !matchingSources.Any(source => string.Equals(source.Id, node.Left!.SourceId, StringComparison.Ordinal))
                || !string.Equals(node.Left!.FieldId, authority.FieldId, StringComparison.Ordinal))
                || Operands(plan).Count(operand => operand.Kind == BaseRelationalOperandKind.Parameter
                    && string.Equals(operand.ParameterId, parameter.Id, StringComparison.Ordinal)) != occurrences.Length)
                throw Invalid("A canonical-JSON parameter is used outside its exact source-bound equality contract.");
        }

        foreach (BaseRelationalReadProjection projection in plan.Projection)
        {
            QueryValueKind? kind = OperandKind(projection.Operand, sources, collections, parameters,
                plan.Aggregates.ToDictionary(static value => value.Id, StringComparer.Ordinal));
            if (kind != QueryValueKind.CanonicalJson)
            {
                if (projection.CanonicalJsonAuthority is not null) throw Invalid("A non-JSON projection carries canonical-JSON authority.");
                continue;
            }
            if (projection.Operand.Kind != BaseRelationalOperandKind.SourceField || projection.CanonicalJsonAuthority is not { } authority
                || !BaseReadCanonicalJsonAuthorityContract.Valid(authority)
                || !sources.TryGetValue(projection.Operand.SourceId!, out BaseRelationalReadSource? source)
                || !string.Equals(source.CollectionId, authority.CollectionId, StringComparison.Ordinal)
                || !string.Equals(projection.Operand.FieldId, authority.FieldId, StringComparison.Ordinal))
                throw Invalid("A canonical-JSON projection is not an exact source-field projection.");
            FieldDefinition field = collections[source.CollectionId].Fields!.Single(candidate =>
                string.Equals(candidate.Id, authority.FieldId, StringComparison.Ordinal));
            BaseReadClientProperty output = registration.ClientContract.Row.Single(candidate =>
                string.Equals(candidate.Id, projection.FieldId, StringComparison.Ordinal));
            bool sourceNullable = field.Presence == BaseFieldPresence.Optional || field.Nullability == BaseFieldNullability.Nullable;
            if (sourceNullable != output.Nullable)
                throw Invalid("A canonical-JSON projection nullability does not match its source field.");
        }
    }

    private static IEnumerable<BaseRelationalPredicate> PredicateNodes(BaseRelationalPredicate? predicate)
    {
        if (predicate is null) yield break;
        yield return predicate;
        foreach (BaseRelationalPredicate child in predicate.Children ?? [])
            foreach (BaseRelationalPredicate descendant in PredicateNodes(child)) yield return descendant;
    }

    private static IEnumerable<BaseRelationalOperand> Operands(BaseRelationalReadPlan plan)
    {
        foreach (BaseRelationalReadJoin join in plan.Joins) { yield return join.Left; yield return join.Right; }
        foreach (BaseRelationalPredicate node in PredicateNodes(plan.Predicate))
        { if (node.Left is not null) yield return node.Left; if (node.Right is not null) yield return node.Right; }
        foreach (BaseRelationalOperand operand in plan.GroupKeys) yield return operand;
        foreach (BaseRelationalReadAggregate aggregate in plan.Aggregates) if (aggregate.Operand is not null) yield return aggregate.Operand;
        foreach (BaseRelationalPredicate node in PredicateNodes(plan.Having))
        { if (node.Left is not null) yield return node.Left; if (node.Right is not null) yield return node.Right; }
        foreach (BaseRelationalReadProjection projection in plan.Projection) yield return projection.Operand;
        foreach (BaseRelationalReadSort sort in plan.Sort) yield return sort.Operand;
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
