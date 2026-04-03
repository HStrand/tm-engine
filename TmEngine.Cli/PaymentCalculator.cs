namespace TmEngine.Cli;

public record PaymentInfo(int MegaCredits, int Steel, int Titanium, int Heat);

public static class PaymentCalculator
{
    public static PaymentInfo Calculate(
        int effectiveCost,
        bool canUseSteel, bool canUseTitanium, bool canUseHeat,
        int availableMC, int availableSteel, int availableTitanium, int availableHeat,
        int steelValue = 2, int titaniumValue = 3)
    {
        int remaining = effectiveCost;
        int titaniumUsed = 0, steelUsed = 0, heatUsed = 0;

        if (canUseTitanium && availableTitanium > 0)
        {
            titaniumUsed = Math.Min(availableTitanium, (remaining + titaniumValue - 1) / titaniumValue);
            remaining = Math.Max(0, remaining - titaniumUsed * titaniumValue);
        }

        if (canUseSteel && availableSteel > 0)
        {
            steelUsed = Math.Min(availableSteel, (remaining + steelValue - 1) / steelValue);
            remaining = Math.Max(0, remaining - steelUsed * steelValue);
        }

        if (canUseHeat && availableHeat > 0)
        {
            heatUsed = Math.Min(availableHeat, remaining);
            remaining -= heatUsed;
        }

        return new PaymentInfo(Math.Max(0, remaining), steelUsed, titaniumUsed, heatUsed);
    }
}
