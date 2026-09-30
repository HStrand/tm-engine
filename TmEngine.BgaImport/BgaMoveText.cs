using System.Text.RegularExpressions;

namespace TmEngine.BgaImport;

/// <summary>
/// Facts that the scraper leaves only in a move's free-text description.
/// A description is a list of log lines joined by " | ".
/// </summary>
public static partial class BgaMoveText
{
    /// <summary>A card turned over from the deck by a "reveal until" effect; rejected cards go to the discard pile.</summary>
    public sealed record Reveal(string CardName, bool Kept);

    [GeneratedRegex(@"reveals (.+?): it (has|does not have) an? \w+ tag")]
    private static partial Regex RevealLine();

    public static IEnumerable<string> Lines(BgaMove move) =>
        move.Description.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [GeneratedRegex(@"^(.+) gains (\d+) TR\b")]
    private static partial Regex TerraformGainLine();

    /// <summary>Terraform rating gains logged in this move, by the player name as the log spells it.</summary>
    public static IEnumerable<(string PlayerName, int Steps)> TerraformGains(BgaMove move) =>
        from line in Lines(move)
        let match = TerraformGainLine().Match(line)
        where match.Success
        select (match.Groups[1].Value, int.Parse(match.Groups[2].Value));

    /// <summary>Cards revealed from the deck in this move, in the order they were turned over.</summary>
    public static IEnumerable<Reveal> Reveals(BgaMove move) =>
        from line in Lines(move)
        let match = RevealLine().Match(line)
        where match.Success
        select new Reveal(match.Groups[1].Value, Kept: match.Groups[2].Value == "has");
}
