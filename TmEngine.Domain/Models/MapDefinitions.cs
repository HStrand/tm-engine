using System.Collections.Immutable;

namespace TmEngine.Domain.Models;

/// <summary>
/// Complete definition of a Mars map, including all hex tiles with their types and bonuses.
/// </summary>
public sealed record MapDefinition(
    MapName Name,
    ImmutableDictionary<HexCoord, HexDefinition> Hexes,
    ImmutableArray<string> MilestoneNames,
    ImmutableArray<string> AwardNames,
    ImmutableArray<HexCoord> VolcanicAreas);

/// <summary>
/// Static definitions for all three in-scope maps: Tharsis, Hellas, Elysium.
/// Hex coordinates use odd-row offset right, matching the replay viewer convention.
/// </summary>
public static class MapDefinitions
{
    public static MapDefinition GetMap(MapName name) => name switch
    {
        MapName.Tharsis => Tharsis,
        MapName.Hellas => Hellas,
        MapName.Elysium => Elysium,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    // ═══════════════════════════════════════════════════════════
    //  THARSIS
    // ═══════════════════════════════════════════════════════════

    public static readonly MapDefinition Tharsis = BuildTharsis();

    private static MapDefinition BuildTharsis()
    {
        // Hex layout from replay viewer mapHexes.ts
        var layout = new Dictionary<int, int[]>
        {
            [1] = [3, 4, 5, 6, 7, 8],
            [2] = [3, 4, 5, 6, 7, 8, 9],
            [3] = [2, 3, 4, 5, 6, 7, 8],
            [4] = [2, 3, 4, 5, 6, 7, 8, 9],
            [5] = [1, 2, 3, 4, 5, 6, 7, 8, 9],
            [6] = [2, 3, 4, 5, 6, 7, 8, 9],
            [7] = [2, 3, 4, 5, 6, 7, 8],
            [8] = [3, 4, 5, 6, 7, 8],
            [9] = [3, 4, 5, 6, 7, 8],
        };

        // Named (reserved) locations
        var named = new Dictionary<string, HexCoord>
        {
            ["Tharsis Tholus"] = new(4, 2),
            ["Ascraeus Mons"] = new(2, 3),
            ["Pavonis Mons"] = new(2, 4),
            ["Arsia Mons"] = new(1, 5),
            ["Noctis City"] = new(3, 5),
        };

        // Ocean-reserved hexes (12 on Tharsis)
        var oceans = new HashSet<HexCoord>
        {
            new(8, 1), new(5, 2), new(6, 2),
            new(7, 3), new(8, 3),
            new(4, 4), new(8, 4),
            new(6, 5),
            new(7, 6), new(8, 6),
            new(4, 7), new(5, 7),
        };

        // Placement bonuses per hex
        var bonuses = new Dictionary<HexCoord, PlacementBonus[]>
        {
            // Row 1
            [new(3, 1)] = [PlacementBonus.Steel, PlacementBonus.Steel],
            [new(5, 1)] = [],
            [new(6, 1)] = [],
            [new(7, 1)] = [PlacementBonus.Cards],
            [new(8, 1)] = [PlacementBonus.Cards, PlacementBonus.Cards], // ocean
            // Row 2
            [new(4, 2)] = [], // Tharsis Tholus
            [new(5, 2)] = [PlacementBonus.Titanium], // ocean
            [new(6, 2)] = [], // ocean
            [new(9, 2)] = [PlacementBonus.Cards, PlacementBonus.Cards],
            // Row 3
            [new(2, 3)] = [], // Ascraeus Mons
            [new(3, 3)] = [PlacementBonus.Cards],
            [new(4, 3)] = [],
            [new(5, 3)] = [PlacementBonus.Steel],
            [new(6, 3)] = [],
            [new(7, 3)] = [], // ocean
            [new(8, 3)] = [], // ocean
            // Row 4
            [new(2, 4)] = [PlacementBonus.Plants], // Pavonis Mons
            [new(3, 4)] = [PlacementBonus.Plants],
            [new(4, 4)] = [PlacementBonus.Plants], // ocean
            [new(5, 4)] = [PlacementBonus.Plants, PlacementBonus.Plants],
            [new(6, 4)] = [PlacementBonus.Plants],
            [new(7, 4)] = [],
            [new(8, 4)] = [], // ocean
            [new(9, 4)] = [],
            // Row 5
            [new(1, 5)] = [PlacementBonus.Plants, PlacementBonus.Plants], // Arsia Mons
            [new(2, 5)] = [PlacementBonus.Plants, PlacementBonus.Plants],
            [new(3, 5)] = [], // Noctis City
            [new(4, 5)] = [PlacementBonus.Steel],
            [new(5, 5)] = [PlacementBonus.Steel],
            [new(6, 5)] = [], // ocean
            [new(7, 5)] = [],
            [new(8, 5)] = [],
            [new(9, 5)] = [],
            // Row 6
            [new(2, 6)] = [PlacementBonus.Plants],
            [new(3, 6)] = [PlacementBonus.Plants],
            [new(4, 6)] = [PlacementBonus.Plants],
            [new(7, 6)] = [], // ocean
            [new(8, 6)] = [], // ocean
            // Row 7
            [new(4, 7)] = [], // ocean
            [new(5, 7)] = [PlacementBonus.Titanium], // ocean
            [new(6, 7)] = [PlacementBonus.Steel],
            // Row 8
            [new(3, 8)] = [PlacementBonus.Steel],
            [new(4, 8)] = [PlacementBonus.Steel, PlacementBonus.Steel],
            [new(8, 8)] = [PlacementBonus.Cards],
            // Row 9
            [new(3, 9)] = [PlacementBonus.Titanium],
            [new(5, 9)] = [PlacementBonus.Steel],
            [new(6, 9)] = [],
            [new(7, 9)] = [PlacementBonus.Cards],
            [new(8, 9)] = [PlacementBonus.Titanium, PlacementBonus.Titanium],
        };

        var volcanoes = ImmutableArray.Create(
            new HexCoord(4, 2),  // Tharsis Tholus
            new HexCoord(2, 3),  // Ascraeus Mons
            new HexCoord(2, 4),  // Pavonis Mons
            new HexCoord(1, 5)); // Arsia Mons

        return BuildMap(MapName.Tharsis, layout, named, oceans, bonuses, volcanoes,
            milestones: ["Terraformer", "Mayor", "Gardener", "Builder", "Planner"],
            awards: ["Landlord", "Banker", "Scientist", "Thermalist", "Miner"]);
    }

    // ═══════════════════════════════════════════════════════════
    //  HELLAS
    // ═══════════════════════════════════════════════════════════

    public static readonly MapDefinition Hellas = BuildHellas();

    private static MapDefinition BuildHellas()
    {
        var layout = new Dictionary<int, int[]>
        {
            [1] = [3, 4, 5, 6, 7],
            [2] = [2, 3, 4, 5, 6, 7, 8],
            [3] = [2, 3, 4, 5, 6, 7, 8, 9],
            [4] = [1, 2, 3, 4, 5, 6, 7, 8, 9],
            [5] = [1, 2, 3, 4, 5, 6, 7, 8, 9],
            [6] = [2, 3, 4, 5, 6, 7, 8, 9],
            [7] = [2, 3, 4, 5, 6, 7, 8],
            [8] = [3, 4, 5, 6, 7, 8],
            [9] = [3, 4, 5, 6, 7],
        };

        var named = new Dictionary<string, HexCoord>
        {
            ["South Pole"] = new(5, 9),
        };

        // Ocean-reserved hexes on Hellas (9 total, different positions from Tharsis)
        var oceans = new HashSet<HexCoord>
        {
            new(5, 4), new(6, 4), new(7, 4),
            new(4, 5), new(5, 5), new(6, 5), new(7, 5),
            new(4, 6), new(5, 6),
        };

        var bonuses = new Dictionary<HexCoord, PlacementBonus[]>
        {
            // Row 1 - plant bonuses near equator
            [new(3, 1)] = [PlacementBonus.Plants, PlacementBonus.Plants],
            [new(4, 1)] = [PlacementBonus.Plants],
            [new(5, 1)] = [PlacementBonus.Plants],
            [new(6, 1)] = [PlacementBonus.Plants],
            [new(7, 1)] = [PlacementBonus.Plants, PlacementBonus.Plants],
            // Row 2
            [new(2, 2)] = [PlacementBonus.Plants, PlacementBonus.Plants],
            [new(3, 2)] = [PlacementBonus.Plants],
            [new(7, 2)] = [],
            [new(8, 2)] = [PlacementBonus.Plants],
            // Row 3
            [new(2, 3)] = [PlacementBonus.Steel],
            [new(3, 3)] = [PlacementBonus.Steel, PlacementBonus.Steel],
            [new(4, 3)] = [],
            [new(8, 3)] = [PlacementBonus.Steel],
            [new(9, 3)] = [PlacementBonus.Cards],
            // Row 4 - Hellas sea (ocean hexes have heat bonuses from thin crust)
            [new(1, 4)] = [PlacementBonus.Cards, PlacementBonus.Cards],
            [new(2, 4)] = [],
            [new(3, 4)] = [],
            [new(5, 4)] = [PlacementBonus.Heat, PlacementBonus.Heat], // ocean
            [new(6, 4)] = [PlacementBonus.Heat], // ocean
            [new(7, 4)] = [], // ocean
            // Row 5
            [new(1, 5)] = [PlacementBonus.Steel],
            [new(4, 5)] = [PlacementBonus.Heat, PlacementBonus.Heat], // ocean
            [new(5, 5)] = [], // ocean
            [new(6, 5)] = [], // ocean
            [new(7, 5)] = [], // ocean
            // Row 6
            [new(4, 6)] = [], // ocean
            [new(5, 6)] = [], // ocean
            [new(6, 6)] = [PlacementBonus.Titanium],
            // Row 7
            [new(2, 7)] = [PlacementBonus.Cards],
            [new(4, 7)] = [PlacementBonus.Steel],
            [new(7, 7)] = [PlacementBonus.Titanium],
            [new(8, 7)] = [PlacementBonus.Cards],
            // Row 8
            [new(3, 8)] = [PlacementBonus.Heat, PlacementBonus.Heat],
            [new(5, 8)] = [],
            [new(6, 8)] = [PlacementBonus.Heat, PlacementBonus.Heat],
            [new(7, 8)] = [PlacementBonus.Heat, PlacementBonus.Heat],
            [new(8, 8)] = [PlacementBonus.Heat, PlacementBonus.Heat],
            // Row 9 - near south pole, heat bonuses from CO2
            [new(3, 9)] = [PlacementBonus.Heat, PlacementBonus.Heat],
            [new(4, 9)] = [PlacementBonus.Heat, PlacementBonus.Heat],
            [new(5, 9)] = [PlacementBonus.Ocean], // South Pole - pay 6MC to gain ocean
            [new(6, 9)] = [PlacementBonus.Heat, PlacementBonus.Heat],
            [new(7, 9)] = [PlacementBonus.Heat, PlacementBonus.Heat],
        };

        // Hellas has no volcanic areas (Lava Flows can go anywhere)
        var volcanoes = ImmutableArray<HexCoord>.Empty;

        return BuildMap(MapName.Hellas, layout, named, oceans, bonuses, volcanoes,
            milestones: ["Diversifier", "Tactician", "Polar Explorer", "Energizer", "Rim Settler"],
            awards: ["Cultivator", "Magnate", "Space Baron", "Excentric", "Contractor"]);
    }

    // ═══════════════════════════════════════════════════════════
    //  ELYSIUM
    // ═══════════════════════════════════════════════════════════

    public static readonly MapDefinition Elysium = BuildElysium();

    private static MapDefinition BuildElysium()
    {
        var layout = new Dictionary<int, int[]>
        {
            [1] = [3, 4, 5, 6, 7, 8],
            [2] = [2, 3, 4, 5, 6, 7, 8],
            [3] = [2, 3, 4, 5, 6, 7, 8],
            [4] = [2, 3, 4, 5, 6, 7, 8, 9],
            [5] = [1, 2, 3, 4, 5, 6, 7, 8, 9],
            [6] = [2, 3, 4, 5, 6, 7, 8, 9],
            [7] = [2, 3, 4, 5, 6, 7, 8],
            [8] = [3, 4, 5, 6, 7, 8],
            [9] = [3, 4, 5, 6, 7],
        };

        var named = new Dictionary<string, HexCoord>
        {
            ["Hecates Tholus"] = new(3, 2),
            ["Elysium Mons"] = new(2, 3),
            ["Olympus Mons"] = new(8, 3),
            ["Arsia Mons"] = new(9, 5),
        };

        // Ocean-reserved hexes on Elysium
        var oceans = new HashSet<HexCoord>
        {
            new(3, 1), new(4, 1), new(5, 1), new(6, 1),
            new(2, 2), new(7, 2),
            new(3, 3), new(4, 3),
            new(2, 4),
        };

        var bonuses = new Dictionary<HexCoord, PlacementBonus[]>
        {
            // Row 1 - northern lowlands (ocean hexes)
            [new(3, 1)] = [], // ocean
            [new(4, 1)] = [], // ocean
            [new(5, 1)] = [PlacementBonus.Steel], // ocean
            [new(6, 1)] = [PlacementBonus.Cards], // ocean
            [new(7, 1)] = [],
            [new(8, 1)] = [],
            // Row 2
            [new(2, 2)] = [], // ocean
            [new(3, 2)] = [], // Hecates Tholus
            [new(4, 2)] = [],
            [new(5, 2)] = [PlacementBonus.Cards, PlacementBonus.Cards],
            [new(7, 2)] = [], // ocean
            [new(8, 2)] = [],
            // Row 3
            [new(2, 3)] = [], // Elysium Mons
            [new(3, 3)] = [PlacementBonus.Titanium], // ocean
            [new(4, 3)] = [], // ocean
            [new(5, 3)] = [],
            [new(6, 3)] = [PlacementBonus.Steel],
            [new(8, 3)] = [], // Olympus Mons
            // Row 4
            [new(2, 4)] = [], // ocean
            [new(3, 4)] = [],
            [new(4, 4)] = [PlacementBonus.Plants],
            [new(5, 4)] = [PlacementBonus.Plants],
            [new(6, 4)] = [PlacementBonus.Plants],
            [new(8, 4)] = [],
            // Row 5
            [new(1, 5)] = [PlacementBonus.Plants, PlacementBonus.Plants],
            [new(2, 5)] = [PlacementBonus.Plants],
            [new(3, 5)] = [PlacementBonus.Plants],
            [new(4, 5)] = [PlacementBonus.Plants, PlacementBonus.Plants],
            [new(5, 5)] = [PlacementBonus.Plants, PlacementBonus.Plants],
            [new(6, 5)] = [PlacementBonus.Plants],
            [new(9, 5)] = [], // Arsia Mons
            // Row 6
            [new(2, 6)] = [],
            [new(3, 6)] = [PlacementBonus.Plants],
            [new(4, 6)] = [PlacementBonus.Plants],
            [new(5, 6)] = [],
            [new(7, 6)] = [PlacementBonus.Steel],
            // Row 7
            [new(3, 7)] = [],
            [new(5, 7)] = [PlacementBonus.Titanium],
            [new(7, 7)] = [],
            [new(8, 7)] = [PlacementBonus.Steel, PlacementBonus.Steel],
            // Row 8
            [new(3, 8)] = [PlacementBonus.Steel],
            [new(4, 8)] = [PlacementBonus.Titanium],
            [new(6, 8)] = [],
            [new(7, 8)] = [PlacementBonus.Cards, PlacementBonus.Cards],
            [new(8, 8)] = [PlacementBonus.Steel],
            // Row 9
            [new(3, 9)] = [],
            [new(5, 9)] = [PlacementBonus.Titanium, PlacementBonus.Titanium],
            [new(6, 9)] = [],
            [new(7, 9)] = [],
        };

        // Elysium volcanic sites where Lava Flows can be placed
        var volcanoes = ImmutableArray.Create(
            new HexCoord(3, 2),  // Hecates Tholus
            new HexCoord(2, 3),  // Elysium Mons
            new HexCoord(8, 3),  // Olympus Mons
            new HexCoord(9, 5)); // Arsia Mons

        return BuildMap(MapName.Elysium, layout, named, oceans, bonuses, volcanoes,
            milestones: ["Generalist", "Specialist", "Ecologist", "Tycoon", "Legend"],
            awards: ["Celebrity", "Industrialist", "Desert Settler", "Estate Dealer", "Benefactor"]);
    }

    // ═══════════════════════════════════════════════════════════
    //  BUILDER
    // ═══════════════════════════════════════════════════════════

    private static MapDefinition BuildMap(
        MapName name,
        Dictionary<int, int[]> layout,
        Dictionary<string, HexCoord> namedLocations,
        HashSet<HexCoord> oceanHexes,
        Dictionary<HexCoord, PlacementBonus[]> bonuses,
        ImmutableArray<HexCoord> volcanoes,
        string[] milestones,
        string[] awards)
    {
        var nameByCoord = namedLocations.ToDictionary(kv => kv.Value, kv => kv.Key);
        var hexBuilder = ImmutableDictionary.CreateBuilder<HexCoord, HexDefinition>();

        foreach (var (row, cols) in layout)
        {
            foreach (var col in cols)
            {
                var coord = new HexCoord(col, row);
                var type = oceanHexes.Contains(coord) ? HexType.OceanReserved
                    : nameByCoord.ContainsKey(coord) ? HexType.Named
                    : HexType.Land;

                var hexBonuses = bonuses.TryGetValue(coord, out var b) && b.Length > 0
                    ? [.. b]
                    : ImmutableArray<PlacementBonus>.Empty;

                var reservedFor = nameByCoord.GetValueOrDefault(coord);

                hexBuilder.Add(coord, new HexDefinition(coord, type, hexBonuses, reservedFor));
            }
        }

        return new MapDefinition(
            name,
            hexBuilder.ToImmutable(),
            [.. milestones],
            [.. awards],
            volcanoes);
    }
}
