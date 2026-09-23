using System.Text.RegularExpressions;

namespace FileHandler.Api.Common;

/// <summary>
/// Parses canonical tokens and validates movement across ownership regions.
/// </summary>
public static class TranslationTokenParser
{

    /// <summary>
    /// Canonical opening run and anchor grammar.
    /// </summary>
    private static readonly Regex Opening = new(@"\G<ox:(r(?:0|[1-9][0-9]*))>|\G<ox:([kb](?:0|[1-9][0-9]*))/>", RegexOptions.CultureInvariant);

    /// <summary>
    /// Decodes structured text without repairing malformed syntax.
    /// </summary>
    /// <param name="text">Structured source or candidate.</param>
    /// <returns>Parts in encounter order.</returns>
    /// <exception cref="FormatException">Token syntax or escaping is invalid.</exception>
    public static IReadOnlyList<TranslationTokenPart> Parse(string text)
    {
        var parts = new List<TranslationTokenPart>();
        var offset = 0;
        while (offset < text.Length)
        {
            var match = Opening.Match(text, offset);
            if (!match.Success) throw new FormatException("token_syntax");
            offset += match.Length;
            var id = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            string? value = null;
            if (id[0] == 'r' && !TranslationTokenSyntax.TryReadText(text, ref offset, TranslationTokenSyntax.Close(id), out value))
                throw new FormatException("token_syntax");
            parts.Add(new(id, value));
        }
        return parts;
    }

    /// <summary>
    /// Checks complete token identity, barriers and contiguous movement regions.
    /// </summary>
    /// <param name="source">Original encoded unit.</param>
    /// <param name="candidate">Translated encoded unit.</param>
    /// <param name="fixedOrder">Whether token order must remain unchanged.</param>
    /// <returns>Null when valid; stable error code otherwise.</returns>
    public static string? Validate(string source, string candidate, bool fixedOrder = false)
    {
        if (!TranslationTokenSyntax.IsStructured(source))
            return !string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(candidate) ? "empty_translation" : null;
        try
        {
            var before = Parse(source);
            var after = Parse(candidate);
            var ids = before.Select(p => p.Id).ToArray();
            if (ids.Distinct().Count() != ids.Length) return "token_identity";
            if (!ids.Order().SequenceEqual(after.Select(p => p.Id).Order())) return "token_identity";
            var regions = new Dictionary<string, int>();
            var region = 0;
            foreach (var part in before)
            {
                var barrier = part.Id[0] == 'b';
                if (barrier) region++;
                regions[part.Id] = region;
                if (barrier) region++;
            }
            if (fixedOrder && !ids.SequenceEqual(after.Select(p => p.Id))) return "token_order";
            if (!before.Select(p => regions[p.Id]).SequenceEqual(after.Select(p => regions[p.Id]))) return "token_region";
            if (before.Any(p => !string.IsNullOrWhiteSpace(p.Text)) && after.All(p => string.IsNullOrWhiteSpace(p.Text))) return "empty_translation";
            return null;
        }
        catch (FormatException) { return "token_syntax"; }
    }
}
