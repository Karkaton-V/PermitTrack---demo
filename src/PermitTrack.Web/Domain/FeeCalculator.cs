namespace PermitTrack.Web.Domain;

public record FeeLine(string Description, decimal Amount);

public record FeeBreakdown(IReadOnlyList<FeeLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.Amount);
}

/// <summary>
/// Sample fee schedule modeled on the valuation-based tables many building departments use.
/// Amounts are illustrative, not any jurisdiction's actual rates.
/// </summary>
public static class FeeCalculator
{
    public const decimal BuildingBaseFee = 50m;
    public const decimal BuildingBaseValuation = 2_000m;
    public const decimal BuildingTierOneLimit = 50_000m;
    public const decimal BuildingTierOneRate = 6m;   // per $1,000 (or fraction) from $2k to $50k
    public const decimal BuildingTierTwoRate = 4m;   // per $1,000 (or fraction) above $50k
    public const decimal PlanReviewThreshold = 10_000m;
    public const decimal PlanReviewRate = 0.25m;
    public const decimal TradeBaseFee = 75m;
    public const decimal TradeRate = 2m;             // per $1,000 (or fraction)
    public const decimal DemolitionFee = 100m;

    public static FeeBreakdown Calculate(PermitType type, decimal valuation)
    {
        if (valuation < 0)
            throw new ArgumentOutOfRangeException(nameof(valuation), "Valuation cannot be negative.");

        return type switch
        {
            PermitType.Building => Building(valuation),
            PermitType.Demolition => new([new("Demolition permit (flat)", DemolitionFee)]),
            _ => Trade(type, valuation)
        };
    }

    private static FeeBreakdown Building(decimal valuation)
    {
        var lines = new List<FeeLine> { new($"Base fee (first {BuildingBaseValuation:C0})", BuildingBaseFee) };

        var tierOne = PerThousand(Math.Min(valuation, BuildingTierOneLimit) - BuildingBaseValuation);
        if (tierOne > 0)
            lines.Add(new($"{tierOne} × {BuildingTierOneRate:C0} per $1,000 up to {BuildingTierOneLimit:C0}", tierOne * BuildingTierOneRate));

        var tierTwo = PerThousand(valuation - BuildingTierOneLimit);
        if (tierTwo > 0)
            lines.Add(new($"{tierTwo} × {BuildingTierTwoRate:C0} per $1,000 over {BuildingTierOneLimit:C0}", tierTwo * BuildingTierTwoRate));

        if (valuation > PlanReviewThreshold)
        {
            var permitFee = lines.Sum(l => l.Amount);
            lines.Add(new($"Plan review ({PlanReviewRate:P0} of permit fee)", Math.Round(permitFee * PlanReviewRate, 2)));
        }

        return new(lines);
    }

    private static FeeBreakdown Trade(PermitType type, decimal valuation)
    {
        var lines = new List<FeeLine> { new($"{type} permit base fee", TradeBaseFee) };
        var units = PerThousand(valuation);
        if (units > 0)
            lines.Add(new($"{units} × {TradeRate:C0} per $1,000 of valuation", units * TradeRate));
        return new(lines);
    }

    /// <summary>Number of $1,000 units, counting any fraction as a full unit.</summary>
    private static int PerThousand(decimal amount) => amount <= 0 ? 0 : (int)Math.Ceiling(amount / 1_000m);
}
