using System.Buffers;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Immutable;
using HPD.Base;

namespace HPD.Auth.Base;

/// <summary>Emits the inert canonical authority projection used to review and freeze the HPD Auth Base graph.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AuthBaseGraphArtifact
{
    /// <summary>Creates canonical UTF-8 JSON for one finalized Auth graph.</summary>
    /// <param name="schema">The finalized provider-neutral logical schema.</param>
    /// <param name="authority">The finalized safe installed-definition authority.</param>
    /// <param name="storageProtection">The exact host-selected Auth storage requirement installed in the graph.</param>
    /// <returns>Owned UTF-8 bytes without a BOM and with one trailing line feed.</returns>
    public static byte[] Create(
        BaseLogicalSchema schema,
        HPDBaseStudioAuthoritySnapshot authority,
        AuthBaseModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.StorageProtectionRequirement);
        BaseStorageProtectionRequirement storageProtection = options.StorageProtectionRequirement;
        if (!string.Equals(schema.ApplicationId, authority.ApplicationId, StringComparison.Ordinal)
            || !string.Equals(storageProtection.OwningModuleId, "hpd.auth", StringComparison.Ordinal))
            throw new ArgumentException("The supplied authorities do not describe the HPD Auth graph.");
        ValidateDefinitionInventory(authority, schema, options.DataProtectionApplicationDiscriminatorDigest);

        JsonObject root = new()
        {
            ["applicationId"] = schema.ApplicationId,
            ["artifactVersion"] = 2,
            ["activations"] = Activations(),
            ["definitions"] = Definitions(authority),
            ["generationCells"] = GenerationCells(),
            ["grants"] = Grants(authority),
            ["logicalSchema"] = JsonSerializer.SerializeToNode(
                schema, HPDBaseJsonSerializerContext.Default.BaseLogicalSchema),
            ["logicalSchemaChecksum"] = schema.CanonicalChecksum,
            ["moduleMutations"] = ModuleMutations(),
            ["policies"] = Policies(authority),
            ["schedules"] = Schedules(options.DataProtectionApplicationDiscriminatorDigest),
            ["selectionProfiles"] = SelectionProfiles(),
            ["semanticActivations"] = SemanticActivations(),
            ["storageProtectionRequirement"] = Storage(storageProtection),
            ["acceptedSubjectRetirementPolicies"] = new JsonArray(),
            ["coordinatedRetirementCapabilities"] = RetirementCapabilities(schema),
            ["exportedSubjectContracts"] = SubjectContracts(schema),
            ["lifecycleExporterContracts"] = LifecycleContracts(schema),
        };
        NormalizeChecksums(root);
        ValidateGrantReferences(root, authority);

        byte[] checksumInput = WriteCanonical(root);
        root["applicationGraphChecksum"] = Convert.ToHexStringLower(SHA256.HashData(checksumInput));
        return WriteCanonical(root);
    }

    /// <summary>Requires committed artifact bytes to equal a fresh trusted projection exactly.</summary>
    /// <param name="committed">The committed UTF-8 artifact bytes.</param>
    /// <param name="schema">The finalized provider-neutral logical schema.</param>
    /// <param name="authority">The finalized safe installed-definition authority.</param>
    /// <param name="options">The exact Auth graph-finalization authority.</param>
    public static void Verify(
        ReadOnlySpan<byte> committed,
        BaseLogicalSchema schema,
        HPDBaseStudioAuthoritySnapshot authority,
        AuthBaseModuleOptions options)
    {
        byte[] expected = Create(schema, authority, options);
        if (!committed.SequenceEqual(expected))
            throw new InvalidOperationException("The committed HPD Auth Base graph artifact does not match the finalized authority.");
    }

    private static JsonArray SelectionProfiles() => Nodes(
        AuthSelectionProfiles.All.OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(value => JsonSerializer.SerializeToNode(
                value, HPDBaseJsonSerializerContext.Default.BaseSelectionOperationProfile)!));

    private static JsonArray ModuleMutations() => Nodes(ModuleDefinitions()
        .OrderBy(static value => value.Id, StringComparer.Ordinal)
        .Select(value => JsonSerializer.SerializeToNode(new AuthModuleMutationArtifact
        {
            Audience = value.Audience,
            Checksum = Convert.ToHexStringLower(value.Checksum.ToArray()),
            GenerationCellIds = value.GenerationCellIds,
            GrantId = value.GrantId,
            Id = value.Id,
            ImportedSubjectContractIds = value.ImportedSubjectContractIds,
            Limits = value.Limits,
            OwningModuleId = value.OwningModuleId,
            ReceiptPolicy = value.ReceiptPolicy,
            RequestTypeId = value.RequestTypeId,
            ResultTypeId = value.ResultTypeId,
            SystemCollectionIds = value.SystemCollectionIds,
            SystemSourceGrants = value.SystemSourceGrants,
            Version = value.Version,
        }, AuthBaseJsonSerializerContext.Default.AuthModuleMutationArtifact)!));

    private static JsonArray Activations() => Nodes(ActivationDefinitions()
        .OrderBy(static value => value.Id, StringComparer.Ordinal)
        .Select(value => JsonSerializer.SerializeToNode(
            value, AuthBaseJsonSerializerContext.Default.BaseActivationDefinition)!));

    private static JsonArray Schedules(BaseBinary discriminator) => Nodes(
        AuthScheduleDeclarations.Create(discriminator).Select(static value => value.Definition)
            .OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(value => JsonSerializer.SerializeToNode(
                value, HPDBaseJsonSerializerContext.Default.BaseScheduleDefinition)!));

    private static JsonArray SemanticActivations() => Nodes(SemanticDefinitions()
        .OrderBy(static value => value.Id, StringComparer.Ordinal)
        .Select(value => JsonSerializer.SerializeToNode(
            value, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationKeyDefinition)!));

    private static JsonArray GenerationCells() => Nodes(GenerationDefinitions().Select(value =>
        (JsonNode)new JsonObject
        {
            ["checksum"] = BaseModuleMutationContract.ComputeCellChecksum(value),
            ["id"] = value.Id,
            ["maximumCellsPerOperation"] = value.MaximumCellsPerOperation,
            ["maximumKeyUtf8Bytes"] = value.MaximumKeyUtf8Bytes,
            ["owningModuleId"] = value.OwningModuleId,
            ["scope"] = Wire(value.Scope),
            ["version"] = value.Version,
        }));

    private static JsonArray SubjectContracts(BaseLogicalSchema schema) => Nodes(
        schema.ExportedSubjects.OrderBy(static value => value.Id, StringComparer.Ordinal).Select(value =>
            (JsonNode)new JsonObject
            {
                ["acquisitionGrantId"] = SubjectGrant(value.Id, "acquire"),
                ["administrationGrantId"] = SubjectGrant(value.Id, "admin"),
                ["audiences"] = new JsonArray(value.Audiences.Select(item => JsonValue.Create(Wire(item))).ToArray()),
                ["checksum"] = value.Checksum,
                ["contractId"] = value.Id,
                ["contractVersion"] = value.Version,
                ["maximumSubjectIdUtf8Bytes"] = value.MaximumSubjectIdUtf8Bytes,
                ["owningModuleId"] = value.OwningModuleId,
                ["scope"] = Wire(value.Scope),
                ["subjectIdKind"] = Wire(value.SubjectIdKind),
                ["validationGrantId"] = SubjectGrant(value.Id, "validate"),
            }));

    private static JsonArray LifecycleContracts(BaseLogicalSchema schema) => Nodes(
        schema.ExportedSubjects.OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(value => LifecycleContract(value)));

    private static JsonNode LifecycleContract(BaseLogicalExportedSubject value)
    {
        bool user = value.Id == "hpd.auth.user-subject";
        var result = new JsonObject
        {
            ["acknowledgementGrantId"] = "base.subjectRetirement.acknowledge",
            ["activeFieldId"] = user ? "auth.users.isActive" : "auth.roles.isActive",
            ["checkpointGrantId"] = "base.subjectLifecycle.feed.checkpoint",
            ["contractChecksum"] = value.Checksum,
            ["contractId"] = value.Id,
            ["contractVersion"] = value.Version,
            ["feedReadGrantId"] = "base.subjectLifecycle.feed.read",
            ["finalPurgeGrantId"] = "base.subjectLifecycle.finalizeRetirement",
            ["privateCollectionId"] = user ? "auth.users" : "auth.roles",
            ["scopeFieldId"] = user ? "auth.users.tenantId" : "auth.roles.tenantId",
            ["stateDerivation"] = "requiredBooleanActiveAndTombstoneFieldsV1",
            ["tombstoneFieldId"] = user ? "auth.users.isDeleted" : "auth.roles.isDeleted",
            ["tombstoneGrantId"] = "base.subjectLifecycle.tombstone",
            ["validationPlanId"] = user
                ? "hpd.auth.user-subject.validation.v1"
                : "hpd.auth.role-subject.validation.v1",
        };
        result["bindingChecksum"] = Convert.ToHexStringLower(SHA256.HashData(WriteCanonical(result)));
        return result;
    }

    private static JsonArray RetirementCapabilities(BaseLogicalSchema schema) => Nodes(
        schema.ExportedSubjects.OrderBy(static value => value.Id, StringComparer.Ordinal).Select(value =>
            (JsonNode)new JsonObject
            {
                ["contractChecksum"] = value.Checksum,
                ["contractId"] = value.Id,
                ["contractVersion"] = value.Version,
                ["defaultCoordinationWindowMilliseconds"] = 86_400_000,
                ["defaultMinimumTombstoneAgeMilliseconds"] = value.Id == "hpd.auth.user-subject"
                    ? 2_592_000_000L : 0L,
                ["defaultTimeoutBehavior"] = "quarantine",
                ["supportsCoordinatedRetirement"] = true,
            }));

    private static string SubjectGrant(string contractId, string operation) => contractId switch
    {
        "hpd.auth.user-subject" => $"auth.subject.user.{operation}",
        "hpd.auth.role-subject" => $"auth.subject.role.{operation}",
        _ => throw new InvalidOperationException("The Auth graph contains an unknown exported subject."),
    };

    private static JsonArray Nodes(IEnumerable<JsonNode> values)
    {
        var result = new JsonArray();
        foreach (JsonNode value in values) result.Add(value);
        return result;
    }

    private static void NormalizeChecksums(JsonNode node)
    {
        if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
                if (child is not null) NormalizeChecksums(child);
            return;
        }
        if (node is not JsonObject obj) return;
        foreach (string key in obj.Select(static pair => pair.Key).ToArray())
        {
            JsonNode? child = obj[key];
            if (key.EndsWith("Checksum", StringComparison.OrdinalIgnoreCase)
                && child is JsonArray bytes && bytes.Count == 32
                && bytes.All(static item => item is JsonValue value
                    && value.TryGetValue(out int number) && number is >= 0 and <= 255))
            {
                obj[key] = Convert.ToHexStringLower(bytes.Select(static item => (byte)item!.GetValue<int>()).ToArray());
                continue;
            }
            if (child is not null) NormalizeChecksums(child);
        }
    }

    private static void ValidateGrantReferences(JsonNode node, HPDBaseStudioAuthoritySnapshot authority)
    {
        HashSet<string> installed = authority.Grants.Select(static value => value.Id)
            .ToHashSet(StringComparer.Ordinal);
        Visit(node);
        return;

        void Visit(JsonNode? current)
        {
            if (current is JsonArray array)
            {
                foreach (JsonNode? child in array) Visit(child);
                return;
            }
            if (current is not JsonObject obj) return;
            foreach ((string key, JsonNode? child) in obj)
            {
                if (key.EndsWith("GrantId", StringComparison.OrdinalIgnoreCase)
                    && child is JsonValue scalar && scalar.TryGetValue(out string? grant)
                    && !string.IsNullOrEmpty(grant) && !installed.Contains(grant))
                    throw new InvalidOperationException($"The graph references an uninstalled grant '{grant}'.");
                if (key.EndsWith("GrantIds", StringComparison.OrdinalIgnoreCase) && child is JsonArray grants)
                    foreach (JsonNode? item in grants)
                        if (item is JsonValue value && value.TryGetValue(out string? grantItem)
                            && !string.IsNullOrEmpty(grantItem) && !installed.Contains(grantItem))
                            throw new InvalidOperationException($"The graph references an uninstalled grant '{grantItem}'.");
                Visit(child);
            }
        }
    }

    private static JsonArray Definitions(HPDBaseStudioAuthoritySnapshot authority)
    {
        var result = new JsonArray();
        foreach (HPDBaseStudioDefinitionAuthority value in authority.Definitions)
            result.Add((JsonNode)new JsonObject
            {
                ["checksum"] = Convert.ToHexStringLower(value.DefinitionChecksum.AsSpan()),
                ["id"] = value.Id,
                ["kind"] = Wire(value.Kind),
                ["owningModuleId"] = value.OwningModuleId,
                ["version"] = value.Version,
            });
        return result;
    }

    private static void ValidateDefinitionInventory(
        HPDBaseStudioAuthoritySnapshot authority,
        BaseLogicalSchema schema,
        BaseBinary discriminator)
    {
        Validate(authority, HPDBaseStudioDefinitionKind.SelectionMutation,
            AuthSelectionProfiles.All.Zip(SelectionChecksums(), static (value, checksum) =>
                (value.Id, value.Version, value.ApplicationId, checksum)));
        Validate(authority, HPDBaseStudioDefinitionKind.ModuleMutation,
            ModuleDefinitions().Select(static value =>
                (value.Id, value.Version, value.OwningModuleId, Convert.ToHexStringLower(value.Checksum.ToArray()))));
        Validate(authority, HPDBaseStudioDefinitionKind.Activation,
            ActivationDefinitions().Select(static value =>
                (value.Id, value.Version, value.OwningModuleId, Convert.ToHexStringLower(value.Checksum.AsSpan()))));
        Validate(authority, HPDBaseStudioDefinitionKind.Schedule,
            AuthScheduleDeclarations.Create(discriminator).Select(static registration => registration.Definition)
                .Select(static value => (value.Id, value.Version, value.OwningModuleId,
                    Convert.ToHexStringLower(value.Checksum.AsSpan()))));
        Validate(authority, HPDBaseStudioDefinitionKind.SemanticActivation,
            SemanticDefinitions().Select(static value =>
                (value.Id, value.Version, value.OwningModuleId, Convert.ToHexStringLower(value.Checksum.AsSpan()))));
        string[] installedReads = authority.Definitions
            .Where(static value => value.Kind == HPDBaseStudioDefinitionKind.RegisteredRead)
            .Select(static value => value.Id).Order(StringComparer.Ordinal).ToArray();
        if (!installedReads.SequenceEqual(schema.ReadDefinitions.Select(static value => value.Id)
                .Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException("The finalized registered-read inventory is inconsistent.");
    }

    private static void Validate(
        HPDBaseStudioAuthoritySnapshot authority,
        HPDBaseStudioDefinitionKind kind,
        IEnumerable<(string Id, int Version, string Owner, string Checksum)> expected)
    {
        var actual = authority.Definitions.Where(value => value.Kind == kind)
            .OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        var values = expected.OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        if (actual.Length != values.Length) throw new InvalidOperationException("The finalized definition inventory is incomplete.");
        for (int index = 0; index < actual.Length; index++)
            if (actual[index].Id != values[index].Id || actual[index].Version != values[index].Version
                || actual[index].OwningModuleId != values[index].Owner
                || !Convert.ToHexStringLower(actual[index].DefinitionChecksum.AsSpan()).Equals(
                    values[index].Checksum, StringComparison.Ordinal))
                throw new InvalidOperationException("The finalized definition authority does not match its source declaration.");
    }

    private static string[] SelectionChecksums() =>
    [
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.SessionsRevokeUser),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.SessionsExpireDue),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.RefreshTokensRevokeUser),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.SessionsDeleteUser),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.RefreshTokensDeleteUser),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.RefreshTokensDeleteExpired),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.RefreshTokenDeliveriesDeleteExpired),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.PasskeysDeleteUser),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.UserClaimsDeleteUser),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.UserLoginsDeleteUser),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.UserTokensDeleteUser),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.UserRolesDeleteUser),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.UserIdentitiesDeleteUser),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.RoleClaimsDeleteRole),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.UserRolesDeleteRole),
        BaseGeneratedGraphEvidence.SelectionProfile(AuthSelectionProfiles.MaintenanceRunsDeleteExpired),
    ];

    private static JsonArray Grants(HPDBaseStudioAuthoritySnapshot authority)
    {
        var result = new JsonArray();
        foreach (HPDBaseStudioGrantAuthority value in authority.Grants)
            result.Add((JsonNode)new JsonObject
            {
                ["checksum"] = Convert.ToHexStringLower(value.GetChecksum()),
                ["hasStaticSemantics"] = value.HasStaticSemantics,
                ["id"] = value.Id,
                ["owningModuleId"] = value.OwningModuleId,
                ["sourceContractId"] = value.SourceContractId,
                ["sourceContractVersion"] = value.SourceContractVersion,
                ["staticSemantics"] = value.GetStaticGrant() is { } grant
                    ? JsonSerializer.SerializeToNode(grant, HPDBaseJsonSerializerContext.Default.AccessGrant)
                    : null,
                ["version"] = value.Version,
            });
        return result;
    }

    private static JsonArray Policies(HPDBaseStudioAuthoritySnapshot authority)
    {
        var result = new JsonArray();
        foreach (HPDBaseStudioPolicyAuthority value in authority.Policies)
            result.Add((JsonNode)new JsonObject
            {
                ["checksum"] = Convert.ToHexStringLower(value.RegistrationChecksum.AsSpan()),
                ["compositionOrder"] = value.CompositionOrder,
                ["evaluatorContractId"] = value.EvaluatorContractId,
                ["evaluatorContractVersion"] = value.EvaluatorContractVersion,
                ["id"] = value.Id,
                ["owningModuleId"] = value.OwningModuleId,
                ["version"] = value.Version,
            });
        return result;
    }

    private static BaseRegisteredModuleMutationDefinition[] ModuleDefinitions() =>
    [
        AuthCreateUserOperationV1.Definition, AuthUpdateUserProfileOperationV1.Definition,
        AuthCreateRoleOperationV1.Definition, AuthRenameRoleOperationV1.Definition,
        AuthMembershipAddOperationV1.Definition, AuthMembershipRemoveOperationV1.Definition,
        AuthLoginLinkOperationV1.Definition, AuthLoginUnlinkOperationV1.Definition,
        AuthChangePasswordOperationV1.Definition, AuthResetPasswordOperationV1.Definition,
        AuthSetSecurityStateOperationV1.Definition, AuthAuditAppendOperationV1.Definition,
        AuthPasskeyRecordAssertionOperationV1.Definition, AuthSessionCreateOperationV1.Definition,
        AuthSessionTouchOperationV1.Definition, AuthRefreshIssueOperationV1.Definition,
        AuthRefreshRotateOperationV1.Definition, AuthRecoveryCodeConsumeOperationV1.Definition,
        AuthRecoveryCodesReplaceOperationV1.Definition, AuthPasskeyRegisterOperationV1.Definition,
        AuthMaintenanceRunInitializeOperationV1.Definition, AuthCleanupReconcileCursorOperationV1.Definition,
        AuthCleanupAdvanceOperationV1.Definition, AuthCleanupPrepareRetirementOperationV1.Definition,
        AuthUserCleanupInitializeOperationV1.Definition, AuthRoleCleanupInitializeOperationV1.Definition,
        AuthUserCleanupRetireOperationV1.Definition, AuthRoleCleanupRetireOperationV1.Definition,
    ];

    private static BaseActivationDefinition[] ActivationDefinitions() =>
    [
        AuthCleanupActivationDeclarations.User.Definition,
        AuthCleanupActivationDeclarations.Role.Definition,
        AuthLifecycleActivationDeclarations.BootstrapUser.Definition,
        AuthLifecycleActivationDeclarations.BootstrapRole.Definition,
        AuthLifecycleActivationDeclarations.RetireUser.Definition,
        AuthLifecycleActivationDeclarations.RetireRole.Definition,
        AuthLifecycleActivationDeclarations.Reconcile.Definition,
        AuthLifecycleActivationDeclarations.Sessions.Definition,
        AuthLifecycleActivationDeclarations.RefreshTokens.Definition,
        AuthLifecycleActivationDeclarations.Deliveries.Definition,
        AuthLifecycleActivationDeclarations.DataProtection.Definition,
    ];

    private static BaseSemanticActivationKeyDefinition[] SemanticDefinitions() =>
    [AuthCleanupSemanticActivations.User.Definition, AuthCleanupSemanticActivations.Role.Definition];

    private static BaseModuleGenerationCellDefinition[] GenerationDefinitions() =>
    [
        Generation("hpd.auth.membership-generation.v1"),
        Generation("hpd.auth.role-state-generation.v1"),
        Generation("hpd.auth.tenant-policy-generation.v1"),
        Generation("hpd.auth.user-security-generation.v1"),
        Generation("hpd.auth.user-state-generation.v1"),
    ];

    private static BaseModuleGenerationCellDefinition Generation(string id) => new()
    {
        Id = id,
        Version = 1,
        OwningModuleId = "hpd.auth",
        Scope = BaseModuleGenerationScope.TenantAndKey,
        MaximumKeyUtf8Bytes = 36,
        MaximumCellsPerOperation = 1,
    };

    private static JsonObject Storage(BaseStorageProtectionRequirement value) => new()
    {
        ["coverage"] = new JsonObject
        {
            ["administrativeExports"] = EnumArray(value.Coverage.AdministrativeExports),
            ["authoritativeBackups"] = EnumArray(value.Coverage.AuthoritativeBackups),
            ["authoritativeRecords"] = EnumArray(value.Coverage.AuthoritativeRecords),
            ["externalFilesAndBlobs"] = EnumArray(value.Coverage.ExternalFilesAndBlobs),
            ["indexes"] = EnumArray(value.Coverage.Indexes),
            ["journal"] = EnumArray(value.Coverage.Journal),
            ["ordinaryExports"] = EnumArray(value.Coverage.OrdinaryExports),
            ["providerState"] = EnumArray(value.Coverage.ProviderState),
            ["receipts"] = EnumArray(value.Coverage.Receipts),
            ["temporaryFiles"] = EnumArray(value.Coverage.TemporaryFiles),
        },
        ["minimumVerification"] = Wire(value.MinimumVerification),
        ["owningModuleId"] = value.OwningModuleId,
        ["permittedGuarantees"] = EnumArray(value.PermittedGuarantees),
        ["permittedKeyOwners"] = EnumArray(value.PermittedKeyOwners),
        ["requiredRotation"] = Wire(value.RequiredRotation),
    };

    private static JsonArray EnumArray<T>(IEnumerable<T> values) where T : struct, Enum =>
        new(values.Select(static value => JsonValue.Create(Wire(value))).ToArray());

    private static string Wire<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static byte[] WriteCanonical(JsonNode node)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            WriteNode(writer, node);
        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                return;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach ((string key, JsonNode? value) in obj.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    WriteNode(writer, value);
                }
                writer.WriteEndObject();
                return;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (JsonNode? value in array) WriteNode(writer, value);
                writer.WriteEndArray();
                return;
            default:
                node.WriteTo(writer);
                return;
        }
    }
}

internal sealed record AuthModuleMutationArtifact
{
    public required BaseModuleMutationAudience Audience { get; init; }
    public required string Checksum { get; init; }
    public required ImmutableArray<string> GenerationCellIds { get; init; }
    public required string GrantId { get; init; }
    public required string Id { get; init; }
    public required ImmutableArray<string> ImportedSubjectContractIds { get; init; }
    public required BaseModuleMutationLimits Limits { get; init; }
    public required string OwningModuleId { get; init; }
    public required BaseModuleMutationReceiptPolicy ReceiptPolicy { get; init; }
    public required string RequestTypeId { get; init; }
    public required string ResultTypeId { get; init; }
    public required ImmutableArray<string> SystemCollectionIds { get; init; }
    public required ImmutableArray<BaseModuleSystemSourceGrant> SystemSourceGrants { get; init; }
    public required int Version { get; init; }
}
