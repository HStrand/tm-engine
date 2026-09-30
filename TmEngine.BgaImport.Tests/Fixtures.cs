using System.Collections.Concurrent;

namespace TmEngine.BgaImport.Tests;

/// <summary>
/// Real game logs scraped from Board Game Arena, minified. One per map / player count / log format.
/// </summary>
public static class Fixtures
{
    public const string Elysium2p = "game_824655675_86296239.json";
    public const string Hellas2p = "game_741102170_93234993.json";
    public const string Tharsis2p = "game_825224074_83900777.json";

    /// <summary>
    /// Elysium, two players. Many placements are filed under the wrong player, and StrandedKnight's Capital is played
    /// but its placement was never logged.
    /// </summary>
    public const string ElysiumMisfiledTiles = "game_921600960_86296239.json";

    /// <summary>Three players, no Prelude, no Corporate Era. Not importable: only two-player games are.</summary>
    public const string Tharsis3p = "game_538921998_93915802.json";

    /// <summary>Played on Vastitas Borealis but the log's map field says Tharsis.</summary>
    public const string MislabelledVastitas = "game_829956648_86296239_mislabelled.json";

    /// <summary>Old scraper version: no hands, pile counts or special tiles.</summary>
    public const string OldFormat = "game_691299249_old_format.json";

    /// <summary>Logs on engine-supported maps in a current scraper format.</summary>
    public static readonly TheoryData<string> OnSupportedMaps = new() { Elysium2p, Hellas2p, Tharsis2p, Tharsis3p };

    private static readonly ConcurrentDictionary<string, BgaGameLog> Cache = new();

    private static readonly ConcurrentDictionary<string, ForkPointsResult> ForkPointsCache = new();

    /// <summary>The fork points of an unmodified fixture. Finding them validates every candidate, so it is done once.</summary>
    public static ForkPointsResult ForkPoints(string name) =>
        ForkPointsCache.GetOrAdd(name, n => BgaImporter.FindForkPoints(Load(n)));

    public static BgaGameLog Load(string name) => Cache.GetOrAdd(name, n =>
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", n));
        return BgaGameLog.Parse(stream);
    });
}
