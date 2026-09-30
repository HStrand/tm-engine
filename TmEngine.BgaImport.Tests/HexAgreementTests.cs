using TmEngine.Domain.Models;

namespace TmEngine.BgaImport.Tests;

/// <summary>
/// Checks that BGA's hex coordinates mean the same hexes as the engine's, using real placements.
/// </summary>
public class HexAgreementTests
{
    [Theory]
    [InlineData("Hex 4,6", 4, 6)]
    [InlineData("Hex 6,5 (6,5)", 6, 5)]
    [InlineData("Hex at (5,8) (5,8)", 5, 8)]
    [InlineData("Tharsis Hex 3,1 (3,1)", 3, 1)]
    [InlineData("Olympus Mons (8,3)", 8, 3)]
    [InlineData("Hecatus Tholus (3,2)", 3, 2)]
    [InlineData("hex_7_4", 7, 4)]
    public void Parse_ReadsTheCoordinatePair(string location, int col, int row)
    {
        Assert.Equal(new HexCoord(col, row), HexLocationParser.Parse(location));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Phobos Space Haven")]
    public void Parse_ReturnsNull_WhenThereIsNoCoordinate(string? location)
    {
        Assert.Null(HexLocationParser.Parse(location));
    }

    [Theory]
    [MemberData(nameof(Fixtures.OnSupportedMaps), MemberType = typeof(Fixtures))]
    public void EveryOnMapPlacement_IsOnAnEngineHex(string fixture)
    {
        var (log, map) = LoadWithMap(fixture);

        var offBoard = Placements(log)
            .Where(p => !HexLocationParser.IsOffMap(p.Coord) && !map.Hexes.ContainsKey(p.Coord))
            .Select(p => $"move {p.Move.MoveNumber}: {p.Move.TilePlaced} at {p.Move.TileLocation}")
            .ToList();

        Assert.Empty(offBoard);
    }

    [Theory]
    [MemberData(nameof(Fixtures.OnSupportedMaps), MemberType = typeof(Fixtures))]
    public void Oceans_LandOnOceanReservedHexes(string fixture)
    {
        var (log, map) = LoadWithMap(fixture);

        // Artificial Lake is the one card that puts an ocean on a land hex.
        var misplaced = Placements(log)
            .Where(p => p.Move.TilePlaced == "Ocean"
                && map.Hexes.TryGetValue(p.Coord, out var hex) && hex.Type != HexType.OceanReserved
                && !PlacedByArtificialLake(log, p.Move))
            .Select(p => $"move {p.Move.MoveNumber}: ocean at {p.Move.TileLocation}")
            .ToList();

        Assert.Empty(misplaced);
    }

    [Theory]
    [MemberData(nameof(Fixtures.OnSupportedMaps), MemberType = typeof(Fixtures))]
    public void SteelAndTitaniumPlacementBonuses_MatchEngineMap(string fixture)
    {
        var (log, map) = LoadWithMap(fixture);

        var mismatches = new List<string>();
        foreach (var (move, coord) in Placements(log))
        {
            if (!map.Hexes.TryGetValue(coord, out var hex) || BonusObscuredByCardEffect.Contains((fixture, move.MoveNumber)))
                continue;

            foreach (var (bonus, resource) in new[] { (PlacementBonus.Steel, "Steel"), (PlacementBonus.Titanium, "Titanium") })
            {
                var expected = hex.Bonuses.Count(b => b == bonus);
                var actual = TrackerDelta(log, move, resource);
                if (expected != actual)
                    mismatches.Add($"move {move.MoveNumber} at {coord}: engine {expected} {resource}, log {actual} | {move.Description}");
            }
        }

        Assert.Empty(mismatches);
    }

    /// <summary>
    /// Placements whose move also logs steel or titanium from the card that triggered them.
    /// Move 200: Deimos Down's 4 steel is logged with the bonus ocean its temperature raise earned.
    /// </summary>
    private static readonly HashSet<(string Fixture, int? MoveNumber)> BonusObscuredByCardEffect =
    [
        (Fixtures.Tharsis3p, 200),
    ];

    private static (BgaGameLog Log, MapDefinition Map) LoadWithMap(string fixture)
    {
        var log = Fixtures.Load(fixture);
        var map = MapResolver.Resolve(log).Map ?? throw new InvalidOperationException($"{fixture} has no supported map.");
        return (log, MapDefinitions.GetMap(map));
    }

    private static IEnumerable<(BgaMove Move, HexCoord Coord)> Placements(BgaGameLog log) =>
        from move in log.Moves
        where move.TilePlaced is not null && move.ActionType != "cancelled"
        let coord = HexLocationParser.Parse(move.TileLocation)
        where coord is not null
        select (move, coord.Value);

    private static bool PlacedByArtificialLake(BgaGameLog log, BgaMove placement)
    {
        if (placement.Reason?.Contains("Artificial Lake") == true || placement.Description.Contains("Artificial Lake"))
            return true;

        // The card play is its own move just before the placement.
        var index = log.Moves.IndexOf(placement);
        return log.Moves.Take(index).Reverse()
            .Where(m => m.PlayerId == placement.PlayerId && m.CardPlayed is not null)
            .Select(m => m.CardPlayed)
            .FirstOrDefault() == "Artificial Lake";
    }

    private static readonly string[] FollowUpActionTypes = ["other", "immediate_effect", "game_state_change"];

    /// <summary>
    /// Change in the placing player's stock of a resource caused by the placement. The card behind the placement
    /// is paid for in an earlier move, and the hex's bonus is often logged in the follow-up moves after it.
    /// </summary>
    private static int? TrackerDelta(BgaGameLog log, BgaMove move, string resource)
    {
        int? Stock(BgaMove m) =>
            m.GameState?.PlayerTrackers is { } trackers
            && trackers.TryGetValue(move.PlayerId, out var player)
            && player.TryGetValue(resource, out var value) ? value : null;

        var index = log.Moves.IndexOf(move);
        var before = log.Moves.Take(index).Reverse().Select(Stock).FirstOrDefault(v => v is not null);

        var last = index;
        while (last + 1 < log.Moves.Count
            && log.Moves[last + 1].PlayerId == move.PlayerId
            && FollowUpActionTypes.Contains(log.Moves[last + 1].ActionType))
        {
            last++;
        }

        return Stock(log.Moves[last]) - before;
    }
}
