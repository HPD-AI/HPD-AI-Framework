using System.Reflection;
using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Records;

public sealed class MutationRequestContractTests
{
    [Fact]
    public void FingerprintDefensivelyCopiesAndUsesByteContentIdentity()
    {
        byte[] source = Enumerable.Range(0, BaseMutationRequestFingerprint.Length)
            .Select(static value => (byte)value)
            .ToArray();
        var first = BaseMutationRequestFingerprint.Create(source);
        var second = BaseMutationRequestFingerprint.Create(source);

        source[0] = 255;

        Assert.Equal(first, second);
        Assert.Equal(0, first.ToArray()[0]);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void FingerprintRejectsEveryInvalidLength(int length) =>
        Assert.Throws<ArgumentException>(() =>
            BaseMutationRequestFingerprint.Create(new byte[length]));

    [Fact]
    public void IdentityFactoryNormalizesAndRejectsControlCharacters()
    {
        var identity = BaseMutationRequestIdentity.Create(
            "  tenant/project  ",
            " submit ",
            " key-1 ",
            BaseMutationRequestFingerprint.Create(new byte[32]));

        Assert.Equal("tenant/project", identity.Scope);
        Assert.Equal("submit", identity.Operation);
        Assert.Equal("key-1", identity.IdempotencyKey);
        Assert.Throws<ArgumentException>(() => BaseMutationRequestIdentity.Create(
            "tenant\u0001",
            "submit",
            "key-1",
            BaseMutationRequestFingerprint.Create(new byte[32])));
    }

    [Fact]
    public void CreateLevelIdempotencySurfaceIsGone()
    {
        Assert.Null(typeof(RecordCreateRequest).GetProperty("IdempotencyKey"));
        Assert.Null(typeof(BaseCollection<>).Assembly.GetType("HPD.Base.BaseCreate`1"));
        Assert.DoesNotContain(
            typeof(BaseCollection<>).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => method.Name is "Create" or "Patch" or "Replace" or "Upsert" or "Delete");
        Assert.DoesNotContain(
            typeof(BaseCollectionSession<>).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => method.Name == "CreateAsync"
                && method.GetParameters().Any(parameter => parameter.Name == "idempotencyKey"));
        Assert.DoesNotContain(
            typeof(BaseBatchBuilder).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => method.Name == "Create"
                && method.GetParameters().Any(parameter => parameter.Name == "idempotencyKey"));
    }

    [Fact]
    public void AtomicRequestCapabilityExposesTheClosedDurabilityContract()
    {
        Enum.GetNames<BaseAtomicRequestDurability>()
            .Should().Equal("None", "ProcessLocal", "Durable");
        typeof(StoreCapabilityDescriptor).GetProperty("AtomicRequest").Should().NotBeNull();
        BaseMutationRequestErrorCodes.OutcomeUnknown.Should().Be("base.runtime.request.outcomeUnknown");
    }

    [Fact]
    public void AdministrationCollectionsAreDefensivelyCopied()
    {
        RecordId[] ids = [RecordId.Create("record_1")];
        var request = new BasePurgeRequest
        {
            CollectionId = "history",
            RecordIds = ids,
            Principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System },
            ReasonCode = "expired",
            AuditReference = "audit_1",
            EvaluatedAt = DateTimeOffset.UnixEpoch,
        };
        ids[0] = RecordId.Create("changed");
        RecordId[] exposed = request.RecordIds;
        exposed[0] = RecordId.Create("changed_again");

        request.RecordIds.Should().Equal(RecordId.Create("record_1"));
    }
}
