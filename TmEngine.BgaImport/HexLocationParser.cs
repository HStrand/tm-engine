using System.Text.RegularExpressions;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport;

/// <summary>
/// Parses the location strings found in BGA game logs into engine hex coordinates.
/// Logs spell locations several ways ("Hex 4,6", "Hex at (5,8) (5,8)", "Olympus Mons (8,3)", "hex_3_5");
/// the coordinate pair is authoritative and the name is ignored, since names are misspelled in places.
/// </summary>
public static partial class HexLocationParser
{
    [GeneratedRegex(@"(\d+)\s*[,_]\s*(\d+)")]
    private static partial Regex CoordPair();

    /// <summary>
    /// Returns the coordinate in the string, or null when there is none.
    /// Column 0 is BGA's off-map area (Phobos Space Haven, Ganymede Colony); see <see cref="IsOffMap"/>.
    /// </summary>
    public static HexCoord? Parse(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        var matches = CoordPair().Matches(location);
        if (matches.Count == 0)
            return null;

        var last = matches[^1];
        return new HexCoord(int.Parse(last.Groups[1].Value), int.Parse(last.Groups[2].Value));
    }

    public static bool IsOffMap(HexCoord coord) => coord.Col == 0;
}
