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
    public const int MinScore = 75;

    /// <summary>
    /// Score een query tegen meerdere tekst-velden en geef de maximum terug.
    /// Lege query → 0. Lege velden worden overgeslagen.
    /// Exacte substring = 100, prefix = 90, anders Fuzz.Ratio (strikte Levenshtein).
    /// WeightedRatio/token_set wordt NIET gebruikt — die is te permissief voor
    /// korte namen (matcht "steam" op "teams" omdat ze dezelfde letters hebben).
    /// </summary>
    public static int Score(string query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;

        var q = query.Trim();
        var qLow = q.ToLowerInvariant();
        // Tokenize op whitespace zodat een multi-word query als "google chrome" of
        // "anti gravity" als losse termen behandeld kan worden. Single-token queries
        // behouden de originele behavior (substring → prefix → PartialRatio).
        var qTokens = qLow.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var best = 0;

        foreach (var raw in fields)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var f = raw.Trim();
            var fLow = f.ToLowerInvariant();

            // Single-token: originele substring-ladder.
            if (qTokens.Length <= 1)
            {
                if (fLow.Contains(qLow)) { best = 100; continue; }
                if (fLow.StartsWith(qLow)) { if (90 > best) best = 90; continue; }
                var s = Fuzz.PartialRatio(qLow, fLow);
                if (s > best) best = s;
                continue;
            }

            // Multi-token: alle tokens moeten als substring in het veld voorkomen
            // (order-independent). Daarmee matcht "anti gravity" → "Antigravity"
            // en "chrome google" → "Google Chrome" zonder dat we afhangen van
            // PartialRatio's quirks rondom spaties.
            var allMatch = true;
            foreach (var token in qTokens)
            {
                if (token.Length == 0) continue;
                if (!fLow.Contains(token)) { allMatch = false; break; }
            }
            if (allMatch) { best = 100; continue; }

            // Niet alle tokens matchen → val terug op PartialRatio over de hele
            // query. Vangt typo's op die over spaties heen lopen.
            var score = Fuzz.PartialRatio(qLow, fLow);
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
