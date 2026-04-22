using System;
using FuzzySharp;

namespace WingetAppDeployer_WinUI.Helpers;

// Fuzzy-match helper voor de search-boxen. Gebruikt FuzzySharp's WeightedRatio
// (token-set + partial + full ratio gecombineerd) zodat we typo's, afkortingen
// en partiele matches afhandelen. Scores lopen van 0 tot 100.
internal static class FuzzyMatcher
{
    // Minimum score waarop een match wordt geaccepteerd. Lager = meer ruis,
    // hoger = typo's missen. 55 is een veilig middenmoot voor WeightedRatio
    // op korte queries zoals app-namen.
    public const int MinScore = 55;

    /// <summary>
    /// Score een query tegen meerdere tekst-velden en geef de maximum terug.
    /// Lege query → 0. Lege velden worden overgeslagen. Exacte substring match
    /// geeft altijd 100.
    /// </summary>
    public static int Score(string query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;

        var q = query.Trim();
        var best = 0;

        foreach (var raw in fields)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var f = raw.Trim();

            // Shortcut: exacte case-insensitive substring = 100. Vermijdt dat
            // "chrome" lager scoort dan "chr" door Levenshtein-rekenwerk.
            if (f.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                best = 100;
                continue;
            }

            var score = Fuzz.WeightedRatio(q.ToLowerInvariant(), f.ToLowerInvariant());
            if (score > best) best = score;
        }

        return best;
    }

    /// <summary>
    /// True als de query genoeg matcht op minstens één veld.
    /// </summary>
    public static bool Matches(string query, params string?[] fields) =>
        Score(query, fields) >= MinScore;
}
