using System.Collections.Frozen;

namespace FileHandler.Api.Modules.Office;

public sealed partial class OfficeSource
{

    /// <summary>
    /// Owned bytes read only by package processors within this request.
    /// </summary>
    internal byte[] Bytes { get; }

    /// <summary>
    /// Decompressed part hashes captured by completed package inspection.
    /// </summary>
    internal FrozenDictionary<string, string> PayloadHashes { get; set; } = FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// Completed source schema baseline without retained XML trees.
    /// </summary>
    internal OfficeSchemaBaseline? SchemaBaseline { get; set; }

    /// <summary>
    /// Whether extraction materializes informational response entries.
    /// </summary>
    internal bool IncludeInformationalSkips { get; set; } = true;

    /// <summary>
    /// Compact preservation facts retained independently of response skip projection.
    /// </summary>
    internal OfficeSkipCollector? ExtractionSkips { get; set; }

    /// <summary>
    /// Creates snapshot with explicit source buffer ownership.
    /// </summary>
    /// <param name="bytes">Source buffer to own or copy.</param>
    /// <param name="sourceHash">Source SHA-256 digest.</param>
    /// <param name="format">Detected Office format.</param>
    /// <param name="limits">Active processing limits.</param>
    /// <param name="profileVersion">Profile version identifier.</param>
    /// <param name="takeOwnership">Whether caller transfers exclusive buffer ownership.</param>
    private OfficeSource(byte[] bytes, string sourceHash, OfficeFormat format, OfficeProcessingOptions limits, string profileVersion, bool takeOwnership)
    {
        Bytes = takeOwnership ? bytes : bytes.ToArray();
        SourceHash = sourceHash;
        Format = format;
        Limits = limits;
        ProfileVersion = profileVersion;
    }

    /// <summary>
    /// Transfers reader-owned bytes without another source-sized allocation.
    /// </summary>
    /// <param name="bytes">Exclusive buffer never modified after this call.</param>
    /// <param name="sourceHash">Source SHA-256 digest.</param>
    /// <param name="format">Detected Office format.</param>
    /// <param name="limits">Active processing limits.</param>
    /// <returns>Request-local immutable source snapshot.</returns>
    internal static OfficeSource TakeOwnership(byte[] bytes, string sourceHash, OfficeFormat format, OfficeProcessingOptions limits) =>
        new(bytes, sourceHash, format, limits, "office-v1", true);
}

/// <summary>
/// Immutable schema baseline reusable only with matching XML reader limits.
/// </summary>
/// <param name="MaxXmlCharactersPerPart">Reader limit used while validating source.</param>
/// <param name="ErrorCount">Total schema findings in completed validation.</param>
/// <param name="ErrorCounts">Finding-key multiset detached from source XML nodes.</param>
internal sealed record OfficeSchemaBaseline(long MaxXmlCharactersPerPart, int ErrorCount, FrozenDictionary<string, int> ErrorCounts);
