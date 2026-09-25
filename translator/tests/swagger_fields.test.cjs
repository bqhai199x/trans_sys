const assert = require('node:assert/strict');
const test = require('node:test');
const { buildBody, requestInterceptor, mount } = require('../src/translation_service/swagger_fields.js');

test('JSON schema renders one input per field', () => {
    global.document = { createElement: name => ({
        tagName: name.toUpperCase(), style: {}, dataset: {}, children: [],
        appendChild(child) { this.children.push(child); }, addEventListener() {}
    }) };
    const body = { panel: null, querySelector(selector) {
        return selector === '.translator-json-fields' ? this.panel : null;
    }, prepend(panel) { this.panel = panel; } };
    const block = { querySelector(selector) { return selector === '.opblock-body' ? body : null; } };
    mount(block, '/api/genprompt', { requestBody: { content: { 'application/json': { schema: {
        required: ['texts', 'target_language'], properties: {
            texts: { type: 'array', example: ['one', 'two'] },
            target_language: { type: 'string', example: 'vi' }
        }
    } } } } });
    const inputs = body.panel.children.filter(child => child.tagName === 'LABEL')
        .map(label => label.children[0]);
    assert.deepEqual(inputs.map(input => input.dataset.jsonField), ['texts', 'target_language']);
    assert.deepEqual(JSON.parse(inputs[0].value), ['one', 'two']);
    assert.equal(inputs[1].value, 'vi');
});

test('separate fields produce JSON array without losing commas or token boundaries', () => {
    const texts = ['<ox:r0>first, second</ox:r0>', '<ox:r0>third</ox:r0>'];
    const body = buildBody([
        { name: 'texts', type: 'array', required: true, value: JSON.stringify(texts) },
        { name: 'target_language', type: 'string', required: true, value: 'vi' },
        { name: 'context', type: 'string', required: false, value: '' },
        { name: 'reasoning_effort', type: 'string', required: false, value: '' }
    ]);
    assert.deepEqual(body, { texts, target_language: 'vi', context: '' });
    assert.throws(() => buildBody([{ name: 'texts', type: 'array', required: true,
        value: '["one", 2]' }]), /JSON array gồm các chuỗi/);
});

test('Swagger Execute sends field values as application/json', () => {
    const fields = [
        { dataset: { jsonField: 'texts', jsonType: 'array', jsonRequired: 'true' },
            value: '["one, two","three"]' },
        { dataset: { jsonField: 'target_language', jsonType: 'string', jsonRequired: 'true' }, value: 'vi' }
    ];
    const error = { textContent: '' };
    const panel = { style: { display: 'grid' }, querySelectorAll: () => fields,
        querySelector: () => error };
    const block = { querySelector: selector => selector === '.opblock-summary-path'
        ? { textContent: '/api/genprompt' } : panel };
    global.location = { href: 'http://127.0.0.1:8000/docs' };
    global.document = { querySelectorAll: () => [block] };
    const request = { url: 'http://127.0.0.1:8000/api/genprompt',
        headers: { 'Content-Type': 'application/json' }, body: '{}' };
    requestInterceptor(request);
    assert.deepEqual(JSON.parse(request.body), { texts: ['one, two', 'three'], target_language: 'vi' });
    assert.equal(error.textContent, '');
});
