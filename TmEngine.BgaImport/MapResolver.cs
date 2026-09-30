using TmEngine.Domain.Models;

namespace TmEngine.BgaImport;

/// <summary>
/// Works out which engine map a BGA game was played on. The log's own map field is wrong or missing
/// in some parses, so it is cross-checked against the milestones and awards that appear in the game.
/// </summary>
public static class MapResolver
{
    public sealed record Result(MapName? Map, string? Problem);

    /// <summary>Normalised BGA spelling -> normalised engine spelling.</summary>
    private static readonly Dictionary<string, string> NameAliases = new()
    {
        ["eccentric"] = "excentric",
    };

    private static readonly MapName[] SupportedMaps = [MapName.Tharsis, MapName.Hellas, MapName.Elysium];

    public static Result Resolve(BgaGameLog log)
    {
        var seen = MilestoneAndAwardNamesIn(log);

        MapName? labelled = null;
        if (!string.IsNullOrWhiteSpace(log.Map))
        {
            if (!Enum.TryParse<MapName>(log.Map.Trim(), ignoreCase: true, out var parsed) || !SupportedMaps.Contains(parsed))
                return new Result(null, $"Map '{log.Map}' is not supported by the engine.");
            labelled = parsed;
        }

        if (labelled is { } map)
        {
            var foreign = seen.Where(n => !NamesOf(map).Contains(n)).ToList();
            return foreign.Count == 0
                ? new Result(map, null)
                : new Result(null, $"Game is labelled {map} but has milestones/awards from another map: {string.Join(", ", foreign)}.");
        }

        // No label: accept only when the milestones and awards seen identify exactly one map.
        if (seen.Count == 0)
            return new Result(null, "Map is not recorded and no milestones or awards were claimed to identify it.");

        var candidates = SupportedMaps.Where(m => seen.All(NamesOf(m).Contains)).ToList();
        return candidates.Count == 1
            ? new Result(candidates[0], null)
            : new Result(null, "Map is not recorded and its milestones/awards do not match a supported map.");
    }

    /// <summary>
    /// The engine's spelling of a milestone or award on the given map, or null when the map has none by that name.
    /// </summary>
    public static string? ResolveMilestoneOrAward(MapName map, string bgaName)
    {
        var key = NormalizeName(bgaName);
        var def = MapDefinitions.GetMap(map);
        return def.MilestoneNames.Concat(def.AwardNames).FirstOrDefault(n => CardNameMap.Normalize(n) == key);
    }

    private static HashSet<string> NamesOf(MapName map)
    {
        var def = MapDefinitions.GetMap(map);
        return def.MilestoneNames.Concat(def.AwardNames).Select(CardNameMap.Normalize).ToHashSet();
    }

    private static HashSet<string> MilestoneAndAwardNamesIn(BgaGameLog log)
    {
        var names = new HashSet<string>();

        // The last snapshot that has any claims carries all of them; the final move's can be empty.
        foreach (var snapshot in log.Moves.Select(m => m.GameState).Where(s => s is not null))
        {
            foreach (var name in snapshot!.Milestones?.Keys ?? Enumerable.Empty<string>())
                names.Add(NormalizeName(name));
            foreach (var name in snapshot.Awards?.Keys ?? Enumerable.Empty<string>())
                names.Add(NormalizeName(name));
        }

        return names;
    }

    private static string NormalizeName(string bgaName)
    {
        var key = CardNameMap.Normalize(bgaName);
        return NameAliases.GetValueOrDefault(key, key);
    }
}
