using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Runtime.Card;

/// <summary>Names an append-only card lifecycle transition without implying provider occurrence.</summary>
public enum CardLifecycleChangeKind
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>Consumes authorized capacity into captured exposure.</summary>
    Capture,
    /// <summary>Releases unused authorized capacity.</summary>
    Void,
    /// <summary>Returns captured value.</summary>
    Refund,
    /// <summary>Places captured exposure into dispute.</summary>
    OpenDispute,
    /// <summary>Returns disputed exposure to ordinary captured exposure.</summary>
    ResolveDispute,
    /// <summary>Converts disputed exposure into chargeback loss.</summary>
    Chargeback,
}

/// <summary>Immutable, dimensioned conservation state for one card authorization lineage.</summary>
/// <remarks>This is a domain projection. Provider occurrence remains owned by External Effect authority.</remarks>
public sealed record CardLifecycleState
{
    /// <summary>Gets the stable lifecycle identity in Value Movement scope.</summary>
    public SemanticId LifecycleId { get; }
    /// <summary>Gets the exact lowercase unit.</summary>
    public string Unit { get; }
    /// <summary>Gets total authorized capacity.</summary>
    public decimal Authorized { get; }
    /// <summary>Gets cumulative captured capacity.</summary>
    public decimal Captured { get; }
    /// <summary>Gets cumulative voided capacity.</summary>
    public decimal Voided { get; }
    /// <summary>Gets cumulative refunded capacity.</summary>
    public decimal Refunded { get; }
    /// <summary>Gets currently disputed capacity.</summary>
    public decimal Disputed { get; }
    /// <summary>Gets cumulative chargeback capacity.</summary>
    public decimal ChargedBack { get; }
    /// <summary>Gets the exact owner generation.</summary>
    public OwnerGeneration Generation { get; }
    /// <summary>Gets the operation that produced this projection.</summary>
    public SemanticId LastOperationId { get; }
    /// <summary>Gets remaining capturable authorization.</summary>
    public decimal Capturable => Authorized - Captured - Voided;
    /// <summary>Gets captured exposure still available to refund or dispute.</summary>
    public decimal UnencumberedCaptured => Captured - Refunded - Disputed - ChargedBack;

    private CardLifecycleState(SemanticId lifecycleId, string unit, decimal authorized, decimal captured, decimal voided,
        decimal refunded, decimal disputed, decimal chargedBack, OwnerGeneration generation, SemanticId lastOperationId)
    {
        LifecycleId = lifecycleId; Unit = unit; Authorized = authorized; Captured = captured; Voided = voided;
        Refunded = refunded; Disputed = disputed; ChargedBack = chargedBack; Generation = generation; LastOperationId = lastOperationId;
        Validate();
    }

    /// <summary>Creates the initial authorization projection.</summary>
    public static CardLifecycleState Authorize(SemanticId lifecycleId, decimal amount, string unit, OwnerGeneration generation, SemanticId operationId) =>
        new(lifecycleId, unit, amount, 0m, 0m, 0m, 0m, 0m, generation, operationId);

    /// <summary>Rehydrates an exact previously admitted projection.</summary>
    public static CardLifecycleState Restore(SemanticId lifecycleId, string unit, decimal authorized, decimal captured, decimal voided,
        decimal refunded, decimal disputed, decimal chargedBack, OwnerGeneration generation, SemanticId lastOperationId) =>
        new(lifecycleId, unit, authorized, captured, voided, refunded, disputed, chargedBack, generation, lastOperationId);

    /// <summary>Applies one checked successor transition.</summary>
    public CardLifecycleState Apply(CardLifecycleChangeKind kind, decimal amount, SemanticId operationId)
    {
        if (kind == CardLifecycleChangeKind.None || !Enum.IsDefined(kind) || amount <= 0m || !operationId.IsValid || operationId.Scope != LifecycleId.Scope ||
            !Generation.TryNext(out var next)) throw new ArgumentException("Invalid card lifecycle transition.");
        return kind switch
        {
            CardLifecycleChangeKind.Capture when amount <= Capturable => Copy(captured: checked(Captured + amount)),
            CardLifecycleChangeKind.Void when amount <= Capturable => Copy(voided: checked(Voided + amount)),
            CardLifecycleChangeKind.Refund when amount <= UnencumberedCaptured => Copy(refunded: checked(Refunded + amount)),
            CardLifecycleChangeKind.OpenDispute when amount <= UnencumberedCaptured => Copy(disputed: checked(Disputed + amount)),
            CardLifecycleChangeKind.ResolveDispute when amount <= Disputed => Copy(disputed: Disputed - amount),
            CardLifecycleChangeKind.Chargeback when amount <= Disputed => Copy(disputed: Disputed - amount, chargedBack: checked(ChargedBack + amount)),
            _ => throw new InvalidOperationException("Card lifecycle capacity would be overstated or negative."),
        };

        CardLifecycleState Copy(decimal? captured = null, decimal? voided = null, decimal? refunded = null,
            decimal? disputed = null, decimal? chargedBack = null) => new(LifecycleId, Unit, Authorized, captured ?? Captured,
            voided ?? Voided, refunded ?? Refunded, disputed ?? Disputed, chargedBack ?? ChargedBack, next, operationId);
    }

    private void Validate()
    {
        if (!LifecycleId.IsValid || LifecycleId.Scope.Authority != "value-movement" || !LastOperationId.IsValid || LastOperationId.Scope != LifecycleId.Scope ||
            !Generation.IsValid || !ScopeId.TryCreate("unit", "unit", Unit, out _) || Authorized <= 0m || Captured < 0m || Voided < 0m ||
            Refunded < 0m || Disputed < 0m || ChargedBack < 0m || Capturable < 0m || UnencumberedCaptured < 0m)
            throw new ArgumentException("Card lifecycle violates identity, dimension or conservation invariants.");
    }
}
