using HPD.Payments.Primitives.Identity;
using HPD.Payments.Contracts.ExternalEffect;

namespace HPD.Payments.Connectors.Stripe;

/// <summary>Names the exact next action after a Stripe transport outcome.</summary>
public enum StripeRetryDisposition
{
    /// <summary>No valid decision.</summary>
    None = 0,
    /// <summary>The request may be attempted with the identical account and semantic idempotency identity.</summary>
    SafeSameIdentity,
    /// <summary>Provider synchronization is required before another side-effecting attempt.</summary>
    SynchronizeRequired,
    /// <summary>Pinned credential, configuration, or API evidence is stale.</summary>
    RejectStale,
}

/// <summary>Evaluates retry without converting possible dispatch or account failover into non-occurrence.</summary>
public static class StripeRetryPolicy
{
    /// <summary>Returns a scoped decision for the exact plan and current revisions.</summary>
    public static StripeRetryDisposition Evaluate(StripeRequestPlan plan, ExternalEffectState transportState,
        Revision currentCredential, Revision currentConfiguration, Revision currentApi, bool idempotencyRetentionProven,
        bool accountFailoverRequested)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.CredentialRevision != currentCredential || plan.ConfigurationRevision != currentConfiguration ||
            plan.ApiRevision != currentApi) return StripeRetryDisposition.RejectStale;
        if (plan.Operation == StripeOperation.Retrieve || transportState == ExternalEffectState.NotDispatched)
            return accountFailoverRequested && plan.Operation != StripeOperation.Retrieve
                ? StripeRetryDisposition.SynchronizeRequired : StripeRetryDisposition.SafeSameIdentity;
        if (transportState == ExternalEffectState.PossibleDispatch)
            return idempotencyRetentionProven && !accountFailoverRequested
                ? StripeRetryDisposition.SafeSameIdentity : StripeRetryDisposition.SynchronizeRequired;
        return StripeRetryDisposition.SynchronizeRequired;
    }
}
