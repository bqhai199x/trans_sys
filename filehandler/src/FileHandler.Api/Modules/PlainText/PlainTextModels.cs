using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.PlainText;

/// <summary>
/// Paragraph span in decoded source text, excluding final line terminator.
/// </summary>
/// <param name="Start">Inclusive zero-based UTF-16 offset.</param>
/// <param name="End">Exclusive zero-based UTF-16 offset.</param>
/// <param name="Line">Inclusive one-based source line range.</param>
internal sealed record PlainTextUnit(int Start, int End, SourceLineRange Line);
