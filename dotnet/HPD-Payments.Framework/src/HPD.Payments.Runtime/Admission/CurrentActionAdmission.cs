using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Runtime.Authorization;

namespace HPD.Payments.Runtime.Admission;

/// <summary>Names the closed result of authorization followed by one owner-local persistence attempt.</summary>
public enum AdmissionDisposition
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>Authorization succeeded and persistence returned a receipt.</summary>
    Attempted,
    /// <summary>Authorization denied the action and persistence was not invoked.</summary>
    Denied,
    /// <summary>Authorization was indeterminate or stale and persistence was not invoked.</summary>
    Indeterminate,
}

/// <summary>Combines an exact current-action request with an authority-created persistence append request.</summary>
public sealed record AdmissionRequest<TAction, TFact>
    where TAction : notnull where TFact : notnull
{
    /// <summary>Gets the current-action authorization input.</summary>
    public CurrentActionRequest<TAction> Authorization { get; }
    /// <summary>Gets the authority-created fact and persistence guards.</summary>
    public OwnerAppendRequest<TFact> Append { get; }

    /// <summary>Creates a request and enforces exact subject/scope agreement between authorization and persistence.</summary>
    /// <exception cref="ArgumentException">The two request halves name different subjects or scopes.</exception>
    public AdmissionRequest(CurrentActionRequest<TAction> authorization, OwnerAppendRequest<TFact> append)
    {
        ArgumentNullException.ThrowIfNull(authorization); ArgumentNullException.ThrowIfNull(append);
        if (authorization.SubjectId != append.ExpectedOwner.SubjectId)
            throw new ArgumentException("Authorization and append must name the same exact subject.");
        Authorization = authorization; Append = append;
    }
}

/// <summary>Returns authorization evidence and, only after authorization, the persistence observation.</summary>
public sealed record AdmissionReceipt<TFact> where TFact : notnull
{
    /// <summary>Gets the admission-level disposition.</summary>
    public AdmissionDisposition Disposition { get; }
    /// <summary>Gets the exact authorization decision.</summary>
    public AuthorizationDecision Authorization { get; }
    /// <summary>Gets the persistence receipt only when an attempt was authorized.</summary>
    public OwnerAppendReceipt<TFact>? Persistence { get; }

    /// <summary>Creates a receipt with persistence presence constrained by its disposition.</summary>
    /// <exception cref="ArgumentException">The disposition and nested receipt presence are inconsistent.</exception>
    public AdmissionReceipt(AdmissionDisposition disposition, AuthorizationDecision authorization, OwnerAppendReceipt<TFact>? persistence)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (disposition == AdmissionDisposition.None || !Enum.IsDefined(disposition) ||
            (disposition == AdmissionDisposition.Attempted) != (persistence is not null))
            throw new ArgumentException("Admission receipt components are inconsistent.");
        Disposition = disposition; Authorization = authorization; Persistence = persistence;
    }
}

/// <summary>Defines adapter-neutral current-action admission for one closed action/fact pair.</summary>
public interface ICurrentActionAdmission<TAction, TFact>
    where TAction : notnull where TFact : notnull
{
    /// <summary>Authorizes the action at its exact revision and invokes persistence only for a matching authorized decision.</summary>
    ValueTask<AdmissionReceipt<TFact>> AdmitAsync(AdmissionRequest<TAction, TFact> request, CancellationToken cancellationToken = default);
}
