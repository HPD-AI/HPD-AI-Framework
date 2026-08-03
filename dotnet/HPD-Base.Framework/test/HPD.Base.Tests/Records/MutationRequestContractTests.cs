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
        Assert.Null(typeof(BaseCreate<>).GetProperty("IdempotencyKey"));
        Assert.DoesNotContain(
            typeof(BaseCollectionSession<>).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => method.Name == "CreateAsync"
                && method.GetParameters().Any(parameter => parameter.Name == "idempotencyKey"));
        Assert.DoesNotContain(
            typeof(BaseBatchBuilder).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => method.Name == "Create"
                && method.GetParameters().Any(parameter => parameter.Name == "idempotencyKey"));
    }
}
