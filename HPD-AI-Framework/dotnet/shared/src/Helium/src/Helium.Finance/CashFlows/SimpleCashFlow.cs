namespace Helium.Finance.CashFlows;

public readonly record struct SimpleCashFlow
{
    private readonly double _amount;

    public SimpleCashFlow(DateOnly PaymentDate, double Amount)
    {
        _amount = default;

        this.PaymentDate = PaymentDate;
        this.Amount = Amount;
    }

    public DateOnly PaymentDate { get; init; }

    public double Amount
    {
        get => _amount;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Cash-flow amount must be finite.");

            _amount = value;
        }
    }

    public void Validate()
    {
        if (!double.IsFinite(Amount))
            throw new ArgumentOutOfRangeException(nameof(Amount), "Cash-flow amount must be finite.");
    }

    public void Deconstruct(out DateOnly PaymentDate, out double Amount)
    {
        PaymentDate = this.PaymentDate;
        Amount = this.Amount;
    }
}
