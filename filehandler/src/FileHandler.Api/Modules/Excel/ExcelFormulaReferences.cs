using System.Text;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Tokenizes sheet qualifiers while preserving formula literals and other syntax.
/// </summary>
internal static class ExcelFormulaReferences
{

    /// <summary>
    /// Rewrites recognized internal sheet qualifiers from original names simultaneously.
    /// </summary>
    /// <param name="formula">Original formula or internal hyperlink target.</param>
    /// <param name="names">Original-to-final sheet name map.</param>
    /// <param name="rewritten">Rewritten value when reference syntax is safe.</param>
    /// <returns>False for dynamic, external, 3D or unrecognized sheet reference syntax.</returns>
    internal static bool TryRewrite(string formula, IReadOnlyDictionary<string, string> names, out string rewritten)
    {
        var result = new StringBuilder(formula.Length);
        var position = 0;
        rewritten = formula;
        while (position < formula.Length)
        {
            var start = position;
            var character = formula[position];
            if (character == '"')
            {
                position++;
                var closed = false;
                while (position < formula.Length)
                {
                    if (formula[position++] != '"') continue;
                    if (position < formula.Length && formula[position] == '"') { position++; continue; }
                    closed = true;
                    break;
                }
                if (!closed) return false;
                result.Append(formula, start, position - start);
                continue;
            }
            if (character == '\'')
            {
                if (start > 0 && formula[start - 1] is ':' or ']') return false;
                var name = new StringBuilder();
                position++;
                var closed = false;
                while (position < formula.Length)
                {
                    var next = formula[position++];
                    if (next != '\'') { name.Append(next); continue; }
                    if (position < formula.Length && formula[position] == '\'') { name.Append('\''); position++; continue; }
                    closed = true;
                    break;
                }
                if (!closed || position == formula.Length || formula[position] != '!' || name.ToString().IndexOfAny([':', '[', ']']) >= 0)
                    return false;
                if (!names.TryGetValue(name.ToString(), out var replacement)) return false;
                result.Append(replacement.Equals(name.ToString(), StringComparison.Ordinal) ? formula[start..position] : Quote(replacement)).Append('!');
                position++;
                continue;
            }
            if (char.IsLetterOrDigit(character) || character is '_' or '\\' or '$')
            {
                position++;
                while (position < formula.Length && (char.IsLetterOrDigit(formula[position]) || formula[position] is '_' or '.' or '\\' or '$')) position++;
                var token = formula[start..position];
                var functionPosition = position;
                while (functionPosition < formula.Length && char.IsWhiteSpace(formula[functionPosition])) functionPosition++;
                if (functionPosition < formula.Length && formula[functionPosition] == '(' &&
                    (token.EndsWith("INDIRECT", StringComparison.OrdinalIgnoreCase) || token.EndsWith("HYPERLINK", StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (position < formula.Length && formula[position] == '!')
                {
                    if (start > 0 && formula[start - 1] is ':' or ']' || !names.TryGetValue(token, out var replacement)) return false;
                    result.Append(replacement.Equals(token, StringComparison.Ordinal) ? token : Quote(replacement)).Append('!');
                    position++;
                }
                else result.Append(token);
                continue;
            }
            if (character == '!') return false;
            result.Append(character);
            position++;
        }
        rewritten = result.ToString();
        return true;
    }

    /// <summary>
    /// Locates all static qualifiers in unsupported formulas, including complete 3D sheet ranges.
    /// </summary>
    /// <param name="formula">Formula rejected by rewrite parser.</param>
    /// <param name="sheetNames">Original workbook names in source order.</param>
    /// <param name="affected">Names whose renames must be preserved.</param>
    /// <returns>False when dynamic or external syntax prevents proving affected scope.</returns>
    internal static bool TryFindAffectedSheets(string formula, IReadOnlyList<string> sheetNames, out HashSet<string> affected)
    {
        affected = new(StringComparer.OrdinalIgnoreCase);
        var positions = sheetNames.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index, StringComparer.OrdinalIgnoreCase);
        var position = 0;
        while (position < formula.Length)
        {
            var character = formula[position];
            if (character == '"')
            {
                position++;
                var closed = false;
                while (position < formula.Length)
                {
                    if (formula[position++] != '"') continue;
                    if (position < formula.Length && formula[position] == '"') { position++; continue; }
                    closed = true;
                    break;
                }
                if (!closed) return false;
                continue;
            }
            if (character is '[' or ']' or '!') return false;
            if (character != '\'' && !IsNameCharacter(character)) { position++; continue; }
            if (!ReadName(formula, ref position, out var name)) return false;
            if (position < formula.Length && formula[position] == ':')
            {
                position++;
                if (!ReadName(formula, ref position, out var last)) return false;
                name += ":" + last;
            }
            var next = position;
            while (next < formula.Length && char.IsWhiteSpace(formula[next])) next++;
            if (next < formula.Length && formula[next] == '(' &&
                (name.EndsWith("INDIRECT", StringComparison.OrdinalIgnoreCase) || name.EndsWith("HYPERLINK", StringComparison.OrdinalIgnoreCase))) return false;
            if (position >= formula.Length || formula[position] != '!') continue;
            position++;
            var ends = name.Split(':');
            if (ends.Length > 2 || !positions.TryGetValue(ends[0], out var firstIndex) || !positions.TryGetValue(ends[^1], out var lastIndex)) return false;
            for (var index = Math.Min(firstIndex, lastIndex); index <= Math.Max(firstIndex, lastIndex); index++) affected.Add(sheetNames[index]);
        }
        return affected.Count > 0;
    }

    /// <summary>
    /// Reads one unquoted or apostrophe-escaped sheet qualifier token.
    /// </summary>
    /// <param name="formula">Original formula.</param>
    /// <param name="position">Current offset, advanced past token.</param>
    /// <param name="name">Unescaped token text.</param>
    /// <returns>True for a complete nonempty token.</returns>
    private static bool ReadName(string formula, ref int position, out string name)
    {
        name = "";
        if (position >= formula.Length) return false;
        if (formula[position] != '\'')
        {
            var start = position;
            while (position < formula.Length && IsNameCharacter(formula[position])) position++;
            name = formula[start..position];
            return name.Length > 0;
        }
        position++;
        var builder = new StringBuilder();
        while (position < formula.Length)
        {
            var character = formula[position++];
            if (character != '\'') { builder.Append(character); continue; }
            if (position < formula.Length && formula[position] == '\'') { builder.Append('\''); position++; continue; }
            name = builder.ToString();
            return name.Length > 0;
        }
        return false;
    }

    /// <summary>
    /// Recognizes conservative unquoted formula-name characters.
    /// </summary>
    /// <param name="character">UTF-16 character to inspect.</param>
    /// <returns>True when character can belong to an unquoted name.</returns>
    private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character is '_' or '.' or '\\' or '$';

    /// <summary>
    /// Quotes a sheet qualifier using Excel apostrophe escaping.
    /// </summary>
    /// <param name="name">Final worksheet name.</param>
    /// <returns>Quoted qualifier without exclamation separator.</returns>
    private static string Quote(string name) => "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";
}
