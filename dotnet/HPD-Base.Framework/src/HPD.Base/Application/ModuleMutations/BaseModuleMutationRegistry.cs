namespace HPD.Base;

internal sealed class BaseModuleMutationRegistry
{
    private readonly Dictionary<(string Id, int Version), BaseRegisteredModuleMutationDefinition> _operations;
    private readonly Dictionary<string, BaseModuleGenerationCellDefinition> _cells;

    internal BaseModuleMutationRegistry(
        IEnumerable<BaseRegisteredModuleMutationDefinition> operations,
        IEnumerable<BaseModuleGenerationCellDefinition> cells)
    {
        _operations = operations.ToDictionary(static value => (value.Id, value.Version));
        _cells = cells.ToDictionary(static value => value.Id, StringComparer.Ordinal);
    }

    internal BaseRegisteredModuleMutationDefinition? Find(string id, int version) => _operations.GetValueOrDefault((id, version));
    internal BaseModuleGenerationCellDefinition? FindCell(string id) => _cells.GetValueOrDefault(id);
    internal IReadOnlyCollection<BaseRegisteredModuleMutationDefinition> Operations => _operations.Values;
    internal IReadOnlyCollection<BaseModuleGenerationCellDefinition> Cells => _cells.Values;
}

/// <summary>Opaque generated identity for one typed registered module mutation.</summary>
public sealed class BaseGeneratedModuleMutationIdentity<TRequest, TResult> : IBaseSerializerMetadataSource
{
    private System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest>? _request;
    private System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult>? _result;
    private readonly IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> _requestBindings;
    private readonly IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> _resultBindings;

    internal BaseGeneratedModuleMutationIdentity(
        string id,
        int version,
        byte[] checksum,
        BaseSerializerContextRegistration registration,
        IReadOnlyList<BaseSerializerPropertyDeclaration> declarations,
        IReadOnlyList<BaseModuleDtoPropertyBinding> requestBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings)
    {
        Id = id;
        Version = version;
        Checksum = checksum;
        Registration = registration;
        Declarations = declarations;
        _requestBindings = FreezeBindings(requestBindings);
        _resultBindings = FreezeBindings(resultBindings);
    }
    internal BaseGeneratedModuleMutationIdentity(
        string id,
        int version,
        byte[] checksum,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> request,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> result,
        IReadOnlyList<BaseModuleDtoPropertyBinding> requestBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings)
    {
        Id = id;
        Version = version;
        Checksum = checksum.ToArray();
        Registration = null!;
        Declarations = [];
        _request = request;
        _result = result;
        _requestBindings = FreezeBindings(requestBindings);
        _resultBindings = FreezeBindings(resultBindings);
    }
    internal string Id { get; }
    internal int Version { get; }
    internal byte[] Checksum { get; }
    internal System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> RequestTypeInfo =>
        _request ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    internal System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> ResultTypeInfo =>
        _result ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    internal IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> RequestBindings => _requestBindings;
    internal IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> ResultBindings => _resultBindings;
    private BaseSerializerContextRegistration Registration { get; }
    private IReadOnlyList<BaseSerializerPropertyDeclaration> Declarations { get; }
    IReadOnlyList<System.Text.Json.Serialization.Metadata.JsonTypeInfo> IBaseSerializerMetadataSource.Roots => [];
    bool IBaseSerializerMetadataSource.Generated => true;
    BaseSerializerContextRegistration? IBaseSerializerMetadataSource.Registration => Registration;
    IReadOnlyList<Type> IBaseSerializerMetadataSource.RootTypes => [typeof(TRequest), typeof(TResult)];
    IReadOnlyList<BaseSerializerPropertyDeclaration>? IBaseSerializerMetadataSource.SerializerDeclarations => Declarations;
    CollectionDefinition? IBaseSerializerMetadataSource.CollectionDefinition => null;
    void IBaseSerializerMetadataSource.Bind(BaseSerializerMetadataOwner owner)
    {
        _request = owner.Resolve(this, typeof(TRequest)) as System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest>
            ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
        _result = owner.Resolve(this, typeof(TResult)) as System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult>
            ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    }

    private static IReadOnlyDictionary<string, BaseModuleDtoPropertyBinding> FreezeBindings(
        IReadOnlyList<BaseModuleDtoPropertyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var result = new Dictionary<string, BaseModuleDtoPropertyBinding>(StringComparer.Ordinal);
        foreach (BaseModuleDtoPropertyBinding binding in bindings)
        {
            BaseApplicationId.Validate(binding.StablePropertyId, nameof(bindings));
            if (binding.DeclaringType is null
                || string.IsNullOrWhiteSpace(binding.ApplicationName)
                || !result.TryAdd(binding.StablePropertyId, binding with
                {
                    StablePropertyId = new string(binding.StablePropertyId.AsSpan()),
                    ApplicationName = new string(binding.ApplicationName.AsSpan()),
                }))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        return result;
    }
}

/// <summary>Binds one stable DTO property identity to exact graph-owned serializer metadata.</summary>
public sealed record BaseModuleDtoPropertyBinding
{
    /// <summary>Gets the globally stable property edge identity.</summary>
    public required string StablePropertyId { get; init; }
    /// <summary>Gets the declaring DTO type.</summary>
    public required Type DeclaringType { get; init; }
    /// <summary>Gets the exact application property identity.</summary>
    public required string ApplicationName { get; init; }
}

/// <summary>Infrastructure-only factory used by generated module mutation declarations.</summary>
public static class BaseGeneratedModuleMutations
{
    /// <summary>Creates one inert generated identity after generated contract validation.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseGeneratedModuleMutationIdentity<TRequest, TResult> Register<TRequest, TResult>(
        string id,
        int version,
        ReadOnlySpan<byte> checksum,
        BaseSerializerContextRegistration registration,
        IReadOnlyList<BaseSerializerPropertyDeclaration> declarations,
        IReadOnlyList<BaseModuleDtoPropertyBinding> requestBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(requestBindings);
        ArgumentNullException.ThrowIfNull(resultBindings);
        BaseApplicationId.Validate(id, nameof(id));
        if (version < 1 || checksum.Length != BaseModuleMutationChecksum.Length)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        return new(new string(id.AsSpan()), version, checksum.ToArray(), registration, declarations.ToArray(), requestBindings.ToArray(), resultBindings.ToArray());
    }
}

internal static class BaseModuleMutationContractValidator
{
    internal static void ValidateCell(BaseModuleGenerationCellDefinition value)
    {
        BaseApplicationId.Validate(value.Id, nameof(value));
        BaseApplicationId.Validate(value.OwningModuleId, nameof(value));
        if (value.Version < 1 || !Enum.IsDefined(value.Scope)
            || value.MaximumKeyUtf8Bytes is < 1 or > 256
            || value.MaximumCellsPerOperation is < 1 or > 128)
            throw new InvalidOperationException("base.moduleMutation.invalid");
    }

    internal static void ValidateDefinition(
        BaseRegisteredModuleMutationDefinition value,
        IReadOnlyDictionary<string, CollectionDefinition> collections,
        IReadOnlyDictionary<string, BaseModuleGenerationCellDefinition> cells)
    {
        BaseApplicationId.Validate(value.Id, nameof(value));
        BaseApplicationId.Validate(value.OwningModuleId, nameof(value));
        BaseApplicationId.Validate(value.GrantId, nameof(value));
        BaseApplicationId.Validate(value.RequestTypeId, nameof(value));
        BaseApplicationId.Validate(value.ResultTypeId, nameof(value));
        if (value.Version < 1 || !Enum.IsDefined(value.Audience)
            || value.ReceiptPolicy.FormatVersion != 1 || value.ReceiptPolicy.Lifetime <= TimeSpan.Zero)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        string[] operationCollections = [.. value.SystemCollectionIds.Order(StringComparer.Ordinal)];
        if (!operationCollections.SequenceEqual(value.SystemCollectionIds, StringComparer.Ordinal)
            || operationCollections.Distinct(StringComparer.Ordinal).Count() != operationCollections.Length)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        foreach (string id in operationCollections)
        {
            if (!collections.TryGetValue(id, out CollectionDefinition? collection)
                || !collection.System
                || !string.Equals(collection.SystemOwnerModuleId, value.OwningModuleId, StringComparison.Ordinal))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        foreach (string id in value.GenerationCellIds)
        {
            if (!cells.TryGetValue(id, out BaseModuleGenerationCellDefinition? cell)
                || !string.Equals(cell.OwningModuleId, value.OwningModuleId, StringComparison.Ordinal))
                throw new InvalidOperationException("base.moduleMutation.invalid");
        }
        ValidateTemplate(value.Template, value.Limits);
    }

    private static void ValidateTemplate(BaseModuleMutationTemplate template, BaseModuleMutationLimits limits)
    {
        if (template.Captures.Length > limits.MaximumCaptures || template.Guards.Length > limits.MaximumGuardNodes)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        if (template.Captures.Select(static value => value.Id).Distinct(StringComparer.Ordinal).Count() != template.Captures.Length
            || template.Guards.Select(static value => value.Id).Distinct(StringComparer.Ordinal).Count() != template.Guards.Length)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        HashSet<string> guards = template.Guards.Select(static value => value.Id).ToHashSet(StringComparer.Ordinal);
        foreach (BaseModuleLogicalGuard guard in template.Guards.OfType<BaseModuleLogicalGuard>())
            if (guard.ChildGuardIds.Any(id => !guards.Contains(id))) throw new InvalidOperationException("base.moduleMutation.invalid");
        var statements = new List<BaseModuleStatement>();
        Collect(template.Body, statements);
        if (statements.Count > limits.MaximumStatements
            || statements.Select(static value => value.Id).Distinct(StringComparer.Ordinal).Count() != statements.Count)
            throw new InvalidOperationException("base.moduleMutation.invalid");
        foreach (BaseModuleIfStatement statement in statements.OfType<BaseModuleIfStatement>())
            if (!guards.Contains(statement.GuardId)) throw new InvalidOperationException("base.moduleMutation.invalid");
        foreach (BaseModuleRequireStatement statement in statements.OfType<BaseModuleRequireStatement>())
            if (!guards.Contains(statement.GuardId)) throw new InvalidOperationException("base.moduleMutation.invalid");
    }

    private static void Collect(BaseModuleMutationBlock block, List<BaseModuleStatement> output)
    {
        if (block.Statements.IsDefaultOrEmpty) throw new InvalidOperationException("base.moduleMutation.invalid");
        foreach (BaseModuleStatement statement in block.Statements)
        {
            output.Add(statement);
            if (statement is BaseModuleIfStatement branch) { Collect(branch.WhenTrue, output); Collect(branch.WhenFalse, output); }
        }
    }
}
