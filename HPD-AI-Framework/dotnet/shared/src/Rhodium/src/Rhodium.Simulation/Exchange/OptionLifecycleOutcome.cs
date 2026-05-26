using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Exchange;

public abstract record OptionLifecycleOutcome
{
    protected OptionLifecycleOutcome(
        Qty quantity,
        Instant appliedAt,
        OptionLifecycleReferenceSource referenceSource,
        string reason,
        bool allowNoReferenceSource = false)
    {
        if (quantity.IsZero)
            throw new ArgumentException("Option lifecycle outcome requires a nonzero quantity.", nameof(quantity));

        if (!Enum.IsDefined(referenceSource))
            throw new ArgumentOutOfRangeException(nameof(referenceSource), referenceSource, "Unknown option lifecycle reference source.");

        if (!allowNoReferenceSource && referenceSource == OptionLifecycleReferenceSource.None)
            throw new ArgumentException("Resolved option lifecycle outcome requires a non-None reference source.", nameof(referenceSource));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Option lifecycle outcome requires a reason.", nameof(reason));

        Quantity = quantity;
        AppliedAt = appliedAt;
        ReferenceSource = referenceSource;
        Reason = reason;
    }

    public Qty Quantity { get; }
    public Instant AppliedAt { get; }
    public OptionLifecycleReferenceSource ReferenceSource { get; }
    public string Reason { get; }

    public sealed record Block : OptionLifecycleOutcome
    {
        public Block(Qty quantity, Instant appliedAt, string reason)
            : base(quantity, appliedAt, OptionLifecycleReferenceSource.None, reason, allowNoReferenceSource: true)
        {
        }
    }

    public sealed record ExpireWorthless : OptionLifecycleOutcome
    {
        public ExpireWorthless(
            Qty quantity,
            Price referencePrice,
            Instant appliedAt,
            OptionLifecycleReferenceSource referenceSource,
            string reason)
            : base(quantity, appliedAt, referenceSource, reason)
        {
            ValidatePositiveQuantity(quantity, nameof(quantity), "Worthless option expiry requires a long option quantity.");
            ReferencePrice = referencePrice;
        }

        public Price ReferencePrice { get; }
    }

    public sealed record ExpireUnexercised : OptionLifecycleOutcome
    {
        public ExpireUnexercised(
            Qty quantity,
            Price referencePrice,
            Instant appliedAt,
            OptionLifecycleReferenceSource referenceSource,
            string reason)
            : base(quantity, appliedAt, referenceSource, reason)
        {
            ValidatePositiveQuantity(quantity, nameof(quantity), "Unexercised option expiry requires a long option quantity.");
            ReferencePrice = referencePrice;
        }

        public Price ReferencePrice { get; }
    }

    public sealed record ExpireUnassigned : OptionLifecycleOutcome
    {
        public ExpireUnassigned(
            Qty quantity,
            Price referencePrice,
            Instant appliedAt,
            OptionLifecycleReferenceSource referenceSource,
            string reason)
            : base(quantity, appliedAt, referenceSource, reason)
        {
            ValidateNegativeQuantity(quantity, nameof(quantity), "Unassigned option expiry requires a short option quantity.");
            ReferencePrice = referencePrice;
        }

        public Price ReferencePrice { get; }
    }

    public sealed record CashSettle : OptionLifecycleOutcome
    {
        public CashSettle(
            OptionLifecycleKind lifecycleKind,
            Qty quantity,
            Price referencePrice,
            Instant appliedAt,
            OptionLifecycleReferenceSource referenceSource,
            string reason)
            : base(quantity, appliedAt, referenceSource, reason)
        {
            ValidateSettlementLifecycleKind(lifecycleKind);
            ValidateSettlementQuantity(lifecycleKind, quantity);
            LifecycleKind = lifecycleKind;
            ReferencePrice = referencePrice;
        }

        public OptionLifecycleKind LifecycleKind { get; }
        public Price ReferencePrice { get; }
    }

    public sealed record PhysicalDeliver : OptionLifecycleOutcome
    {
        public PhysicalDeliver(
            OptionLifecycleKind lifecycleKind,
            Qty quantity,
            Price referencePrice,
            Instant appliedAt,
            OptionLifecycleReferenceSource referenceSource,
            string reason,
            string premiumReason)
            : base(quantity, appliedAt, referenceSource, reason)
        {
            ValidateSettlementLifecycleKind(lifecycleKind);
            ValidateSettlementQuantity(lifecycleKind, quantity);
            if (string.IsNullOrWhiteSpace(premiumReason))
                throw new ArgumentException("Physical option lifecycle outcome requires a premium reason.", nameof(premiumReason));

            LifecycleKind = lifecycleKind;
            ReferencePrice = referencePrice;
            PremiumReason = premiumReason;
        }

        public OptionLifecycleKind LifecycleKind { get; }
        public Price ReferencePrice { get; }
        public string PremiumReason { get; }
    }

    private static void ValidateSettlementLifecycleKind(OptionLifecycleKind lifecycleKind)
    {
        if (lifecycleKind is not (OptionLifecycleKind.Exercise or OptionLifecycleKind.Assignment))
            throw new ArgumentException("Option settlement lifecycle outcome requires Exercise or Assignment lifecycle kind.", nameof(lifecycleKind));
    }

    private static void ValidateSettlementQuantity(OptionLifecycleKind lifecycleKind, Qty quantity)
    {
        if (lifecycleKind == OptionLifecycleKind.Exercise)
            ValidatePositiveQuantity(quantity, nameof(quantity), "Exercise lifecycle outcome requires a long option quantity.");
        else
            ValidateNegativeQuantity(quantity, nameof(quantity), "Assignment lifecycle outcome requires a short option quantity.");
    }

    private static void ValidatePositiveQuantity(Qty quantity, string paramName, string message)
    {
        if (!quantity.IsPositive)
            throw new ArgumentException(message, paramName);
    }

    private static void ValidateNegativeQuantity(Qty quantity, string paramName, string message)
    {
        if (quantity.Value >= 0m)
            throw new ArgumentException(message, paramName);
    }
}
