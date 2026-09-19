using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Builds ordered templates while retaining every physical scalar binding.
/// </summary>
internal sealed class OfficeTemplateBuilder
{

    /// <summary>
    /// Active template quotas.
    /// </summary>
    private readonly OfficeProcessingOptions _limits;

    /// <summary>
    /// Retained source text characters.
    /// </summary>
    private long _characters;

    /// <summary>
    /// Creates a bounded template builder.
    /// </summary>
    /// <param name="limits">Active limits, or defaults.</param>
    internal OfficeTemplateBuilder(OfficeProcessingOptions? limits = null) => _limits = limits ?? new();

    /// <summary>
    /// Mutable slots in source order.
    /// </summary>
    private readonly List<OfficeTextSlot> _slots = [];

    /// <summary>
    /// Linear accumulation buffers for coalesced text runs.
    /// </summary>
    private readonly List<StringBuilder> _texts = [];

    /// <summary>
    /// Protected source objects.
    /// </summary>
    private readonly List<OfficeProtectedAnchor> _anchors = [];

    /// <summary>
    /// Scalar bindings for editable text.
    /// </summary>
    private readonly List<OfficeTextBinding> _bindings = [];

    /// <summary>
    /// Interleaved token order.
    /// </summary>
    private readonly List<string> _order = [];

    /// <summary>
    /// Cached source scalar hashes for split control spans.
    /// </summary>
    private readonly Dictionary<OpenXmlElement, string> _scalarHashes = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Cached source XML hashes for repeated protected spans.
    /// </summary>
    private readonly Dictionary<OpenXmlElement, string> _anchorHashes = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Whether previous token can coalesce with another text node.
    /// </summary>
    private bool _merge;

    /// <summary>
    /// Adds a text node with exact address and formatting context.
    /// </summary>
    /// <param name="node">Source text scalar.</param>
    /// <param name="partUri">Containing part URI.</param>
    /// <param name="fingerprint">Complete explicit formatting and ownership context.</param>
    /// <returns>No return value.</returns>
    internal void Text(OpenXmlLeafTextElement node, string partUri, string fingerprint)
    {
        var start = 0;
        if (node is not DocumentFormat.OpenXml.Spreadsheet.Text)
            for (var i = 0; i < node.Text.Length; i++)
                if (node.Text[i] is '\r' or '\n' or '\t')
                {
                    AppendText(node, partUri, fingerprint, start, i - start);
                    Anchor(node, AnchorKind.RawTextControl);
                    start = i + 1;
                }
        AppendText(node, partUri, fingerprint, start, node.Text.Length - start);
    }

    /// <summary>
    /// Appends text and same-style spaces without absorbing protected control characters.
    /// </summary>
    /// <param name="node">Complete source scalar.</param>
    /// <param name="partUri">Containing part URI.</param>
    /// <param name="fingerprint">Explicit format and owner context.</param>
    /// <param name="offset">Span offset in UTF-16 characters.</param>
    /// <param name="length">Span length in UTF-16 characters.</param>
    /// <returns>No return value.</returns>
    private void AppendText(OpenXmlLeafTextElement node, string partUri, string fingerprint, int offset, int length)
    {
        if (length == 0) return;
        var value = node.Text.Substring(offset, length);
        _characters += length;
        if (_characters > _limits.MaxPlanChars) throw new FileHandler.Api.Common.FileLimitException("office_plan_limit_exceeded");
        var canMerge = _merge && _slots[^1].FormatFingerprint == fingerprint;
        if (string.IsNullOrWhiteSpace(value) && (!canMerge || value.IndexOfAny(['\r', '\n', '\t']) >= 0))
        {
            Anchor(node, AnchorKind.Whitespace);
            return;
        }
        string slotId;
        if (canMerge)
        {
            var previous = _slots[^1];
            slotId = previous.SlotId;
            _texts[^1].Append(value);
        }
        else
        {
            CheckTokenBudget();
            slotId = $"r{_slots.Count}";
            _slots.Add(new(slotId, "", fingerprint));
            _texts.Add(new StringBuilder(value));
            _order.Add(slotId);
        }
        _merge = true;
        if (!_scalarHashes.TryGetValue(node, out var sourceHash))
            _scalarHashes[node] = sourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(node.Text)));
        if (_bindings.Count >= _limits.MaxBindings) throw new FileHandler.Api.Common.FileLimitException("office_plan_limit_exceeded");
        _bindings.Add(new(partUri, new OfficeLocation(partUri, OfficeTextBindings.Path(node)), node.LocalName,
            sourceHash, offset, length, slotId, node.Text));
    }

    /// <summary>
    /// Adds a protected token and ends text coalescing.
    /// </summary>
    /// <param name="node">Protected source node.</param>
    /// <param name="kind">Protection category.</param>
    /// <returns>No return value.</returns>
    internal void Anchor(OpenXmlElement node, AnchorKind kind)
    {
        CheckTokenBudget();
        var id = $"k{_anchors.Count}";
        if (!_anchorHashes.TryGetValue(node, out var sourceHash))
            _anchorHashes[node] = sourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(node.OuterXml)));
        _anchors.Add(new(id, kind, sourceHash));
        _order.Add(id);
        _merge = false;
    }

    /// <summary>
    /// Rejects token growth before retaining another entry.
    /// </summary>
    /// <returns>No return value.</returns>
    private void CheckTokenBudget()
    {
        if (_slots.Count + _anchors.Count >= _limits.MaxTokensPerUnit)
            throw new FileHandler.Api.Common.FileLimitException("office_plan_limit_exceeded");
    }

    /// <summary>
    /// Produces template when editable text exists.
    /// </summary>
    /// <returns>Ordered template, or null for protected-only content.</returns>
    internal OfficeTextTemplate? Build() => _slots.Count == 0 ? null : new(
        _slots.Count == 1 && _anchors.Count == 0 ? UnitMode.Plain : UnitMode.Structured,
        _slots.Select((slot, index) => slot with { OriginalText = _texts[index].ToString() }).ToArray(), _anchors.ToArray(), _bindings.ToArray(), _order.ToArray());
}
