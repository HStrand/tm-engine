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
            titaniumUsed = Math.Min(availableTitanium, remaining / titaniumValue);
            remaining -= titaniumUsed * titaniumValue;
        }

        if (canUseSteel && availableSteel > 0)
        {
            steelUsed = Math.Min(availableSteel, remaining / steelValue);
            remaining -= steelUsed * steelValue;
        }

        // Use MC first, then heat only for what MC can't cover
        if (canUseHeat && availableHeat > 0)
        {
            var mcShortfall = Math.Max(0, remaining - availableMC);
            heatUsed = Math.Min(availableHeat, mcShortfall);
            remaining -= heatUsed;
        }

        return new PaymentInfo(Math.Max(0, remaining), steelUsed, titaniumUsed, heatUsed);
    }
}
