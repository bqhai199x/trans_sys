namespace FileHandler.Api.Common;

/// <summary>
/// Decoded run or protected anchor in encounter order.
/// </summary>
/// <param name="Id">Canonical run or anchor identifier.</param>
/// <param name="Text">Decoded run text; null for anchors.</param>
public sealed record TranslationTokenPart(string Id, string? Text);
