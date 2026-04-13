using System.Collections.Immutable;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Moves;

/// <summary>
/// How a player pays for a card. Steel applies to Building tags (2 MC each),
/// Titanium applies to Space tags (3 MC each). Heat payment is Helion-only.
/// CardResources maps source card ID to the number of resources spent from that card.
/// No refund for overpaying with steel/titanium/card resources.
/// </summary>
public sealed record PaymentInfo(
    int MegaCredits = 0,
    int Steel = 0,
    int Titanium = 0,
    int Heat = 0,
    ImmutableDictionary<string, int>? CardResources = null)
{
    public static readonly PaymentInfo Zero = new();

    /// <summary>
    /// Total effective MC value of this payment.
    /// CardResourceValues maps source card ID to the MC value per resource.
    /// </summary>
    public int TotalValue(
        int steelValue = Constants.SteelValue,
        int titaniumValue = Constants.TitaniumValue,
        ImmutableDictionary<string, int>? cardResourceValues = null)
    {
        var total = MegaCredits + (Steel * steelValue) + (Titanium * titaniumValue) + Heat;

        if (CardResources != null && cardResourceValues != null)
        {
            foreach (var (cardId, count) in CardResources)
            {
                if (cardResourceValues.TryGetValue(cardId, out var valuePerResource))
                    total += count * valuePerResource;
            }
        }

        return total;
    }
}
