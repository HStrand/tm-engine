using System.Collections.Frozen;
using System.Text;
using TmEngine.Domain.Cards;

namespace TmEngine.BgaImport;

/// <summary>
/// Resolves card names as they appear in BGA game logs to engine card ids.
/// Matching ignores case and punctuation; spelling differences are handled by <see cref="Aliases"/>.
/// </summary>
public static class CardNameMap
{
    /// <summary>Normalised BGA spelling -> normalised engine spelling.</summary>
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["alliedbank"] = "alliedbanks",
        ["biolabs"] = "biolab",
        ["ceosfavouriteproject"] = "ceosfavoriteproject",
        ["excentricsponsor"] = "eccentricsponsor",
    };

    private static readonly Lazy<FrozenDictionary<string, string>> IdByName = new(() =>
        CardRegistry.All.Values.ToFrozenDictionary(e => Normalize(e.Definition.Name), e => e.Definition.Id));

    public static bool TryResolve(string bgaName, out string cardId)
    {
        var key = Normalize(bgaName);
        if (Aliases.TryGetValue(key, out var alias))
            key = alias;
        return IdByName.Value.TryGetValue(key, out cardId!);
    }

    public static string? Resolve(string bgaName) => TryResolve(bgaName, out var id) ? id : null;

    public static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
