using System.Collections.Immutable;
using System.Security.Cryptography;
using HPD.Base.Testing;
using Microsoft.Extensions.Options;

namespace HPD.Base.Tests.Runtime.Security;

public sealed class BaseSemanticActivationControlTokenCodecTests
{
    [Fact]
    public void Control_tokens_bind_store_application_payload_kind_and_expiry()
    {
        var time = new BaseTestTimeProvider(new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.Zero));
        using var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 53, Key = Enumerable.Repeat((byte)0x53, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            },
        }), time);
        var codec = new BaseSemanticActivationControlTokenCodec(protector, time);
        BaseSemanticActivationControlTokenPayload value = Payload(time.GetUtcNow().AddMinutes(5));

        BaseSemanticActivationControlToken token = codec.Protect(value);

        codec.TryRead(token, "application", "store", out BaseSemanticActivationControlTokenPayload? decoded).Should().BeTrue();
        decoded.Should().BeEquivalentTo(value);
        codec.TryRead(token, "other", "store", out _).Should().BeFalse();
        codec.TryRead(token, "application", "other", out _).Should().BeFalse();
        char replacement = token.Value[^1] == 'A' ? 'B' : 'A';
        codec.TryRead(BaseSemanticActivationControlToken.FromWire(token.Value[..^1] + replacement), "application", "store", out _).Should().BeFalse();
        time.Advance(TimeSpan.FromMinutes(5));
        codec.TryRead(token, "application", "store", out _).Should().BeFalse();
    }

    [Fact]
    public void Control_phase_tokens_reconstruct_one_exact_identified_operation()
    {
        BaseSemanticActivationControlTokenPayload payload = Payload(DateTimeOffset.MaxValue);
        BaseMutationRequestIdentity initial = DefaultHPDBaseAdministration.SemanticControlIdentity(payload, "operation-1");
        BaseMutationRequestIdentity resumed = DefaultHPDBaseAdministration.SemanticControlIdentity(
            payload with { Kind = BaseSemanticActivationControlTokenKind.ResumeCompact, IdempotencyKey = "operation-1" }, "operation-1");
        BaseMutationRequestIdentity resolved = DefaultHPDBaseAdministration.SemanticControlIdentity(
            payload with { Kind = BaseSemanticActivationControlTokenKind.ResolveCompact, IdempotencyKey = "operation-1" }, "operation-1");

        resumed.Scope.Should().Be(initial.Scope); resumed.Operation.Should().Be(initial.Operation);
        resumed.IdempotencyKey.Should().Be(initial.IdempotencyKey); resumed.Fingerprint.Should().Be(initial.Fingerprint);
        resolved.Scope.Should().Be(initial.Scope); resolved.Operation.Should().Be(initial.Operation);
        resolved.IdempotencyKey.Should().Be(initial.IdempotencyKey); resolved.Fingerprint.Should().Be(initial.Fingerprint);
        DefaultHPDBaseAdministration.SemanticControlIdentity(payload with
            { Kind = BaseSemanticActivationControlTokenKind.Remove }, "operation-1").Fingerprint.Should().NotBe(initial.Fingerprint);
        DefaultHPDBaseAdministration.SemanticControlIdentity(payload, "operation-2").Fingerprint.Should().NotBe(initial.Fingerprint);
        BaseMutationRequestIdentity trimmed = DefaultHPDBaseAdministration.SemanticControlIdentity(payload, " operation-1 ");
        trimmed.IdempotencyKey.Should().Be(initial.IdempotencyKey);
        trimmed.Fingerprint.Should().Be(initial.Fingerprint);
        BaseMutationRequestIdentity decomposed = DefaultHPDBaseAdministration.SemanticControlIdentity(payload, "ope\u0301ration-1");
        BaseMutationRequestIdentity composed = DefaultHPDBaseAdministration.SemanticControlIdentity(payload, "opération-1");
        decomposed.IdempotencyKey.Should().Be(composed.IdempotencyKey);
        decomposed.Fingerprint.Should().Be(composed.Fingerprint);
    }

    [Fact]
    public void Control_payload_has_one_locked_canonical_binary_encoding()
    {
        BaseSemanticActivationControlTokenPayload payload = Payload(
            new DateTimeOffset(2031, 2, 3, 4, 10, 6, TimeSpan.Zero));

        Convert.ToHexString(BaseSemanticActivationControlTokenCodec.CanonicalPayloadChecksum(payload).AsSpan())
            .Should().Be("52444156261BED100BA2182C1D903C265D55F03F35C3863AD39D72AA0563F980");

        foreach (BaseSemanticActivationControlTokenKind kind in Enum.GetValues<BaseSemanticActivationControlTokenKind>())
            BaseSemanticActivationControlTokenCodec.CanonicalPayloadChecksum(payload with { Kind = kind })
                .Should().NotEqual(BaseSemanticActivationControlTokenCodec.CanonicalPayloadChecksum(
                    payload with { Kind = kind == BaseSemanticActivationControlTokenKind.Compact
                        ? BaseSemanticActivationControlTokenKind.Remove
                        : BaseSemanticActivationControlTokenKind.Compact }));

        BaseSemanticActivationControlTokenCodec.CanonicalPayloadChecksum(payload with { LiveCount = 2 })
            .Should().NotEqual(BaseSemanticActivationControlTokenCodec.CanonicalPayloadChecksum(payload));
        BaseSemanticActivationControlTokenCodec.CanonicalPayloadChecksum(payload with { RestoreEpoch = 8 })
            .Should().NotEqual(BaseSemanticActivationControlTokenCodec.CanonicalPayloadChecksum(payload));
        BaseSemanticActivationControlTokenCodec.CanonicalPayloadChecksum(payload with { IdempotencyKey = "operation-1" })
            .Should().NotEqual(BaseSemanticActivationControlTokenCodec.CanonicalPayloadChecksum(payload));
    }

    private static BaseSemanticActivationControlTokenPayload Payload(DateTimeOffset expiry) => new(
        BaseSemanticActivationControlTokenKind.Compact,
        "application", "store", 7,
        new BaseSemanticActivationDefinitionKey
        {
            Id = "semantic.definition", Version = 3,
            Checksum = SHA256.HashData("definition"u8).ToImmutableArray(),
        },
        SHA256.HashData("definitions"u8).ToImmutableArray(), 9, 1, 2, 3,
        SHA256.HashData("retired"u8).ToImmutableArray(),
        SHA256.HashData("state"u8).ToImmutableArray(),
        SHA256.HashData("absence"u8).ToImmutableArray(),
        new BaseSemanticActivationMaintenanceLimits
        {
            PageSize = 4, MaximumPages = 5, MaximumRows = 20,
            MaximumBytes = 4096, Deadline = TimeSpan.FromSeconds(5),
        }, null, expiry);
}
