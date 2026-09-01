using System.Security.Cryptography;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace HPD.Base.Tests.Application.Activations;

public sealed class GeneratedActivationDtoAuthorityTests
{
    [Fact]
    public void Canonical_round_trip_and_checksums_are_stable()
    {
        BaseGeneratedActivationDtoAuthority<GeneratedActivationInput, GeneratedActivationResult> authority =
            GeneratedActivationDtos.HPDBaseActivationDtoAuthority;
        var input = new GeneratedActivationInput { Name = "work", Digest = BaseBinary.From(new byte[32]) };

        byte[] first = authority.CanonicalInput(input);
        byte[] second = authority.CanonicalInput(input);

        first.Should().Equal(second);
        authority.DecodeInput(first, providerInfluenced: false).Should().Be(input);
        authority.InputDtoAuthorityChecksum.Length.Should().Be(SHA256.HashSizeInBytes);
        authority.ResultDtoAuthorityChecksum.Length.Should().Be(SHA256.HashSizeInBytes);
        authority.DtoAuthorityChecksum.Length.Should().Be(SHA256.HashSizeInBytes);
    }

    [Fact]
    public void Duplicate_and_unknown_provider_properties_are_rejected()
    {
        BaseGeneratedActivationDtoAuthority<GeneratedActivationInput, GeneratedActivationResult> authority =
            GeneratedActivationDtos.HPDBaseActivationDtoAuthority;

        Action duplicate = () => authority.DecodeInput(
            "{\"name\":\"one\",\"name\":\"two\",\"digest\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"}"u8,
            providerInfluenced: true);
        Action unknown = () => authority.DecodeInput(
            "{\"name\":\"one\",\"digest\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\",\"extra\":true}"u8,
            providerInfluenced: true);

        duplicate.Should().Throw<Exception>();
        unknown.Should().Throw<Exception>();
    }

    [Fact]
    public void Generated_identity_rebinds_runtime_metadata_to_the_finalized_owner()
    {
        BaseGeneratedActivationDtoAuthority<GeneratedActivationInput, GeneratedActivationResult> authority =
            GeneratedActivationDtos.HPDBaseActivationDtoAuthority;
        var definition = new BaseActivationDefinition
        {
            Id = "hpd.base.tests.activation.v1", Version = 1, OwningModuleId = "hpd.base.tests",
            ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
            InputTypeId = authority.InputTypeId, ResultTypeId = authority.ResultTypeId,
            Grants = null!, SourceGrantIds = [], Retry = null!,
            Limits = new BaseActivationLimits
            {
                MaximumInputBytes = 1, MaximumResultBytes = 1, MaximumAttempts = 1, MaximumYields = 0,
                MaximumRenewalsPerSlice = 0, MaximumChildrenPerSlice = 0, MaximumLineageDepth = 0,
                LeaseDuration = TimeSpan.FromSeconds(1), HandlerTimeout = TimeSpan.FromSeconds(1),
                Provider = null!, AtomicCreation = null!,
            },
            ReceiptRetention = new BaseActivationReceiptRetentionPolicy
            {
                FormatVersion = 1, DuplicateResolutionLifetime = TimeSpan.FromHours(1),
                ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
            },
            Checksum = new byte[32].ToImmutableArray(),
        };
        BaseActivationRegistrationIdentity<GeneratedActivationInput, GeneratedActivationResult> identity =
            BaseActivationRegistrationIdentity<GeneratedActivationInput, GeneratedActivationResult>.Generated(definition, authority);
        object provisional = identity.Input;

        _ = BaseSerializerMetadataOwner.Create([(IBaseSerializerMetadataSource)identity]);

        identity.Input.Should().NotBeSameAs(provisional);
        identity.Input.Should().BeSameAs(authority.InputTypeInfo);
        identity.Result.Should().BeSameAs(authority.ResultTypeInfo);
    }

    [Fact]
    public void Scalar_authority_changes_change_the_dto_authority_checksum()
    {
        GeneratedActivationDtos.HPDBaseActivationDtoAuthority.InputDtoAuthorityChecksum.Span
            .SequenceEqual(AlternateGeneratedActivationDtos.HPDBaseActivationDtoAuthority.InputDtoAuthorityChecksum.Span)
            .Should().BeFalse();
    }

    [Fact]
    public void Null_typed_input_has_one_bounded_contract_failure()
    {
        Action act = () => GeneratedActivationDtos.HPDBaseActivationDtoAuthority.CanonicalInput(null!);

        act.Should().Throw<BaseActivationDtoContractException>()
            .Which.Code.Should().Be("base.activation.inputInvalid");
    }
}

[BaseActivationDtoAuthority(
    "hpd.base.tests.activation.dto.v1", 1, "hpd.base.tests",
    "hpd.base.tests.activation.input.v1", "hpd.base.tests.activation.result.v1",
    typeof(GeneratedActivationDtoJsonContext), typeof(GeneratedActivationInput), typeof(GeneratedActivationResult))]
internal static partial class GeneratedActivationDtos;

[BaseActivationDtoAuthority(
    "hpd.base.tests.activation.dto.alternate.v1", 1, "hpd.base.tests",
    "hpd.base.tests.activation.input.alternate.v1", "hpd.base.tests.activation.result.alternate.v1",
    typeof(AlternateGeneratedActivationDtoJsonContext), typeof(AlternateGeneratedActivationInput), typeof(AlternateGeneratedActivationResult))]
internal static partial class AlternateGeneratedActivationDtos;

internal sealed record GeneratedActivationInput
{
    [BaseField("hpd.base.tests.activation.input.name", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 64)]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string Name { get; init; }

    [BaseField("hpd.base.tests.activation.input.digest", MinimumBytes = 32, MaximumBytes = 32)]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required BaseBinary Digest { get; init; }
}

internal sealed record GeneratedActivationResult
{
    [BaseField("hpd.base.tests.activation.result.accepted")]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required bool Accepted { get; init; }
}

internal sealed record AlternateGeneratedActivationInput
{
    [BaseField("hpd.base.tests.activation.input.name", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 63)]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string Name { get; init; }

    [BaseField("hpd.base.tests.activation.input.digest", MinimumBytes = 32, MaximumBytes = 32)]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required BaseBinary Digest { get; init; }
}

internal sealed record AlternateGeneratedActivationResult
{
    [BaseField("hpd.base.tests.activation.result.alternate.accepted")]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required bool Accepted { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GeneratedActivationInput))]
[JsonSerializable(typeof(GeneratedActivationResult))]
internal sealed partial class GeneratedActivationDtoJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AlternateGeneratedActivationInput))]
[JsonSerializable(typeof(AlternateGeneratedActivationResult))]
internal sealed partial class AlternateGeneratedActivationDtoJsonContext : JsonSerializerContext;
