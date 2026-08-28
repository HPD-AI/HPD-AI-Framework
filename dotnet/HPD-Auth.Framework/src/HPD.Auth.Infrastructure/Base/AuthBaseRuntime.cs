using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HPD.Auth.Core.Interfaces;
using HPD.Base;

namespace HPD.Auth.Infrastructure.Base;

/// <summary>
/// Opens tenant-bound HPD Base sessions for Auth-owned persistence operations.
/// </summary>
internal sealed class AuthBaseRuntime(
    IBaseSessionFactory sessions,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
{
    internal Guid TenantId => tenantContext.InstanceId;

    internal DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow();

    internal BaseSession OpenServiceSession() => sessions.For(new PrincipalContext
    {
        AuthenticationState = PrincipalAuthenticationState.Service,
        SubjectKind = AccessSubjectKind.ServicePrincipal,
        SubjectId = "hpd.auth",
        CurrentTenantId = TenantId.ToString("D"),
        AuthSource = "hpd.auth.runtime.v1",
    });

    internal static BaseMutationRequestIdentity MutationIdentity(
        string operation,
        Guid tenantId,
        string subjectId,
        params string?[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        using var canonical = new MemoryStream();
        Write(canonical, "HPD-AUTH-MUTATION-IDENTITY-1");
        Write(canonical, operation);
        Write(canonical, tenantId.ToString("D"));
        Write(canonical, subjectId);
        foreach (string? value in values)
            WriteNullable(canonical, value);

        byte[] digest = SHA256.HashData(canonical.GetBuffer().AsSpan(0, checked((int)canonical.Length)));
        string fingerprint = Convert.ToHexStringLower(digest);
        return BaseMutationRequestIdentity.Create(
            $"hpd.auth:{tenantId:D}",
            operation,
            $"{subjectId}:{fingerprint}",
            BaseMutationRequestFingerprint.Create(digest));
    }

    private static void WriteNullable(Stream stream, string? value)
    {
        stream.WriteByte(value is null ? (byte)0 : (byte)1);
        if (value is not null)
            Write(stream, value);
    }

    private static void Write(Stream stream, string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC));
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, utf8.Length);
        stream.Write(length);
        stream.Write(utf8);
    }
}

/// <summary>
/// Represents a safe Auth persistence failure after provider details were discarded.
/// </summary>
internal sealed class AuthBasePersistenceException : InvalidOperationException
{
    internal AuthBasePersistenceException(string code)
        : base($"HPD Auth persistence could not complete the operation ({code}).") => Code = code;

    internal string Code { get; }
}
