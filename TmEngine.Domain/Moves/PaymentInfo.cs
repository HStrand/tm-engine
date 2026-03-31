using TmEngine.Domain.Models;

namespace TmEngine.Domain.Moves;

/// <summary>
/// How a player pays for a card. Steel applies to Building tags (2 MC each),
/// Titanium applies to Space tags (3 MC each). Heat payment is Helion-only.
/// No refund for overpaying with steel/titanium.
/// </summary>
public sealed record PaymentInfo(
    int MegaCredits = 0,
    int Steel = 0,
    int Titanium = 0,
    int Heat = 0)
{
    public static readonly PaymentInfo Zero = new();

    /// <summary>
    /// Total effective MC value of this payment.
    /// </summary>
    public int TotalValue(int steelValue = Constants.SteelValue, int titaniumValue = Constants.TitaniumValue) =>
        MegaCredits + (Steel * steelValue) + (Titanium * titaniumValue) + Heat;
}
