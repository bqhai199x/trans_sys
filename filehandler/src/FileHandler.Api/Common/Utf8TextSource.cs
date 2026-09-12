namespace FileHandler.Api.Common;

/// <summary>
/// Original UTF-8 bytes and decoded text metadata.
/// </summary>
/// <param name="Bytes">Original bytes including optional BOM.</param>
/// <param name="Text">Decoded text without leading BOM.</param>
/// <param name="HasBom">Whether source starts with UTF-8 BOM.</param>
internal sealed record Utf8TextSource(byte[] Bytes, string Text, bool HasBom);
