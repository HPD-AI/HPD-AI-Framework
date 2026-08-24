using HPD.Payments.Contracts.ExternalEffect;

namespace HPD.Payments.Host.Api;

/// <summary>Names the exactly one admitted host profile artifact.</summary>
public enum PaymentsApiProfile { None = 0, EmbeddedInMemory, EmbeddedSqlite }

/// <summary>Immutable host configuration; profile selection is explicit and singular.</summary>
public sealed record PaymentsApiConfiguration
{
    /// <summary>Gets the selected profile.</summary>
    public PaymentsApiProfile Profile { get; }
    /// <summary>Gets the exact wire version.</summary>
    public string WireVersion => "hpd.payments.api.v1";
    /// <summary>Creates a validated API configuration.</summary>
    public PaymentsApiConfiguration(PaymentsApiProfile profile)
    {
        if (profile is PaymentsApiProfile.None || !Enum.IsDefined(profile)) throw new ArgumentException("Exactly one API profile must be selected.");
        Profile = profile;
    }
}

/// <summary>Transport-only evidence response with bounded redaction.</summary>
public sealed record PaymentsApiEvidenceResponse(string OperationId, string State, string? ExternalReference, string WireVersion);

/// <summary>Defines versioning and redaction at the HTTP boundary without owning payment authority.</summary>
public static class PaymentsApiTransport
{
    /// <summary>Gets the complete closed route inventory.</summary>
    public static IReadOnlyList<string> Routes { get; } = Array.AsReadOnly(new[] { "/hpd/payments/v1/health", "/hpd/payments/v1/manifest" });

    /// <summary>Projects already-authoritative evidence into a versioned response.</summary>
    public static PaymentsApiEvidenceResponse Project(string operationId, ExternalEffectState state, string? externalReference,
        bool mayReadExternalReference)
    {
        if (string.IsNullOrWhiteSpace(operationId) || state == ExternalEffectState.None || !Enum.IsDefined(state))
            throw new ArgumentException("API evidence projection is invalid.");
        return new(operationId, state.ToString(), mayReadExternalReference ? externalReference : null, "hpd.payments.api.v1");
    }

    /// <summary>Rejects unknown wire versions before request interpretation.</summary>
    public static void RequireVersion(string version)
    {
        if (!StringComparer.Ordinal.Equals(version, "hpd.payments.api.v1")) throw new InvalidOperationException("Unsupported Payments API version.");
    }
}
