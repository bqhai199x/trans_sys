(function (root) {
    'use strict';
    const multipart = typeof module === 'object' && module.exports ? require('./swagger-multipart.js') : root.FileHandlerMultipart;
    const maxInlineBytes = 32 * 1024;
    const maxPreviewChars = 16 * 1024;

    // Large JSON is never parsed or pretty-printed merely to display a preview.
    function preview(bytes) {
        const oversized = bytes.byteLength > maxInlineBytes;
        let text = new TextDecoder().decode(oversized ? bytes.subarray(0, maxPreviewChars) : bytes);
        if (!oversized) {
            try { text = JSON.stringify(JSON.parse(text), null, 2); } catch { /* Preserve bounded raw text. */ }
        }
        return { text: text.slice(0, maxPreviewChars), truncated: oversized || text.length > maxPreviewChars };
    }

    function prepare(bytes, contentType) {
        if (/^multipart\/mixed(?:\s*;|$)/i.test(contentType)) {
            const parts = multipart.parse(bytes, contentType);
            return { kind: 'multipart', parts, preview: preview(parts[0].bytes) };
        }
        return { kind: 'json', parts: [{ bytes, contentType, filename: 'response.json' }], preview: preview(bytes) };
    }

    const api = { prepare, preview, maxInlineBytes, maxPreviewChars };
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.FileHandlerResponse = api;
})(globalThis);
