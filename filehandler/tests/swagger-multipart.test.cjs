const { test } = require('node:test');
const assert = require('node:assert/strict');
const multipart = require('../src/FileHandler.Api/wwwroot/swagger-multipart.js');

function response(payload, id = 'file', name = "filename*=UTF-8''%E6%97%A5%E6%9C%AC%E8%AA%9E.xlsx") {
    return Buffer.concat([
        Buffer.from('--sample\r\nContent-Type: application/json; charset=utf-8\r\nContent-ID: <metadata>\r\n\r\n{"metadata":{"skipped":[]},"errors":[]}\r\n--sample\r\nContent-Type: application/octet-stream\r\nContent-ID: <' + id + '>\r\nContent-Disposition: attachment; ' + name + '\r\n\r\n'),
        payload,
        Buffer.from('\r\n--sample--\r\n')
    ]);
}

test('multipart binary payload and Unicode download name remain exact', () => {
    const payload = Buffer.concat([Buffer.from(Array.from({ length: 256 }, (_, i) => i)), Buffer.from('\r\n--sampleX\r\n--sample--not-a-boundary')]);
    const parts = multipart.parse(response(payload), 'multipart/mixed; boundary="sample"');
    assert.equal(parts.length, 2);
    assert.deepEqual(parts[1].bytes, payload);
    assert.equal(parts[1].filename, '日本語.xlsx');
    assert.deepEqual(multipart.json(parts[0]), { metadata: { skipped: [] }, errors: [] });
});

test('units attachment remains separate from skipped metadata', () => {
    const units = Buffer.from('{"units":[{"index":0,"kind":"paragraph","location":{"line":{"start":1,"end":1}}}]}');
    const parts = multipart.parse(response(units, 'units', 'filename="units.json"'), 'Multipart/Mixed; Boundary=sample');
    assert.equal(parts[1].id, '<units>');
    assert.equal(parts[1].filename, 'units.json');
    assert.deepEqual(parts[1].bytes, units);
    assert.deepEqual(multipart.json(parts[0]).metadata.skipped, []);
});

test('filenames use safe basenames with fallback for invalid encoded values', () => {
    const parts = multipart.parse(response(Buffer.alloc(0), 'file', 'filename="../../safe.txt"; filename*=UTF-8\'\'%XX'), 'multipart/mixed; boundary=sample');
    assert.equal(parts[1].filename, 'safe.txt');
    assert.equal(parts[1].bytes.length, 0);
});

test('incomplete bodies and unsupported MIME types are rejected', () => {
    const bytes = response(Buffer.from('payload'));
    assert.throws(() => multipart.parse(bytes.subarray(0, bytes.length - 10), 'multipart/mixed; boundary=sample'));
    assert.throws(() => multipart.parse(bytes, 'multipart/mixed'));
    assert.throws(() => multipart.parse(bytes, 'text/plain; boundary=sample'));
    assert.throws(() => multipart.parse(response(Buffer.alloc(0), 'unexpected'), 'multipart/mixed; boundary=sample'));
});
