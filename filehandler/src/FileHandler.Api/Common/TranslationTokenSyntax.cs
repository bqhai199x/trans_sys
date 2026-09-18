using System.Text;

namespace FileHandler.Api.Common;

/// <summary>
/// Shared wire syntax for translated text runs and protected anchors.
/// </summary>
internal static class TranslationTokenSyntax
{

    /// <summary>
    /// Builds canonical opening run token.
    /// </summary>
    /// <param name="id">Scoped run identifier, including r prefix.</param>
    /// <returns>Opening run token.</returns>
    internal static string Open(string id) => $"<ox:{id}>";

    /// <summary>
    /// Builds canonical closing run token.
    /// </summary>
    /// <param name="id">Scoped run identifier, including r prefix.</param>
    /// <returns>Closing run token.</returns>
    internal static string Close(string id) => $"</ox:{id}>";

    /// <summary>
    /// Builds canonical protected anchor token.
    /// </summary>
    /// <param name="id">Scoped anchor identifier, including k prefix.</param>
    /// <returns>Self-closing protected anchor.</returns>
    internal static string Anchor(string id) => $"<ox:{id}/>";

    /// <summary>
    /// Appends escaped literal text inside structured run tokens.
    /// </summary>
    /// <param name="builder">Destination builder.</param>
    /// <param name="text">Unescaped literal text.</param>
    /// <returns>No return value.</returns>
    internal static void AppendEscaped(StringBuilder builder, string text)
    {
        foreach (var character in text)
        {
            if (character is '\\' or '<')
                builder.Append('\\');
            builder.Append(character);
        }
    }

    /// <summary>
    /// Reads escaped text until expected closing run token.
    /// </summary>
    /// <param name="value">Structured translation.</param>
    /// <param name="offset">Current offset, advanced past closing token on success.</param>
    /// <param name="closing">Expected canonical closing token.</param>
    /// <param name="text">Decoded literal text.</param>
    /// <returns>True when escapes and closing token are valid.</returns>
    internal static bool TryReadText(string value, ref int offset, string closing, out string text)
    {
        var builder = new StringBuilder();
        text = string.Empty;
        while (offset < value.Length)
        {
            var character = value[offset++];
            if (character == '\\')
            {
                if (offset == value.Length || value[offset] is not ('\\' or '<'))
                    return false;
                builder.Append(value[offset++]);
            }
            else if (character == '<')
            {
                if (!value.AsSpan(offset - 1).StartsWith(closing, StringComparison.Ordinal))
                    return false;
                offset += closing.Length - 1;
                text = builder.ToString();
                return true;
            }
            else
                builder.Append(character);
        }
        return false;
    }
}
