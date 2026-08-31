using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using System.Text;
using HPD.Auth.Base;
using HPD.Base;
using Xunit;

namespace HPD.Auth.Base.ContractTests;

/// <summary>
/// Proves that the committed Auth graph contains the complete closed L3 operation
/// inventory rather than a count-compatible or partially wired substitute.
/// </summary>
public sealed class AuthOperationGraphInventoryTests
{
    private static readonly IReadOnlyDictionary<string, string> ModuleMutationAuthoritySnapshots =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hpd.auth.audit.append.v1"] = "02c8fec41780367c572ca9035eacb2ab5e5dd9939c2d02d6d9316f02dff663ad",
            ["hpd.auth.cleanup.advance.v1"] = "7d75ecd7dc34889c3ce4293e1d9d8014568467a8234e7f9665de9f6dc7f0334d",
            ["hpd.auth.cleanup.initialize-role.v1"] = "27bf9ebb513427c2cff75c2454489caefdea20b451476180cf170ab73246ff6f",
            ["hpd.auth.cleanup.initialize-user.v1"] = "f4a4f21e78f92cd0741cf006741641d00aa812c492e58a8968ea2ff1446fb20a",
            ["hpd.auth.cleanup.prepare-retirement.v1"] = "410b8d368be95e75a4ba67661d2aec65da2f95a1a03d823e0162ec8b3bc9ab2e",
            ["hpd.auth.cleanup.reconcile-cursor.v1"] = "b9ae3aa62e3bc9c7972fbfb43976d377e9af10f1003eae31ed234a8e420681ce",
            ["hpd.auth.cleanup.retire-role.v1"] = "6570e76a511abd3b66fbdfeceebd041497f19924dfa18421a0f27047b7b0701e",
            ["hpd.auth.cleanup.retire-user.v1"] = "c74236180e702c84580f31da6b43556a9d5435de62f8320dfe2f48c4b216267a",
            ["hpd.auth.login.link.v1"] = "816c905281b0c609fd2484bbe07f9f99fc89c44ff86a745f8d53f77d324320ea",
            ["hpd.auth.login.unlink.v1"] = "cb4c526689538c51eb31c6cee3dc767686168d761a71e4bd30d04be06a5b4274",
            ["hpd.auth.maintenance-run.initialize.v1"] = "4a9e3b99da48f565c1ff3e28e6518156c9bd971051842a8591debb5b4212d91c",
            ["hpd.auth.membership.add.v1"] = "a5ae4b74862ba5d230fd02b9bdac5d3dd8882b2efffb0ef0b98b3147d0653fcc",
            ["hpd.auth.membership.remove.v1"] = "5aef0bf87a714734aa4c30c746657c449ff6705fae58765cdd1dda77ea677e78",
            ["hpd.auth.passkey.record-assertion.v1"] = "591a95cba14221e780101c5095cbb5589dda1b85caac40b9a6a125cf7e891a55",
            ["hpd.auth.passkey.register.v1"] = "3414132a698f65911692e1c2127b99abc111ff50637c68528ca6b6441916d2b5",
            ["hpd.auth.passkey.remove.v1"] = "ce5bbc47a3b423c6e84c411eaeb059552dc2418ec26a900fa3a2e8603493de26",
            ["hpd.auth.recovery-code.consume.v1"] = "dd97dd2c4ab7754d1c48dccc67a0d0fc8f50a9c42371846e76c3be3a43cdca4b",
            ["hpd.auth.recovery-codes.replace.v1"] = "99ce6a3a5b3106fef8b02c8f3c511a2275ad6606d1a8566cd5c13d01dd26c1dc",
            ["hpd.auth.refresh.issue.v1"] = "ca8390d9ef3b02a9406ec1ba73425a5917a5579f787152a3843a063cceab8443",
            ["hpd.auth.refresh.rotate.v1"] = "5f0e82a25912f8ac8ae8643293e8e61ba3356f3f2acfda2ab852f8a6bdcddad0",
            ["hpd.auth.role.create.v1"] = "93f8ddf8d173fa5acffb30402e9970b2b9a8ba3c3011f862f7e4aec3095da1e4",
            ["hpd.auth.role.rename.v1"] = "85d5f1ae5d2a13442dbf1824d1c1e2eb14d951f183c13699219a581c716f0e3b",
            ["hpd.auth.session.create.v1"] = "df863e826479361d6c3a5d9e1546460b9823ea250d0174e4e2c36c6404d88661",
            ["hpd.auth.session.touch.v1"] = "b11540917a1fdb8f3147032c7ad02fa475f5f1c914e1ee8819a19456bb8541b1",
            ["hpd.auth.user.change-password.v1"] = "788ee091f01e65921fb902aac666fc4316be673906992c579105700f77320149",
            ["hpd.auth.user.create.v1"] = "7b9318a94d75a3e193610b5c9c6b2786650b6100186559b1c974ef18d639fe1a",
            ["hpd.auth.user.remove-password.v1"] = "97dd9dd71552e5cddfc48dedf724e2fe04f6150871e466742eb917a1b9ff81c8",
            ["hpd.auth.user.reset-password.v1"] = "e3be52206fb84616f465c26d85e5549c60377988bdeebaeacf7094ebb8b03cb5",
            ["hpd.auth.user.set-security-state.v1"] = "19d014b9fbaef58b2988cc7f5421b4417ee470e51d92da9390ccfb999e8c2270",
            ["hpd.auth.user.update-profile.v1"] = "ee52708c81e3a8136210429e1ecf2f0c817408a41b6e3add24d5668122444a50",
        };

    private static readonly string[] ModuleMutationIds =
    [
        "hpd.auth.audit.append.v1", "hpd.auth.cleanup.advance.v1",
        "hpd.auth.cleanup.initialize-role.v1", "hpd.auth.cleanup.initialize-user.v1",
        "hpd.auth.cleanup.prepare-retirement.v1", "hpd.auth.cleanup.reconcile-cursor.v1",
        "hpd.auth.cleanup.retire-role.v1", "hpd.auth.cleanup.retire-user.v1",
        "hpd.auth.login.link.v1", "hpd.auth.login.unlink.v1",
        "hpd.auth.maintenance-run.initialize.v1", "hpd.auth.membership.add.v1",
        "hpd.auth.membership.remove.v1", "hpd.auth.passkey.record-assertion.v1",
        "hpd.auth.passkey.register.v1", "hpd.auth.passkey.remove.v1",
        "hpd.auth.recovery-code.consume.v1", "hpd.auth.recovery-codes.replace.v1",
        "hpd.auth.refresh.issue.v1", "hpd.auth.refresh.rotate.v1",
        "hpd.auth.role.create.v1", "hpd.auth.role.rename.v1",
        "hpd.auth.session.create.v1", "hpd.auth.session.touch.v1",
        "hpd.auth.user.change-password.v1", "hpd.auth.user.create.v1",
        "hpd.auth.user.remove-password.v1", "hpd.auth.user.reset-password.v1",
        "hpd.auth.user.set-security-state.v1", "hpd.auth.user.update-profile.v1",
    ];

    private static readonly string[] SelectionProfileIds =
    [
        "auth.maintenanceRuns.delete-expired.v1", "auth.passkeys.delete-user.v1",
        "auth.refreshTokenDeliveries.delete-expired.v1", "auth.refreshTokens.delete-expired.v1",
        "auth.refreshTokens.delete-user.v1", "auth.refreshTokens.revoke-user.v1",
        "auth.roleClaims.delete-role.v1", "auth.sessions.delete-user.v1",
        "auth.sessions.expire-due.v1", "auth.sessions.revoke-user.v1",
        "auth.userClaims.delete-user.v1", "auth.userIdentities.delete-user.v1",
        "auth.userLogins.delete-user.v1", "auth.userRoles.delete-role.v1",
        "auth.userRoles.delete-user.v1", "auth.userTokens.delete-user.v1",
    ];

    private static readonly string[] ActivationIds =
    [
        "hpd.auth.cleanup.bootstrap.role.v1", "hpd.auth.cleanup.bootstrap.user.v1",
        "hpd.auth.cleanup.reconcile.v1", "hpd.auth.cleanup.role.v1",
        "hpd.auth.cleanup.semantic-retire.role.v1",
        "hpd.auth.cleanup.semantic-retire.user.v1", "hpd.auth.cleanup.user.v1",
        "hpd.auth.data-protection.refresh.v1", "hpd.auth.expiration.deliveries.v1",
        "hpd.auth.expiration.refresh-tokens.v1", "hpd.auth.expiration.sessions.v1",
    ];

    private static readonly string[] ScheduleIds =
    [
        "hpd.auth.schedule.cleanup-reconcile.v1",
        "hpd.auth.schedule.data-protection-refresh.v1",
        "hpd.auth.schedule.delivery-expiration.v1",
        "hpd.auth.schedule.refresh-expiration.v1",
        "hpd.auth.schedule.session-expiration.v1",
    ];

    [Fact]
    public void CommittedGraphContainsTheExactClosedOperationInventory()
    {
        using JsonDocument graph = LoadGraph();
        JsonElement root = graph.RootElement;

        AssertIds(root, "moduleMutations", ModuleMutationIds);
        AssertIds(root, "selectionProfiles", SelectionProfileIds);
        AssertIds(root, "activations", ActivationIds);
        AssertIds(root, "schedules", ScheduleIds);
        AssertIds(root, "semanticActivations",
        [
            "hpd.auth.semantic.cleanup.role.v1",
            "hpd.auth.semantic.cleanup.user.v1",
        ]);

        string json = root.GetRawText();
        Assert.DoesNotContain("TBD", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runtime-computed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cleanupJobs", json, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryClosedDefinitionHasExactIdentityChecksumAndReceiptAuthority()
    {
        using JsonDocument graph = LoadGraph();
        JsonElement root = graph.RootElement;

        foreach (string family in new[]
                 {
                     "moduleMutations", "selectionProfiles", "activations", "schedules",
                     "semanticActivations",
                 })
        {
            foreach (JsonElement definition in root.GetProperty(family).EnumerateArray())
            {
                Assert.False(string.IsNullOrWhiteSpace(definition.GetProperty("id").GetString()));
                Assert.Equal(1, definition.GetProperty("version").GetInt32());
                if (definition.TryGetProperty("checksum", out JsonElement checksum))
                    AssertChecksum(checksum.GetString());
            }
        }

        JsonElement[] installedDefinitions = root.GetProperty("definitions")
            .EnumerateArray().ToArray();
        foreach (string profileId in SelectionProfileIds)
        {
            JsonElement authority = Assert.Single(installedDefinitions,
                value => value.GetProperty("id").GetString() == profileId);
            AssertChecksum(authority.GetProperty("checksum").GetString());
        }

        foreach (JsonElement operation in root.GetProperty("moduleMutations").EnumerateArray())
        {
            Assert.Equal("hpd.auth", operation.GetProperty("owningModuleId").GetString());
            Assert.StartsWith("auth.operation.", operation.GetProperty("grantId").GetString(),
                StringComparison.Ordinal);
            Assert.StartsWith("hpd.auth.type.", operation.GetProperty("requestTypeId").GetString(),
                StringComparison.Ordinal);
            Assert.StartsWith("hpd.auth.type.", operation.GetProperty("resultTypeId").GetString(),
                StringComparison.Ordinal);
            Assert.Equal(1, operation.GetProperty("receiptPolicy")
                .GetProperty("formatVersion").GetInt32());
            Assert.Equal("1.00:00:00", operation.GetProperty("receiptPolicy")
                .GetProperty("lifetime").GetString());

            string[] collections = operation.GetProperty("systemCollectionIds")
                .EnumerateArray().Select(static value => value.GetString()!).ToArray();
            foreach (JsonElement source in operation.GetProperty("systemSourceGrants")
                         .EnumerateArray())
            {
                Assert.Contains(source.GetProperty("collectionId").GetString(), collections,
                    StringComparer.Ordinal);
            }
        }
    }

    [Fact]
    public void EveryModuleMutationGrantAndCollectionSourceGrantIsIndependentlyInstalled()
    {
        using JsonDocument graph = LoadGraph();
        JsonElement root = graph.RootElement;
        Dictionary<string, JsonElement> grants = root.GetProperty("grants")
            .EnumerateArray()
            .ToDictionary(static value => value.GetProperty("id").GetString()!, StringComparer.Ordinal);

        foreach (JsonElement operation in root.GetProperty("moduleMutations").EnumerateArray())
        {
            AssertInstalledGrant(operation.GetProperty("grantId").GetString()!, grants);

            string[] collections = operation.GetProperty("systemCollectionIds")
                .EnumerateArray().Select(static value => value.GetString()!).ToArray();
            JsonElement[] sources = operation.GetProperty("systemSourceGrants")
                .EnumerateArray().ToArray();
            Assert.Equal(collections.Length, sources.Length);
            Assert.Equal(collections, sources.Select(static source =>
                source.GetProperty("collectionId").GetString()).ToArray());
            foreach (JsonElement source in sources)
                AssertInstalledGrant(source.GetProperty("grantId").GetString()!, grants);
        }

        static void AssertInstalledGrant(
            string grantId,
            IReadOnlyDictionary<string, JsonElement> grants)
        {
            Assert.True(grants.TryGetValue(grantId, out JsonElement grant), grantId);
            Assert.Equal(1, grant.GetProperty("version").GetInt32());
            Assert.Equal("hpd.auth", grant.GetProperty("owningModuleId").GetString());
            Assert.Equal("hpd.auth.grants", grant.GetProperty("sourceContractId").GetString());
            Assert.Equal(1, grant.GetProperty("sourceContractVersion").GetInt32());
            AssertChecksum(grant.GetProperty("checksum").GetString());
        }
    }

    [Fact]
    public void EveryModuleMutationAuthorityProjectionMatchesItsCommittedSnapshot()
    {
        using JsonDocument graph = LoadGraph();
        JsonElement[] operations = graph.RootElement.GetProperty("moduleMutations")
            .EnumerateArray().ToArray();

        Assert.Equal(ModuleMutationAuthoritySnapshots.Count, operations.Length);
        foreach (JsonElement operation in operations)
        {
            string id = operation.GetProperty("id").GetString()!;
            Assert.True(ModuleMutationAuthoritySnapshots.TryGetValue(id, out string? expected), id);
            byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(operation);
            string actual = Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
            Assert.True(string.Equals(expected, actual, StringComparison.Ordinal),
                $"{id}: expected {expected}, actual {actual}");
        }
    }

    [Fact]
    public void EveryModuleMutationLimitIsAdmittedExactlyAndMaximumPlusOneIsProviderRejected()
    {
        using JsonDocument graph = LoadGraph();
        MethodInfo supports = typeof(BaseModuleMutationPlatform).Assembly
            .GetType("HPD.Base.BaseModuleMutationCapabilityContract", throwOnError: true)!
            .GetMethod("Supports", BindingFlags.Static | BindingFlags.NonPublic)!;

        foreach (JsonElement operation in graph.RootElement.GetProperty("moduleMutations").EnumerateArray())
        {
            string id = operation.GetProperty("id").GetString()!;
            BaseModuleMutationLimits required = JsonSerializer.Deserialize<BaseModuleMutationLimits>(
                operation.GetProperty("limits").GetRawText(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            BaseModuleMutationCapability exact = Capability(required);
            Assert.True(InvokeSupports(supports, required, exact), id);

            foreach ((string member, long amount) in LimitValues(required))
            {
                Assert.True(amount > 0, $"{id}:{member}");
                BaseModuleMutationLimits insufficient = WithLimit(required, member, amount - 1);
                Assert.False(InvokeSupports(supports, required, Capability(insufficient)),
                    $"{id}:{member} admitted a request one unit beyond provider authority");
            }
        }
    }

    [Fact]
    public void EveryModuleMutationUsesTheClosedReceiptIdentityFingerprintAndIndeterminatePolicy()
    {
        MethodInfo definitionsMethod = typeof(AuthBaseGraphArtifact).GetMethod(
            "ModuleDefinitions", BindingFlags.Static | BindingFlags.NonPublic)!;
        BaseRegisteredModuleMutationDefinition[] definitions =
            (BaseRegisteredModuleMutationDefinition[])definitionsMethod.Invoke(null, null)!;
        Type runtime = typeof(BaseModuleMutationPlatform).Assembly.GetType(
            "HPD.Base.DefaultBaseModuleMutationRuntime", throwOnError: true)!;
        MethodInfo bind = runtime.GetMethod(
            "BindReceiptIdentity", BindingFlags.Static | BindingFlags.NonPublic)!;
        BaseMutationRequestFingerprint fingerprint = BaseMutationRequestFingerprint.Create(
            SHA256.HashData("hpd.auth.receipt-policy.snapshot.v1"u8));
        BaseMutationRequestIdentity local = BaseMutationRequestIdentity.Create(
            "auth.test.scope", "auth.test.operation", "auth-test-idempotency", fingerprint);

        foreach (BaseRegisteredModuleMutationDefinition definition in definitions)
        {
            var bound = (BaseMutationRequestIdentity)bind.Invoke(
                null, [local, definition, "hpd.auth.identity.v1", "auth-store-v1"])!;
            Assert.Equal(ExpectedReceiptScope(
                local, definition, "hpd.auth.identity.v1", "auth-store-v1"), bound.Scope);
            Assert.Equal(definition.Id, bound.Operation);
            Assert.Equal(local.IdempotencyKey, bound.IdempotencyKey);
            Assert.Equal(local.Fingerprint, bound.Fingerprint);
            Assert.Equal(1, definition.ReceiptPolicy.FormatVersion);
            Assert.Equal(TimeSpan.FromDays(1), definition.ReceiptPolicy.Lifetime);
        }

        Type errors = typeof(BaseModuleMutationPlatform).Assembly.GetType(
            "HPD.Base.BaseModuleMutationErrorCodes", throwOnError: true)!;
        string[] admittedIndeterminate = errors.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(static field => field.FieldType == typeof(string))
            .Select(static field => (string)field.GetRawConstantValue()!)
            .Where(static value => value.Contains("indeterminate", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["base.moduleMutation.commitIndeterminate"], admittedIndeterminate);
        Assert.Equal("base.runtime.request.fingerprintConflict",
            BaseMutationRequestErrorCodes.FingerprintConflict);
    }

    [Fact]
    public void SemanticCleanupDefinitionsBindTheExactOperationsSubjectsAndActivations()
    {
        using JsonDocument graph = LoadGraph();
        JsonElement[] definitions = graph.RootElement.GetProperty("semanticActivations")
            .EnumerateArray().ToArray();

        AssertSemantic(definitions, "role", "hpd.auth.role-subject");
        AssertSemantic(definitions, "user", "hpd.auth.user-subject");
    }

    private static void AssertSemantic(
        JsonElement[] definitions,
        string kind,
        string subjectContractId)
    {
        JsonElement definition = Assert.Single(definitions,
            value => value.GetProperty("id").GetString()
                == $"hpd.auth.semantic.cleanup.{kind}.v1");
        Assert.Equal($"hpd.auth.cleanup.{kind}.v1",
            definition.GetProperty("activation").GetProperty("id").GetString());
        Assert.Equal($"hpd.auth.cleanup.initialize-{kind}.v1",
            definition.GetProperty("ensureOperation").GetProperty("operationId").GetString());
        Assert.Equal($"hpd.auth.cleanup.retire-{kind}.v1",
            definition.GetProperty("retirementOperation").GetProperty("operationId").GetString());
        Assert.Equal(subjectContractId, definition.GetProperty("compaction")
            .GetProperty("subjectContract").GetProperty("contractId").GetString());
        Assert.Equal($"auth.cleanup.{kind}.subject.v1", definition.GetProperty("compaction")
            .GetProperty("subjectReferenceRequestPropertyId").GetString());
        AssertChecksum(definition.GetProperty("requestSerializerChecksum").GetString());
        AssertChecksum(definition.GetProperty("keyExpressionChecksum").GetString());
        AssertChecksum(definition.GetProperty("ensureOperation")
            .GetProperty("operationChecksum").GetString());
        AssertChecksum(definition.GetProperty("retirementOperation")
            .GetProperty("operationChecksum").GetString());
    }

    private static void AssertIds(JsonElement root, string property, string[] expected)
    {
        string[] actual = root.GetProperty(property).EnumerateArray()
            .Select(static value => value.GetProperty("id").GetString()!)
            .ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(actual.Length, actual.Distinct(StringComparer.Ordinal).Count());
    }

    private static void AssertChecksum(string? checksum)
    {
        Assert.NotNull(checksum);
        Assert.Equal(64, checksum.Length);
        Assert.All(checksum, static value => Assert.True(
            value is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }

    private static BaseModuleMutationCapability Capability(BaseModuleMutationLimits limits) => new()
    {
        Supported = true,
        SerializableExecution = true,
        DurableReceipts = true,
        GenerationCells = true,
        AtomicRecordAndGenerationCommit = true,
        MaximumRemovedFieldsPerMutation = BaseModuleMutationPlatform.MaximumLimits.MaximumRemovedFields,
        MaximumLimits = limits,
    };

    private static string ExpectedReceiptScope(
        BaseMutationRequestIdentity identity,
        BaseRegisteredModuleMutationDefinition definition,
        string applicationId,
        string logicalStoreId)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "base.moduleMutation.receiptAuthority.v1"u8);
        Append(hash, Encoding.UTF8.GetBytes(applicationId));
        Append(hash, Encoding.UTF8.GetBytes(logicalStoreId));
        Append(hash, Encoding.UTF8.GetBytes(definition.Id));
        Span<byte> version = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(version, definition.Version);
        Append(hash, version);
        Append(hash, definition.Checksum.ToArray());
        Append(hash, Encoding.UTF8.GetBytes(identity.Scope));
        Append(hash, Encoding.UTF8.GetBytes(identity.Operation));
        return Convert.ToHexStringLower(hash.GetHashAndReset());

        static void Append(IncrementalHash target, ReadOnlySpan<byte> value)
        {
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
            target.AppendData(length);
            target.AppendData(value);
        }
    }

    private static bool InvokeSupports(
        MethodInfo supports,
        BaseModuleMutationLimits required,
        BaseModuleMutationCapability capability) =>
        (bool)supports.Invoke(null, [required, capability])!;

    private static IEnumerable<(string Member, long Amount)> LimitValues(BaseModuleMutationLimits value)
    {
        yield return (nameof(value.MaximumCaptures), value.MaximumCaptures);
        yield return (nameof(value.MaximumRecordCaptures), value.MaximumRecordCaptures);
        yield return (nameof(value.MaximumRelationTargetCaptures), value.MaximumRelationTargetCaptures);
        yield return (nameof(value.MaximumGenerationCaptures), value.MaximumGenerationCaptures);
        yield return (nameof(value.MaximumRecordMutations), value.MaximumRecordMutations);
        yield return (nameof(value.MaximumGenerationReads), value.MaximumGenerationReads);
        yield return (nameof(value.MaximumGenerationComparisons), value.MaximumGenerationComparisons);
        yield return (nameof(value.MaximumGenerationIncrements), value.MaximumGenerationIncrements);
        yield return (nameof(value.MaximumGuardNodes), value.MaximumGuardNodes);
        yield return (nameof(value.MaximumGuardDepth), value.MaximumGuardDepth);
        yield return (nameof(value.MaximumStatements), value.MaximumStatements);
        yield return (nameof(value.MaximumBranches), value.MaximumBranches);
        yield return (nameof(value.MaximumExpressionNodes), value.MaximumExpressionNodes);
        yield return (nameof(value.MaximumPreconditions), value.MaximumPreconditions);
        yield return (nameof(value.MaximumRequestGuardEvaluations), value.MaximumRequestGuardEvaluations);
        yield return (nameof(value.MaximumStaticSetMembers), value.MaximumStaticSetMembers);
        yield return (nameof(value.MaximumStaticSetComparisons), value.MaximumStaticSetComparisons);
        yield return (nameof(value.MaximumDisabledCaptures), value.MaximumDisabledCaptures);
        yield return (nameof(value.MaximumRemovedFields), value.MaximumRemovedFields);
        yield return (nameof(value.MaximumReadIntervals), value.MaximumReadIntervals);
        yield return (nameof(value.MaximumSubjectValidations), value.MaximumSubjectValidations);
        yield return (nameof(value.MaximumAuthorityReads), value.MaximumAuthorityReads);
        yield return (nameof(value.MaximumRelationChecks), value.MaximumRelationChecks);
        yield return (nameof(value.MaximumUniqueConstraintChecks), value.MaximumUniqueConstraintChecks);
        yield return (nameof(value.MaximumRequestBytes), value.MaximumRequestBytes);
        yield return (nameof(value.MaximumSelectedBytes), value.MaximumSelectedBytes);
        yield return (nameof(value.MaximumGenerationBytes), value.MaximumGenerationBytes);
        yield return (nameof(value.MaximumEvidenceBytes), value.MaximumEvidenceBytes);
        yield return (nameof(value.MaximumWrittenBytes), value.MaximumWrittenBytes);
        yield return (nameof(value.MaximumFactBytes), value.MaximumFactBytes);
        yield return (nameof(value.MaximumJournalBytes), value.MaximumJournalBytes);
        yield return (nameof(value.MaximumReceiptBytes), value.MaximumReceiptBytes);
        yield return (nameof(value.MaximumResultBytes), value.MaximumResultBytes);
        yield return (nameof(value.MaximumTransientBytes), value.MaximumTransientBytes);
        yield return (nameof(value.Deadlines.AcquisitionTimeout), value.Deadlines.AcquisitionTimeout.Ticks);
        yield return (nameof(value.Deadlines.TransactionTimeout), value.Deadlines.TransactionTimeout.Ticks);
        yield return (nameof(value.Deadlines.CommitObservationTimeout), value.Deadlines.CommitObservationTimeout.Ticks);
        yield return (nameof(value.Deadlines.ReceiptResolutionTimeout), value.Deadlines.ReceiptResolutionTimeout.Ticks);
    }

    private static BaseModuleMutationLimits WithLimit(
        BaseModuleMutationLimits value,
        string member,
        long amount) => member switch
    {
        nameof(value.MaximumCaptures) => value with { MaximumCaptures = checked((int)amount) },
        nameof(value.MaximumRecordCaptures) => value with { MaximumRecordCaptures = checked((int)amount) },
        nameof(value.MaximumRelationTargetCaptures) => value with { MaximumRelationTargetCaptures = checked((int)amount) },
        nameof(value.MaximumGenerationCaptures) => value with { MaximumGenerationCaptures = checked((int)amount) },
        nameof(value.MaximumRecordMutations) => value with { MaximumRecordMutations = checked((int)amount) },
        nameof(value.MaximumGenerationReads) => value with { MaximumGenerationReads = checked((int)amount) },
        nameof(value.MaximumGenerationComparisons) => value with { MaximumGenerationComparisons = checked((int)amount) },
        nameof(value.MaximumGenerationIncrements) => value with { MaximumGenerationIncrements = checked((int)amount) },
        nameof(value.MaximumGuardNodes) => value with { MaximumGuardNodes = checked((int)amount) },
        nameof(value.MaximumGuardDepth) => value with { MaximumGuardDepth = checked((int)amount) },
        nameof(value.MaximumStatements) => value with { MaximumStatements = checked((int)amount) },
        nameof(value.MaximumBranches) => value with { MaximumBranches = checked((int)amount) },
        nameof(value.MaximumExpressionNodes) => value with { MaximumExpressionNodes = checked((int)amount) },
        nameof(value.MaximumPreconditions) => value with { MaximumPreconditions = checked((int)amount) },
        nameof(value.MaximumRequestGuardEvaluations) => value with { MaximumRequestGuardEvaluations = checked((int)amount) },
        nameof(value.MaximumStaticSetMembers) => value with { MaximumStaticSetMembers = checked((int)amount) },
        nameof(value.MaximumStaticSetComparisons) => value with { MaximumStaticSetComparisons = amount },
        nameof(value.MaximumDisabledCaptures) => value with { MaximumDisabledCaptures = checked((int)amount) },
        nameof(value.MaximumRemovedFields) => value with { MaximumRemovedFields = checked((int)amount) },
        nameof(value.MaximumReadIntervals) => value with { MaximumReadIntervals = checked((int)amount) },
        nameof(value.MaximumSubjectValidations) => value with { MaximumSubjectValidations = checked((int)amount) },
        nameof(value.MaximumAuthorityReads) => value with { MaximumAuthorityReads = checked((int)amount) },
        nameof(value.MaximumRelationChecks) => value with { MaximumRelationChecks = checked((int)amount) },
        nameof(value.MaximumUniqueConstraintChecks) => value with { MaximumUniqueConstraintChecks = checked((int)amount) },
        nameof(value.MaximumRequestBytes) => value with { MaximumRequestBytes = amount },
        nameof(value.MaximumSelectedBytes) => value with { MaximumSelectedBytes = amount },
        nameof(value.MaximumGenerationBytes) => value with { MaximumGenerationBytes = amount },
        nameof(value.MaximumEvidenceBytes) => value with { MaximumEvidenceBytes = amount },
        nameof(value.MaximumWrittenBytes) => value with { MaximumWrittenBytes = amount },
        nameof(value.MaximumFactBytes) => value with { MaximumFactBytes = amount },
        nameof(value.MaximumJournalBytes) => value with { MaximumJournalBytes = amount },
        nameof(value.MaximumReceiptBytes) => value with { MaximumReceiptBytes = amount },
        nameof(value.MaximumResultBytes) => value with { MaximumResultBytes = amount },
        nameof(value.MaximumTransientBytes) => value with { MaximumTransientBytes = amount },
        nameof(value.Deadlines.AcquisitionTimeout) => value with { Deadlines = value.Deadlines with { AcquisitionTimeout = TimeSpan.FromTicks(amount) } },
        nameof(value.Deadlines.TransactionTimeout) => value with { Deadlines = value.Deadlines with { TransactionTimeout = TimeSpan.FromTicks(amount) } },
        nameof(value.Deadlines.CommitObservationTimeout) => value with { Deadlines = value.Deadlines with { CommitObservationTimeout = TimeSpan.FromTicks(amount) } },
        nameof(value.Deadlines.ReceiptResolutionTimeout) => value with { Deadlines = value.Deadlines with { ReceiptResolutionTimeout = TimeSpan.FromTicks(amount) } },
        _ => throw new ArgumentOutOfRangeException(nameof(member)),
    };

    private static JsonDocument LoadGraph() => JsonDocument.Parse(File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "auth-base-graph-v2.json")));
}
