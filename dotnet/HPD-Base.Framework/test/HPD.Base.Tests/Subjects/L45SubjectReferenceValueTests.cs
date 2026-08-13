using System.Text.Json;

#pragma warning disable HPDBASE0461 // Adversarial contract construction is intentionally exercised by this test fixture.

namespace HPD.Base.Tests.Subjects;

public sealed class L45SubjectReferenceValueTests
{
    private sealed class UserSubject;
    private sealed class SecondSubject;

    static L45SubjectReferenceValueTests() =>
        BaseSubjectReferenceJsonConverterFactory.Register<UserSubject>(BaseSubjectIdKind.Guid, 36);

    [Theory]
    [InlineData(BaseSubjectIdKind.OrdinalString, "Ada", "Ada")]
    [InlineData(BaseSubjectIdKind.Guid, "0194f778-5cd1-7d17-ae1f-8f95b3114a20", "0194f778-5cd1-7d17-ae1f-8f95b3114a20")]
    [InlineData(BaseSubjectIdKind.UInt64, "0", "0")]
    [InlineData(BaseSubjectIdKind.UInt64, "18446744073709551615", "18446744073709551615")]
    public void Subject_id_accepts_only_canonical_closed_grammars(BaseSubjectIdKind kind, string input, string expected)
    {
        BaseSubjectId id = BaseSubjectId.Create(input, kind);

        Assert.Equal(expected, id.Value);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(expected), id.ToUtf8Bytes());
    }

    [Theory]
    [InlineData(BaseSubjectIdKind.OrdinalString, "e\u0301")]
    [InlineData(BaseSubjectIdKind.OrdinalString, "a\n")]
    [InlineData(BaseSubjectIdKind.Guid, "0194F778-5CD1-7D17-AE1F-8F95B3114A20")]
    [InlineData(BaseSubjectIdKind.Guid, "{0194f778-5cd1-7d17-ae1f-8f95b3114a20}")]
    [InlineData(BaseSubjectIdKind.UInt64, "00")]
    [InlineData(BaseSubjectIdKind.UInt64, "+1")]
    public void Subject_id_rejects_noncanonical_values(BaseSubjectIdKind kind, string input) =>
        Assert.Throws<FormatException>(() => BaseSubjectId.Create(input, kind));

    [Fact]
    public void Ordinal_subject_id_rejects_invalid_utf16_instead_of_replacing_it() =>
        Assert.Throws<FormatException>(() => BaseSubjectId.Create("\ud800", BaseSubjectIdKind.OrdinalString));

    [Fact]
    public void Reference_codec_owns_exact_canonical_wire_shape()
    {
        const string json = "{\"subjectId\":\"0194f778-5cd1-7d17-ae1f-8f95b3114a20\",\"authorityEpoch\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"incarnation\":\"BBBBBBBBBBBBBBBBBBBBBA\"}";

        BaseSubjectReference<UserSubject> value = JsonSerializer.Deserialize<BaseSubjectReference<UserSubject>>(json);

        Assert.Equal("0194f778-5cd1-7d17-ae1f-8f95b3114a20", value.SubjectId.Value);
        Assert.Equal(json, JsonSerializer.Serialize(value));
        byte[] epoch = value.AuthorityEpoch.ToArray();
        epoch[0] = 255;
        Assert.All(value.AuthorityEpoch.ToArray(), static item => Assert.Equal(0, item));
    }

    [Theory]
    [InlineData("{\"authorityEpoch\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"subjectId\":\"0194f778-5cd1-7d17-ae1f-8f95b3114a20\",\"incarnation\":\"BBBBBBBBBBBBBBBBBBBBBA\"}")]
    [InlineData("{\"subjectId\":\"0194f778-5cd1-7d17-ae1f-8f95b3114a20\",\"authorityEpoch\":\"AAAAAAAAAAAAAAAAAAAAAA==\",\"incarnation\":\"BBBBBBBBBBBBBBBBBBBBBA\"}")]
    [InlineData("{\"subjectId\":\"0194f778-5cd1-7d17-ae1f-8f95b3114a20\",\"authorityEpoch\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"incarnation\":\"BBBBBBBBBBBBBBBBBBBBBA\",\"extra\":0}")]
    [InlineData("{\"subjectId\":null,\"authorityEpoch\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"incarnation\":\"BBBBBBBBBBBBBBBBBBBBBA\"}")]
    public void Reference_codec_rejects_noncanonical_shapes(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BaseSubjectReference<UserSubject>>(json));

    [Fact]
    public void Subject_contract_normalization_is_deeply_owned_and_checksum_bound()
    {
        var definition = Definition();

        BaseGeneratedSubjectRegistration registration = BaseGeneratedSubjects.Register<UserSubject>(definition);
        definition.Audiences[0] = HPDBaseEndpointAudience.ControlPlane;

        Assert.Equal(64, registration.Checksum.Length);
        Assert.Equal(64, registration.PlanChecksum.Length);
        Assert.Equal(HPDBaseEndpointAudience.Application, registration.Definition.Audiences[0]);
        Assert.Equal("user.active", registration.Definition.ValidationPlan.Active.FieldId);
        Assert.Equal(registration.Checksum, registration.Definition.ValidationPlan.ContractChecksum);
    }

    [Fact]
    public void Subject_validation_limits_enforce_every_closed_boundary()
    {
        BaseExportedSubjectDefinition original = Definition();
        BaseExportedSubjectDefinition definition = original with
        {
            ValidationPlan = original.ValidationPlan with
            {
                Limits = original.ValidationPlan.Limits with { MaximumReferencesPerRecord = 33 },
            },
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => BaseGeneratedSubjects.Register<UserSubject>(definition));

        Assert.Equal(BaseSubjectErrorCodes.ContractInvalid, exception.Message);
    }

    [Fact]
    public async Task InMemory_reports_one_exact_installation_stable_lowering_receipt()
    {
        BaseGeneratedSubjectRegistration registration = BaseGeneratedSubjects.Register<UserSubject>(Definition());
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            StoreId = "subject-receipts",
            ExportedSubjects = [registration.Definition],
        });

        OperationResult<BaseSubjectValidationPlanReceipt[]> result =
            await store.ReadSubjectValidationPlanReceiptsAsync();

        Assert.True(result.IsSuccess());
        BaseSubjectValidationPlanReceipt receipt = Assert.Single(result.Value!);
        Assert.Equal(registration.Definition.ValidationPlan.Id, receipt.PlanId);
        Assert.Equal(registration.Definition.ValidationPlan.Version, receipt.PlanVersion);
        Assert.Equal(registration.PlanChecksum, receipt.PlanChecksum);
        Assert.Equal("subject-receipts", receipt.StoreInstanceId);
        Assert.Equal(1, receipt.SchemaGeneration);
        Assert.Equal(BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys, receipt.Access);
        Assert.Equal(1, receipt.LoweringFormatVersion);
    }

    [Fact]
    public void Duplicate_validation_plan_identity_fails_with_the_stable_registration_conflict()
    {
        BaseGeneratedSubjectRegistration first = BaseGeneratedSubjects.Register<UserSubject>(Definition());
        BaseExportedSubjectDefinition secondDefinition = Definition() with
        {
            Id = "hpd.auth.second-subject",
            ValidationPlan = Definition().ValidationPlan with { ContractId = "hpd.auth.second-subject" },
        };
        BaseGeneratedSubjectRegistration second = BaseGeneratedSubjects.Register<SecondSubject>(secondDefinition);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new BaseSubjectContractRegistry([first, second]));

        Assert.Equal(BaseSubjectErrorCodes.RegistrationConflict, exception.Message);
    }

    [Fact]
    public void Subject_reference_converter_registration_rejects_conflicting_grammar()
    {
        BaseSubjectReferenceJsonConverterFactory.Register<SecondSubject>(BaseSubjectIdKind.OrdinalString, 40);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            BaseSubjectReferenceJsonConverterFactory.Register<SecondSubject>(BaseSubjectIdKind.Guid, 36));

        Assert.Equal(BaseSubjectErrorCodes.RegistrationConflict, exception.Message);
    }

    public static TheoryData<string, OperationStatus, ErrorCategory, string> FailureMappings => new()
    {
        { BaseSubjectErrorCodes.ContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation, "The subject contract is invalid." },
        { BaseSubjectErrorCodes.RegistrationConflict, OperationStatus.Conflict, ErrorCategory.Conflict, "The subject contract conflicts with the installed graph." },
        { BaseSubjectErrorCodes.ReferenceInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation, "The subject reference is invalid." },
        { BaseSubjectErrorCodes.ValidationUnavailable, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability, "Subject validation is unavailable." },
        { BaseSubjectErrorCodes.GuaranteeUnavailable, OperationStatus.CapabilityUnavailable, ErrorCategory.Unsupported, "The required subject validation guarantee is unavailable." },
        { BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation, "The subject validation limit was exceeded." },
        { BaseSubjectErrorCodes.ProviderContractInvalid, OperationStatus.StoreError, ErrorCategory.Store, "The subject validation provider returned an invalid result." },
        { BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict, "The subject validation authority changed." },
        { BaseSubjectErrorCodes.TransactionConflict, OperationStatus.Conflict, ErrorCategory.Conflict, "The subject validation transaction conflicted." },
        { BaseSubjectErrorCodes.CommitIndeterminate, OperationStatus.StoreError, ErrorCategory.Store, "The subject reference mutation outcome is indeterminate." },
        { BaseSubjectErrorCodes.ReceiptMismatch, OperationStatus.Conflict, ErrorCategory.Conflict, "The mutation identity belongs to a different request." },
    };

    [Theory]
    [MemberData(nameof(FailureMappings))]
    public void Stable_subject_failures_have_the_exact_closed_mapping(
        string code,
        OperationStatus expectedStatus,
        ErrorCategory expectedCategory,
        string expectedMessage)
    {
        BaseError error = BaseSubjectFailureContract.Error(code);

        Assert.Equal(code, error.Code);
        Assert.Equal(expectedCategory, error.Category);
        Assert.Equal(expectedMessage, error.Message);
        Assert.Equal(expectedStatus, BaseSubjectFailureContract.NormalizeProviderStatus(expectedStatus, error));
        Assert.Null(error.Detail);
        Assert.Null(error.Target);
    }

    [Fact]
    public void Unknown_provider_failure_is_sanitized_to_the_closed_provider_contract_failure()
    {
        BaseError error = BaseSubjectFailureContract.Error("hostile.provider.code");

        Assert.Equal(BaseSubjectErrorCodes.ProviderContractInvalid, error.Code);
        Assert.Equal(ErrorCategory.Store, error.Category);
        Assert.Equal("The subject validation provider returned an invalid result.", error.Message);
        Assert.Equal(OperationStatus.StoreError, BaseSubjectFailureContract.NormalizeProviderStatus(
            OperationStatus.Ok,
            new BaseError
            {
                Code = "hostile.provider.code",
                Message = "hostile text",
                Category = ErrorCategory.Authorization,
            }));
    }

    private static BaseExportedSubjectDefinition Definition() => new()
    {
        Id = "hpd.auth.user-subject",
        Version = 1,
        OwningModuleId = "hpd.auth",
        SubjectIdKind = BaseSubjectIdKind.Guid,
        MaximumSubjectIdUtf8Bytes = 36,
        Scope = BaseSubjectScopeKind.Tenant,
        AcquisitionGrantId = "hpd.auth.user.acquire",
        ValidationGrantId = "hpd.auth.user.validate",
        AdministrationGrantId = "hpd.auth.user.admin",
        Audiences = [HPDBaseEndpointAudience.Application],
        ValidationPlan = new BaseSubjectValidationPlanDefinition
        {
            Id = "hpd.auth.user.validate.v1",
            Version = 1,
            ContractId = "hpd.auth.user-subject",
            ContractVersion = 1,
            ContractChecksum = new string('0', 64),
            PrivateCollectionId = "auth.users",
            SubjectId = BaseSubjectIdBinding.RecordId,
            Active = new BaseSubjectActiveBinding { Kind = BaseSubjectActiveBindingKind.RequiredBooleanField, FieldId = "user.active", ActiveValue = true },
            Scope = new BaseSubjectScopeBinding { Kind = BaseSubjectScopeBindingKind.RequiredTenantField, FieldId = "user.tenant" },
            Access = BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys,
            Limits = BaseSubjectValidationLimits.Default with { },
        },
    };
}
