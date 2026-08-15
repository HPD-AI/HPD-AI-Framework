using System.Buffers.Binary;
using System.Text;

namespace HPD.Payments.Primitives.Identity;

/// <summary>Identifies one tenant, environment, and exclusive authority collision domain.</summary>
/// <remarks>Components use ordinal lowercase-ASCII semantics. The default value is invalid and ambient tenant/environment context is never consulted.</remarks>
public readonly struct ScopeId : IEquatable<ScopeId>
{
    /// <summary>Specifies the maximum admitted size or count enforced by the containing type.</summary>
    public const int MaximumComponentUtf8Bytes = 128;
    /// <summary>Gets the validated <c>Tenant</c> component; it does not imply ambient context or mutation authority.</summary>
    public string Tenant { get; }
    /// <summary>Gets the validated <c>Environment</c> component; it does not imply ambient context or mutation authority.</summary>
    public string Environment { get; }
    /// <summary>Gets the validated <c>Authority</c> component; it does not imply ambient context or mutation authority.</summary>
    public string Authority { get; }

    /// <summary>Gets whether the value satisfies its required-field and bound invariants; a default value is invalid.</summary>
    public bool IsValid => Tenant is not null && Environment is not null && Authority is not null;

    private ScopeId(string tenant, string environment, string authority) =>
        (Tenant, Environment, Authority) = (tenant, environment, authority);

    /// <summary>Validates all collision-domain components without throwing for invalid input.</summary>
    /// <param name="tenant">The explicit tenant token.</param><param name="environment">The explicit environment token.</param><param name="authority">The exclusive authority token.</param>
    /// <param name="value">The valid scope, or the default invalid scope on failure.</param>
    /// <returns><see langword="true"/> only when every component is non-empty lowercase ASCII and within <see cref="MaximumComponentUtf8Bytes"/>.</returns>
    public static bool TryCreate(string? tenant, string? environment, string? authority, out ScopeId value) =>
        TryComponent(tenant, out var t) && TryComponent(environment, out var e) && TryComponent(authority, out var a)
            ? Return(new(t, e, a), out value)
            : Return(default, out value);

    /// <summary>Creates a validated explicit collision scope.</summary>
    /// <param name="tenant">Tenant token.</param><param name="environment">Environment token.</param><param name="authority">Authority token.</param>
    /// <returns>The validated scope.</returns><exception cref="ArgumentException">Any component is invalid or over-bound.</exception>
    public static ScopeId Create(string tenant, string environment, string authority) =>
        TryCreate(tenant, environment, authority, out var value) ? value : throw new ArgumentException("Scope components must be non-empty lowercase ASCII tokens within the UTF-8 bound.");

    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public bool Equals(ScopeId other) => IsValid && other.IsValid &&
        StringComparer.Ordinal.Equals(Tenant, other.Tenant) && StringComparer.Ordinal.Equals(Environment, other.Environment) && StringComparer.Ordinal.Equals(Authority, other.Authority);
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public override bool Equals(object? obj) => obj is ScopeId other && Equals(other);
    /// <summary>Returns a process-local hash consistent with equality; the hash is never a persisted identity.</summary>
    public override int GetHashCode() => IsValid ? HashCode.Combine(StringComparer.Ordinal.GetHashCode(Tenant), StringComparer.Ordinal.GetHashCode(Environment), StringComparer.Ordinal.GetHashCode(Authority)) : 0;
    /// <summary>Returns the stable textual representation defined by the containing type, or its explicit invalid diagnostic where supported.</summary>
    public override string ToString() => IsValid ? $"{Tenant}/{Environment}/{Authority}" : "<invalid-scope>";
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public static bool operator ==(ScopeId left, ScopeId right) => left.Equals(right);
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public static bool operator !=(ScopeId left, ScopeId right) => !left.Equals(right);

    internal static bool TryComponent(string? input, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(input) || Encoding.UTF8.GetByteCount(input) > MaximumComponentUtf8Bytes) return false;
        foreach (var c in input)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_')) return false;
        value = input;
        return true;
    }

    private static bool Return(ScopeId candidate, out ScopeId value) { value = candidate; return candidate.IsValid; }
}

/// <summary>Identifies one immutable semantic subject inside its complete tenant/environment/authority/namespace/kind collision scope.</summary>
/// <remarks>Provider and account are an all-or-none external-scope suffix. Equality is ordinal and the default value is invalid.</remarks>
public readonly struct SemanticId : IEquatable<SemanticId>
{
    /// <summary>Specifies the maximum admitted size or count enforced by the containing type.</summary>
    public const int MaximumCanonicalBytes = 1024;
    /// <summary>Gets the validated <c>Scope</c> component; it does not imply ambient context or mutation authority.</summary>
    public ScopeId Scope { get; }
    /// <summary>Gets the validated <c>Namespace</c> component; it does not imply ambient context or mutation authority.</summary>
    public string Namespace { get; }
    /// <summary>Gets the validated <c>Kind</c> component; it does not imply ambient context or mutation authority.</summary>
    public string Kind { get; }
    /// <summary>Gets the validated <c>LocalId</c> component; it does not imply ambient context or mutation authority.</summary>
    public string LocalId { get; }
    /// <summary>Gets the validated <c>Provider</c> component; it does not imply ambient context or mutation authority.</summary>
    public string? Provider { get; }
    /// <summary>Gets the validated <c>Account</c> component; it does not imply ambient context or mutation authority.</summary>
    public string? Account { get; }
    /// <summary>Gets whether the value satisfies its required-field and bound invariants; a default value is invalid.</summary>
    public bool IsValid => Scope.IsValid && Namespace is not null && Kind is not null && LocalId is not null && ((Provider is null) == (Account is null));

    private SemanticId(ScopeId scope, string ns, string kind, string localId, string? provider, string? account) =>
        (Scope, Namespace, Kind, LocalId, Provider, Account) = (scope, ns, kind, localId, provider, account);

    /// <summary>Validates a complete semantic identity, including the optional paired provider/account suffix.</summary>
    /// <param name="scope">A valid explicit collision scope.</param><param name="ns">Namespace token.</param><param name="kind">Semantic kind token.</param><param name="localId">Local identifier token.</param>
    /// <param name="value">The valid identity, or default invalid identity on failure.</param><param name="provider">Optional provider token.</param><param name="account">Optional account token required exactly when provider is supplied.</param>
    /// <returns><see langword="true"/> when every component and the total canonical encoding fit their bounds.</returns>
    public static bool TryCreate(ScopeId scope, string? ns, string? kind, string? localId, out SemanticId value, string? provider = null, string? account = null)
    {
        value = default;
        if (!scope.IsValid || !ScopeId.TryComponent(ns, out var n) || !ScopeId.TryComponent(kind, out var k) || !ScopeId.TryComponent(localId, out var l)) return false;
        if ((provider is null) != (account is null)) return false;
        string? p = null, a = null;
        if (provider is not null && (!ScopeId.TryComponent(provider, out p!) || !ScopeId.TryComponent(account, out a!))) return false;
        var candidate = new SemanticId(scope, n, k, l, p, a);
        if (candidate.GetCanonicalBytes().Length > MaximumCanonicalBytes) return false;
        value = candidate;
        return true;
    }

    /// <summary>Creates a validated value and rejects missing, unknown, or out-of-bound components.</summary>
    public static SemanticId Create(ScopeId scope, string ns, string kind, string localId, string? provider = null, string? account = null) =>
        TryCreate(scope, ns, kind, localId, out var value, provider, account) ? value : throw new ArgumentException("Invalid or over-bound semantic identity.");

    /// <summary>Returns newly allocated, ordered, two-byte big-endian-length-prefixed UTF-8 components.</summary>
    /// <returns>Canonical bytes that include tenant and environment and therefore cannot collide across those scopes.</returns>
    /// <exception cref="InvalidOperationException">This is the default invalid identity.</exception>
    public byte[] GetCanonicalBytes()
    {
        if (!IsValid) throw new InvalidOperationException("The default identity is invalid.");
        var fields = Provider is null
            ? new[] { Scope.Tenant, Scope.Environment, Scope.Authority, Namespace, Kind, LocalId }
            : new[] { Scope.Tenant, Scope.Environment, Scope.Authority, Namespace, Kind, LocalId, Provider, Account };
        var size = fields.Sum(static x => 2 + Encoding.UTF8.GetByteCount(x!));
        var bytes = new byte[size];
        var offset = 0;
        foreach (var field in fields)
        {
            var length = Encoding.UTF8.GetByteCount(field!);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset, 2), checked((ushort)length));
            offset += 2;
            offset += Encoding.UTF8.GetBytes(field!, bytes.AsSpan(offset));
        }
        return bytes;
    }

    /// <summary>Parses the exact six- or eight-component canonical encoding without Unicode replacement or unknown-field fallthrough.</summary>
    /// <param name="bytes">Borrowed lexical bytes; no alias is retained.</param><param name="value">The parsed identity, or default invalid identity on failure.</param>
    /// <returns><see langword="false"/> for malformed UTF-8, length errors, invalid tokens, wrong component count, or oversize input.</returns>
    public static bool TryParseCanonical(ReadOnlySpan<byte> bytes, out SemanticId value)
    {
        value = default;
        if (bytes.Length is 0 or > MaximumCanonicalBytes) return false;
        var fields = new List<string>(8);
        while (!bytes.IsEmpty)
        {
            if (bytes.Length < 2) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes);
            bytes = bytes[2..];
            if (length == 0 || length > bytes.Length) return false;
            try { fields.Add(new UTF8Encoding(false, true).GetString(bytes[..length])); }
            catch (DecoderFallbackException) { return false; }
            bytes = bytes[length..];
        }
        if (fields.Count is not (6 or 8) || !ScopeId.TryCreate(fields[0], fields[1], fields[2], out var scope)) return false;
        return TryCreate(scope, fields[3], fields[4], fields[5], out value,
            fields.Count == 8 ? fields[6] : null, fields.Count == 8 ? fields[7] : null);
    }

    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public bool Equals(SemanticId other) => IsValid && other.IsValid && Scope == other.Scope &&
        StringComparer.Ordinal.Equals(Namespace, other.Namespace) && StringComparer.Ordinal.Equals(Kind, other.Kind) && StringComparer.Ordinal.Equals(LocalId, other.LocalId) &&
        StringComparer.Ordinal.Equals(Provider, other.Provider) && StringComparer.Ordinal.Equals(Account, other.Account);
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public override bool Equals(object? obj) => obj is SemanticId other && Equals(other);
    /// <summary>Returns a process-local hash consistent with equality; the hash is never a persisted identity.</summary>
    public override int GetHashCode() => IsValid ? HashCode.Combine(Scope, StringComparer.Ordinal.GetHashCode(Namespace), StringComparer.Ordinal.GetHashCode(Kind), StringComparer.Ordinal.GetHashCode(LocalId), Provider, Account) : 0;
    /// <summary>Returns the stable textual representation defined by the containing type, or its explicit invalid diagnostic where supported.</summary>
    public override string ToString() => IsValid ? $"{Scope}/{Namespace}/{Kind}/{LocalId}" : "<invalid-identity>";
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public static bool operator ==(SemanticId left, SemanticId right) => left.Equals(right);
    /// <summary>Compares values using the containing type’s stable component and owned-content equality rules.</summary>
    public static bool operator !=(SemanticId left, SemanticId right) => !left.Equals(right);
}
