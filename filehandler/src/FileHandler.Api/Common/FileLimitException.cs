namespace FileHandler.Api.Common;

/// <summary>
/// Reports a resource budget failure without input content.
/// </summary>
internal sealed class FileLimitException : InvalidOperationException
{

    /// <summary>
    /// Stable error code for HTTP and service results.
    /// </summary>
    internal string Code { get; }

    /// <summary>
    /// Creates a typed budget failure.
    /// </summary>
    /// <param name="code">Stable resource limit code.</param>
    internal FileLimitException(string code) : base("Configured resource limit exceeded.") => Code = code;
}
