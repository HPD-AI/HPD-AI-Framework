using Rhodium.Events;
using Rhodium.Options;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Exchange;

/// <summary>
/// Side-effect-free option lifecycle decision component.
/// </summary>
public sealed class OptionLifecycleProcessor
{
    private readonly IOptionAssignmentModel _assignmentModel;

    public OptionLifecycleProcessor(IOptionAssignmentModel? assignmentModel = null)
    {
        _assignmentModel = assignmentModel ?? DefaultOptionAssignmentModel.Instance;
    }

    public OptionLifecycleResult Process(OptionLifecycleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Quantity.Value < 0m)
            return ProcessShort(request);

        return ProcessLong(request);
    }

    private OptionLifecycleResult ProcessLong(OptionLifecycleRequest request)
    {
        if (request.Reference.Price is not { } referencePrice)
            return Single(Block(request));

        var terms = GetTerms(request.Contract);
        var isInTheMoney = IsOptionInTheMoney(terms, referencePrice);
        if (!ShouldAutoExerciseLongOption(terms))
        {
            if (!isInTheMoney)
            {
                return Single(new OptionLifecycleOutcome.ExpireWorthless(
                    request.Quantity,
                    referencePrice,
                    request.Now,
                    request.Reference.Source,
                    "Out of the money at expiry."));
            }

            return Single(new OptionLifecycleOutcome.ExpireUnexercised(
                request.Quantity,
                referencePrice,
                request.Now,
                request.Reference.Source,
                "In the money but not exercised by contract policy."));
        }

        var isPhysical = terms.SettlementStyle == OptionSettlementStyle.Physical ||
            request.Contract.Settlement is SettlementTerms.Physical;
        if (isPhysical)
        {
            if (isInTheMoney)
            {
                return Single(new OptionLifecycleOutcome.PhysicalDeliver(
                    OptionLifecycleKind.Exercise,
                    request.Quantity,
                    referencePrice,
                    request.Now,
                    request.Reference.Source,
                    "In the money at expiry.",
                    "Premium settlement at physical expiry."));
            }

            return Single(new OptionLifecycleOutcome.ExpireWorthless(
                request.Quantity,
                referencePrice,
                request.Now,
                request.Reference.Source,
                "Out of the money at expiry."));
        }

        if (!isInTheMoney)
        {
            return Single(new OptionLifecycleOutcome.ExpireWorthless(
                request.Quantity,
                referencePrice,
                request.Now,
                request.Reference.Source,
                "Out of the money at expiry."));
        }

        return Single(new OptionLifecycleOutcome.CashSettle(
            OptionLifecycleKind.Exercise,
            request.Quantity,
            referencePrice,
            request.Now,
            request.Reference.Source,
            "Cash settled at expiry."));
    }

    private OptionLifecycleResult ProcessShort(OptionLifecycleRequest request)
    {
        if (request.Reference.Price is not { } referencePrice)
            return Single(Block(request));

        var terms = GetTerms(request.Contract);
        var shortQuantity = new Qty(Math.Abs(request.Quantity.Value));
        var decision = _assignmentModel.GetAssignment(new OptionAssignmentContext(
            request.Contract,
            shortQuantity,
            new OptionMarketState(request.Contract.Instrument, request.Now, UnderlyingMark: referencePrice),
            request.Now,
            request.AssignmentInput?.AssignmentRule,
            request.AssignmentInput?.IsSelectedForRandomAssignment,
            request.AssignmentInput?.ProRataAssignmentRatio));

        if (!decision.IsAssigned)
        {
            return Single(new OptionLifecycleOutcome.ExpireUnassigned(
                request.Quantity,
                referencePrice,
                request.Now,
                request.Reference.Source,
                request.AssignmentInput?.Reason ?? decision.Reason ?? "Short option was not assigned."));
        }

        var assignedAbs = Math.Min(decision.Quantity.Value, shortQuantity.Value);
        var assignedQuantity = new Qty(-assignedAbs);
        var unassignedQuantity = new Qty(request.Quantity.Value + assignedAbs);
        var outcomes = new List<OptionLifecycleOutcome>(unassignedQuantity.IsZero ? 1 : 2);
        var isPhysical = terms.SettlementStyle == OptionSettlementStyle.Physical ||
            request.Contract.Settlement is SettlementTerms.Physical;
        if (isPhysical)
        {
            outcomes.Add(new OptionLifecycleOutcome.PhysicalDeliver(
                OptionLifecycleKind.Assignment,
                assignedQuantity,
                referencePrice,
                request.Now,
                request.Reference.Source,
                request.AssignmentInput?.Reason ?? decision.Reason ?? "Short option assigned at expiry.",
                "Premium settlement at physical assignment."));
        }
        else
        {
            outcomes.Add(new OptionLifecycleOutcome.CashSettle(
                OptionLifecycleKind.Assignment,
                assignedQuantity,
                referencePrice,
                request.Now,
                request.Reference.Source,
                request.AssignmentInput?.Reason ?? decision.Reason ?? "Cash settled at expiry."));
        }

        if (!unassignedQuantity.IsZero)
        {
            outcomes.Add(new OptionLifecycleOutcome.ExpireUnassigned(
                unassignedQuantity,
                referencePrice,
                request.Now,
                request.Reference.Source,
                "Unassigned short option quantity expired."));
        }

        return new OptionLifecycleResult(outcomes);
    }

    private static OptionTerms GetTerms(InstrumentContract contract)
        => ((PayoffTerms.Option)contract.Payoff).Terms;

    private static bool ShouldAutoExerciseLongOption(OptionTerms terms)
        => terms.ExercisePolicy switch
        {
            OptionExercisePolicy.Manual => false,
            OptionExercisePolicy.AutoExerciseInTheMoney => true,
            OptionExercisePolicy.CashSettledAtExpiry => true,
            OptionExercisePolicy.VenueDefined => true,
            _ => throw new InvalidOperationException($"Unknown option exercise policy {terms.ExercisePolicy}.")
        };

    private static bool IsOptionInTheMoney(OptionTerms terms, Price underlyingMark)
    {
        var strike = terms.Strike.ScaledStrike.Value;
        return terms.Right switch
        {
            OptionRight.Call => underlyingMark.Value > strike,
            OptionRight.Put => underlyingMark.Value < strike,
            _ => throw new InvalidOperationException($"Unknown option right {terms.Right}.")
        };
    }

    private static OptionLifecycleResult Single(OptionLifecycleOutcome outcome)
        => new([outcome]);

    private static OptionLifecycleOutcome.Block Block(OptionLifecycleRequest request)
        => new(
            request.Quantity,
            request.Now,
            request.Reference.BlockReason ?? "Missing option lifecycle reference price.");
}
