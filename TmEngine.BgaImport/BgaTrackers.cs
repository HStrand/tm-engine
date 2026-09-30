using TmEngine.Domain.Models;

namespace TmEngine.BgaImport;

/// <summary>Names of the per-player counters in a log snapshot. Scraper versions name the tag counters differently.</summary>
internal static class BgaTrackers
{
    public const string MegaCredits = "M€";
    public const string HandCount = "Hand Counter";

    public static readonly string[] PlayedEvents = ["Count of played Events cards", "Event tag"];

    /// <summary>The tags BGA keeps a counter for. Event tags are counted as played events instead.</summary>
    public static readonly Tag[] CountedTags =
    [
        Tag.Building, Tag.Space, Tag.Power, Tag.Science, Tag.Jovian, Tag.Earth,
        Tag.Plant, Tag.Microbe, Tag.Animal, Tag.City, Tag.Wild,
    ];

    public static string[] TagCount(Tag tag) => tag == Tag.Power
        ? ["Count of Power tags", "Energy tag", "Power tag"]
        : [$"Count of {tag} tags", $"{tag} tag"];

    /// <summary>The first of the given counters the snapshot has, or null when it has none of them.</summary>
    public static int? Read(Dictionary<string, int> trackers, params string[] names)
    {
        foreach (var name in names)
        {
            if (trackers.TryGetValue(name, out var value))
                return value;
        }
        return null;
    }
}
