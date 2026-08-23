using HPD.Base.Sqlite;

namespace HPD.Base.Sqlite;

internal sealed class SqliteNames
{
    /// <summary>Initializes a new instance.</summary>
    public SqliteNames(HPDBaseSqliteOptions options)
    {
        Prefix = options.SchemaPrefix;
        Collections = Prefix + "collections";
        ProviderState = Prefix + "provider_state";
        MutationJournal = Prefix + "mutation_journal";
        OperationReceipts = Prefix + "operation_receipts";
        SchemaIdentity = Prefix + "schema_identity";
        SchemaBaseline = Prefix + "schema_baseline";
        SchemaAssets = Prefix + "schema_assets";
        SchemaHistory = Prefix + "schema_history";
        SchemaLease = Prefix + "schema_lease";
        SubjectContracts = Prefix + "subject_contracts";
        SubjectLifetimes = Prefix + "subject_lifetimes";
        SubjectTerminalLifetimes = Prefix + "subject_terminal_lifetimes";
        SubjectLifecycleFacts = Prefix + "subject_lifecycle_facts";
        SubjectLifecycleMemberships = Prefix + "subject_lifecycle_memberships";
        SubjectLifecycleConsumers = Prefix + "subject_lifecycle_consumers";
        SubjectLifecycleCheckpoints = Prefix + "subject_lifecycle_checkpoints";
        SubjectLifecycleMaintenance = Prefix + "subject_lifecycle_maintenance";
        SubjectLifecycleScopeStage = Prefix + "subject_lifecycle_scope_stage";
        SubjectLifecycleMembershipStage = Prefix + "subject_lifecycle_membership_stage";
        SubjectRetirementBarriers = Prefix + "subject_retirement_barriers";
        SubjectRetirementAcknowledgements = Prefix + "subject_retirement_acknowledgements";
        SubjectRetirementTerminals = Prefix + "subject_retirement_terminals";
        SubjectRetirementPublications = Prefix + "subject_retirement_publications";
        SubjectMaintenance = Prefix + "subject_maintenance";
        SubjectRewriteStage = Prefix + "subject_rewrite_stage";
        ModuleGenerations = Prefix + "module_generations";
        ModuleMutationDefinitions = Prefix + "module_mutation_definitions";
        ModuleGenerationDefinitions = Prefix + "module_generation_definitions";
        SemanticActivationDefinitions = Prefix + "semantic_activation_definitions";
        SemanticActivationScopes = Prefix + "semantic_activation_scopes";
        SemanticActivationSlots = Prefix + "semantic_activation_slots";
        Activations = Prefix + "activations";
        Executors = Prefix + "activation_executors";
        ActivationEffects = Prefix + "activation_effects";
        ActivationSchedules = Prefix + "activation_schedules";
        ActivationOccurrences = Prefix + "activation_occurrences";
        ActivationScheduleCancellations = Prefix + "activation_schedule_cancellations";
        ActivationReceipts = Prefix + "activation_receipts";
        MutationJournalScopeIndex = "ix_" + Prefix + "mutation_journal_scope_position";
    }

    /// <summary>Gets the prefix.</summary>
    public string Prefix { get; }
    /// <summary>Gets the collections.</summary>
    public string Collections { get; }
    /// <summary>Gets the provider state.</summary>
    public string ProviderState { get; }
    /// <summary>Gets the mutation journal.</summary>
    public string MutationJournal { get; }
    /// <summary>Gets the durable atomic request receipt table.</summary>
    public string OperationReceipts { get; }
    /// <summary>Gets the physical store identity.</summary>
    public string SchemaIdentity { get; }
    /// <summary>Gets the schema baseline.</summary>
    public string SchemaBaseline { get; }
    /// <summary>Gets the schema assets.</summary>
    public string SchemaAssets { get; }
    /// <summary>Gets the schema history.</summary>
    public string SchemaHistory { get; }
    /// <summary>Gets the schema lease.</summary>
    public string SchemaLease { get; }
    /// <summary>Gets the provider-owned exported-subject contract-state table.</summary>
    public string SubjectContracts { get; }
    /// <summary>Gets the provider-owned current subject-lifetime table.</summary>
    public string SubjectLifetimes { get; }
    public string SubjectTerminalLifetimes { get; }
    public string SubjectLifecycleFacts { get; }
    public string SubjectLifecycleMemberships { get; }
    public string SubjectLifecycleConsumers { get; }
    public string SubjectLifecycleCheckpoints { get; }
    public string SubjectLifecycleMaintenance { get; }
    public string SubjectLifecycleScopeStage { get; }
    public string SubjectLifecycleMembershipStage { get; }
    public string SubjectRetirementBarriers { get; }
    public string SubjectRetirementAcknowledgements { get; }
    public string SubjectRetirementTerminals { get; }
    public string SubjectRetirementPublications { get; }
    /// <summary>Gets the provider-owned subject-authority maintenance checkpoint table.</summary>
    public string SubjectMaintenance { get; }
    /// <summary>Gets the provider-owned subject-reference rewrite staging table.</summary>
    public string SubjectRewriteStage { get; }
    /// <summary>Gets provider-owned registered-module generation cells.</summary>
    public string ModuleGenerations { get; }
    /// <summary>Gets persisted registered module-mutation schema authority.</summary>
    public string ModuleMutationDefinitions { get; }
    /// <summary>Gets persisted module-generation-cell schema authority.</summary>
    public string ModuleGenerationDefinitions { get; }
    /// <summary>Gets persisted semantic-definition authority.</summary>
    public string SemanticActivationDefinitions { get; }
    /// <summary>Gets provider-owned semantic scope-directory authority.</summary>
    public string SemanticActivationScopes { get; }
    /// <summary>Gets provider-owned semantic live/retired/absence slots.</summary>
    public string SemanticActivationSlots { get; }
    /// <summary>Gets provider-owned durable activation payload and control rows.</summary>
    public string Activations { get; }
    /// <summary>Gets provider-owned durable executor-incarnation authority.</summary>
    public string Executors { get; }
    /// <summary>Gets provider-owned durable external-effect authority.</summary>
    public string ActivationEffects { get; }
    /// <summary>Gets provider-owned current schedule authority.</summary>
    public string ActivationSchedules { get; }
    /// <summary>Gets provider-owned immutable occurrence facts.</summary>
    public string ActivationOccurrences { get; }
    /// <summary>Gets durable cancel-previous maintenance table name.</summary>
    public string ActivationScheduleCancellations { get; }
    /// <summary>Gets durable activation-operation receipt table name.</summary>
    public string ActivationReceipts { get; }
    /// <summary>Gets the mutation journal scope index.</summary>
    public string MutationJournalScopeIndex { get; }
}
