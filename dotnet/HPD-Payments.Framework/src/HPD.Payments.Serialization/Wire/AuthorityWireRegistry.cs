namespace HPD.Payments.Serialization.Wire;

/// <summary>Provides the closed, explicit mapping between stable wire discriminators and 17 authorities.</summary>
public static class AuthorityWireRegistry
{
    private static readonly (AuthorityFamily Family, string Discriminator)[] Entries =
    [
        (AuthorityFamily.Agreement, "agreement"),
        (AuthorityFamily.EffectiveCommercialFact, "effective-commercial-fact"),
        (AuthorityFamily.ScopedIdentity, "scoped-identity"),
        (AuthorityFamily.MeasuredFact, "measured-fact"),
        (AuthorityFamily.MeasurementGeneration, "measurement-generation"),
        (AuthorityFamily.Valuation, "valuation"),
        (AuthorityFamily.Obligation, "obligation"),
        (AuthorityFamily.RequestedTransition, "requested-transition"),
        (AuthorityFamily.IssuanceFact, "issuance-fact"),
        (AuthorityFamily.HeldPosition, "held-position"),
        (AuthorityFamily.ValueMovement, "value-movement"),
        (AuthorityFamily.EntitlementGrantRemovalFact, "entitlement-grant-removal-fact"),
        (AuthorityFamily.RestrictionFact, "restriction-fact"),
        (AuthorityFamily.CapabilityEvidence, "capability-evidence"),
        (AuthorityFamily.ExternalEffect, "external-effect"),
        (AuthorityFamily.WorkRequirement, "work-requirement"),
        (AuthorityFamily.PublicationObligation, "publication-obligation"),
    ];

    /// <summary>Gets every admitted family and stable discriminator in frozen order.</summary>
    public static ReadOnlySpan<(AuthorityFamily Family, string Discriminator)> All => Entries;

    /// <summary>Resolves a stable discriminator using ordinal comparison.</summary>
    /// <param name="discriminator">Wire discriminator.</param>
    /// <param name="family">Resolved family when successful.</param>
    /// <returns>True only for one of the 17 explicit entries.</returns>
    public static bool TryResolve(string discriminator, out AuthorityFamily family)
    {
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.Discriminator, discriminator, StringComparison.Ordinal))
            {
                family = entry.Family;
                return true;
            }
        }

        family = default;
        return false;
    }

    /// <summary>Returns the stable discriminator for a known authority family.</summary>
    /// <param name="family">Known family.</param>
    /// <returns>Stable discriminator.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not an admitted family.</exception>
    public static string GetDiscriminator(AuthorityFamily family)
    {
        foreach (var entry in Entries)
            if (entry.Family == family) return entry.Discriminator;
        throw new ArgumentOutOfRangeException(nameof(family));
    }
}
