using PermitTrack.Web.Domain;

namespace PermitTrack.Tests;

public class FeeCalculatorTests
{
    [Theory]
    [InlineData(0, 50)]
    [InlineData(2_000, 50)]
    [InlineData(2_001, 56)]         // any fraction of $1,000 counts as a full unit
    [InlineData(10_000, 98)]        // at the plan-review threshold, not over it
    [InlineData(10_001, 130)]       // 50 + 9×6 = 104, +25% plan review
    [InlineData(50_000, 422.50)]    // 50 + 48×6 = 338, +25%
    [InlineData(100_000, 672.50)]   // 338 + 50×4 = 538, +25%
    public void Building_fee_follows_valuation_tiers(decimal valuation, decimal expected) =>
        Assert.Equal(expected, FeeCalculator.Calculate(PermitType.Building, valuation).Total);

    [Theory]
    [InlineData(PermitType.Electrical, 0, 75)]
    [InlineData(PermitType.Plumbing, 2_200, 81)]
    [InlineData(PermitType.Mechanical, 9_800, 95)]
    public void Trade_permits_charge_base_plus_per_thousand(PermitType type, decimal valuation, decimal expected) =>
        Assert.Equal(expected, FeeCalculator.Calculate(type, valuation).Total);

    [Fact]
    public void Demolition_is_flat_regardless_of_valuation()
    {
        Assert.Equal(100m, FeeCalculator.Calculate(PermitType.Demolition, 0).Total);
        Assert.Equal(100m, FeeCalculator.Calculate(PermitType.Demolition, 1_000_000).Total);
    }

    [Fact]
    public void Breakdown_lines_add_up_to_total()
    {
        var fee = FeeCalculator.Calculate(PermitType.Building, 285_000);
        Assert.Equal(4, fee.Lines.Count);
        Assert.Equal(fee.Lines.Sum(l => l.Amount), fee.Total);
    }

    [Fact]
    public void Negative_valuation_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FeeCalculator.Calculate(PermitType.Building, -1));
}
