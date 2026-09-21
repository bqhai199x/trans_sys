(function (root) {
    'use strict';
    const encoder = new TextEncoder();
    const decoder = new TextDecoder('utf-8', { fatal: true });

    function matches(bytes, marker, offset) {
        if (offset + marker.length > bytes.length) return false;
        for (let index = 0; index < marker.length; index++) {
            if (bytes[offset + index] !== marker[index]) return false;
        }
        return true;
    }

    function find(bytes, marker, start, accept = () => true) {
        for (let offset = bytes.indexOf(marker[0], start); offset >= 0 && offset <= bytes.length - marker.length; offset = bytes.indexOf(marker[0], offset + 1)) {
            if (matches(bytes, marker, offset) && accept(offset + marker.length)) return offset;
        }
        return -1;
    }

    function filename(disposition) {
        const unicode = /(?:^|;)\s*filename\*\s*=\s*(?:"([^"]*)"|([^;]*))/i.exec(disposition || '');
        const plain = /(?:^|;)\s*filename\s*=\s*(?:"((?:\\.|[^"\\])*)"|([^;]*))/i.exec(disposition || '');
        let name = plain ? (plain[1] ?? plain[2]).replace(/\\(.)/g, '$1').trim() : '';
        if (unicode) {
            const value = (unicode[1] ?? unicode[2]).trim();
            const encoded = /^utf-8'[^']*'(.*)$/i.exec(value);
            if (encoded) {
                try { name = decodeURIComponent(encoded[1]); } catch { /* Keep ASCII fallback. */ }
            }
        }
        return name.replace(/\\/g, '/').split('/').pop().replace(/[\x00-\x1f\x7f]/g, '') || 'download';
    }

    // Only MIME headers and explicit JSON parts are decoded; binary attachments remain bytes.
    function parse(bytes, contentType) {
        const match = /(?:^|;)\s*boundary\s*=\s*(?:"([^"]+)"|([^;\s]+))/i.exec(contentType || '');
        if (!/^multipart\/mixed(?:\s*;|$)/i.test(contentType || '') || !match) throw new Error('Thiếu boundary của multipart/mixed.');
        const boundary = match[1] || match[2];
        if (/[\r\n]/.test(boundary)) throw new Error('Boundary không hợp lệ.');
        const first = encoder.encode('--' + boundary + '\r\n');
        const delimiter = encoder.encode('\r\n--' + boundary);
        const crlf = encoder.encode('\r\n');
        const closing = encoder.encode('--');
        const headerEnd = encoder.encode('\r\n\r\n');
        if (!matches(bytes, first, 0)) throw new Error('Không tìm thấy phần đầu multipart.');
        let offset = first.length;
        const parts = [];
        while (offset < bytes.length) {
            const headersEnd = find(bytes, headerEnd, offset);
            if (headersEnd < 0) throw new Error('Header multipart chưa đầy đủ.');
            const headers = {};
            for (const line of decoder.decode(bytes.subarray(offset, headersEnd)).split('\r\n')) {
                const separator = line.indexOf(':');
                if (separator < 1) throw new Error('Header multipart không hợp lệ.');
                const key = line.slice(0, separator).trim().toLowerCase();
                if (Object.hasOwn(headers, key)) throw new Error('Header multipart bị lặp.');
                headers[key] = line.slice(separator + 1).trim();
            }
            const bodyStart = headersEnd + headerEnd.length;
            const end = find(bytes, delimiter, bodyStart, after => matches(bytes, crlf, after) ||
                matches(bytes, closing, after) && (after + 2 === bytes.length || matches(bytes, crlf, after + 2)));
            if (end < 0) throw new Error('Nội dung multipart chưa đầy đủ.');
            const id = headers['content-id'];
            const type = headers['content-type'] || 'application/octet-stream';
            parts.push({ id, contentType: type, filename: headers['content-disposition'] ? filename(headers['content-disposition']) : null, bytes: bytes.subarray(bodyStart, end) });
            offset = end + delimiter.length;
            if (matches(bytes, closing, offset)) {
                if (parts.length !== 2 || parts[0].id !== '<metadata>' || !['<file>', '<units>'].includes(parts[1].id)) throw new Error('Thứ tự phần multipart không đúng contract.');
                return parts;
            }
            offset += crlf.length;
        }
        throw new Error('Thiếu boundary kết thúc multipart.');
    }

    function json(part) {
        if (!/^application\/json(?:\s*;|$)/i.test(part.contentType)) throw new Error('Phần metadata không phải JSON.');
        return JSON.parse(decoder.decode(part.bytes));
    }

    const api = { parse, json };
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.FileHandlerMultipart = api;
})(globalThis);
