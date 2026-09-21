const { test } = require('node:test');
const assert = require('node:assert/strict');
const response = require('../src/FileHandler.Api/wwwroot/swagger-response.js');

test('large JSON preview stays bounded without parsing the entire response', () => {
    const bytes = Buffer.from(JSON.stringify({ texts: Array(20000).fill('Nội dung dài'), metadata: { skipCount: { warning: 2, info: 7 } } }));
    const parse = JSON.parse;
    let parsed = false;
    JSON.parse = () => { parsed = true; throw new Error('Large JSON must not be parsed'); };
    let result;
    try { result = response.prepare(bytes, 'application/json'); }
    finally { JSON.parse = parse; }
    assert.equal(parsed, false);
    assert.equal(result.preview.truncated, true);
    assert.ok(result.preview.text.length <= response.maxPreviewChars);
    assert.deepEqual(result.parts[0].bytes, bytes);
    assert.equal(result.parts[0].filename, 'response.json');
});

test('multipart preview is bounded while metadata and binary downloads remain exact', () => {
    const metadata = Buffer.from(JSON.stringify({ metadata: { skipped: Array(10000).fill({ code: 'formula_cell', severity: 'info' }) } }));
    const binary = Buffer.concat([Buffer.alloc(4 * 1024 * 1024, 255), Buffer.from('\r\n--sampleX')]);
    const bytes = Buffer.concat([
        Buffer.from('--sample\r\nContent-Type: application/json\r\nContent-ID: <metadata>\r\n\r\n'), metadata,
        Buffer.from('\r\n--sample\r\nContent-Type: application/octet-stream\r\nContent-ID: <file>\r\nContent-Disposition: attachment; filename="file.xlsx"\r\n\r\n'), binary,
        Buffer.from('\r\n--sample--\r\n')
    ]);
    const result = response.prepare(bytes, 'multipart/mixed; boundary=sample');
    assert.equal(result.kind, 'multipart');
    assert.equal(result.preview.truncated, true);
    assert.ok(result.preview.text.length <= response.maxPreviewChars);
    assert.deepEqual(result.parts[0].bytes, metadata);
    assert.deepEqual(result.parts[1].bytes, binary);
});

test('small JSON is readable and malformed JSON stays bounded', () => {
    const good = response.preview(Buffer.from('{"metadata":{"skipCount":{"warning":1,"info":2}}}'));
    assert.equal(good.truncated, false);
    assert.equal(JSON.parse(good.text).metadata.skipCount.info, 2);
    const bad = response.preview(Buffer.alloc(response.maxInlineBytes + 1, 65));
    assert.equal(bad.truncated, true);
    assert.equal(bad.text.length, response.maxPreviewChars);
});
