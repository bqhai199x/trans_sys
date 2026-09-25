(function (root) {
    'use strict';
    const valuesByPath = new Map();

    function buildBody(fields) {
        const body = {};
        for (const field of fields) {
            const value = field.value;
            if (!field.required && value === '' && field.name !== 'context' && field.name !== 'custom_prompt') {
                continue;
            }
            if (field.type === 'array') {
                let items;
                try {
                    items = JSON.parse(value);
                } catch (_) {
                    throw new Error(field.name + ' phải là JSON array.');
                }
                if (!Array.isArray(items) || !items.every(item => typeof item === 'string')) {
                    throw new Error(field.name + ' phải là JSON array gồm các chuỗi.');
                }
                body[field.name] = items;
            } else {
                body[field.name] = value;
            }
        }
        return body;
    }

    function findBlock(url) {
        const pathname = new URL(url, root.location.href).pathname;
        return Array.from(document.querySelectorAll('.opblock-post')).find(block => {
            const path = block.querySelector('.opblock-summary-path')?.textContent.trim();
            return path && pathname.endsWith(path);
        });
    }

    function currentMediaType(block) {
        return block.querySelector('select.content-type')?.value || 'application/json';
    }

    function readFields(panel) {
        return Array.from(panel.querySelectorAll('[data-json-field]'), input => ({
            name: input.dataset.jsonField,
            type: input.dataset.jsonType,
            required: input.dataset.jsonRequired === 'true',
            value: input.value
        }));
    }

    function requestInterceptor(request) {
        const headers = request.headers || {};
        const header = Object.keys(headers).find(name => name.toLowerCase() === 'content-type');
        if (!header || !/^application\/json(?:;|$)/i.test(String(headers[header]))) return request;
        const panel = findBlock(request.url)?.querySelector('.translator-json-fields');
        if (!panel || panel.style.display === 'none') return request;
        try {
            request.body = JSON.stringify(buildBody(readFields(panel)));
            panel.querySelector('.translator-json-error').textContent = '';
        } catch (error) {
            panel.querySelector('.translator-json-error').textContent = error.message;
            throw error;
        }
        return request;
    }

    function mount(block, path, operation) {
        const body = block.querySelector('.opblock-body');
        if (!body || !operation?.requestBody?.content?.['application/json']?.schema?.properties) return;
        const schema = operation.requestBody.content['application/json'].schema;
        let panel = body.querySelector('.translator-json-fields');
        if (!panel) {
            panel = document.createElement('div');
            panel.className = 'translator-json-fields';
            panel.style.cssText = 'display:grid;gap:10px;padding:16px;margin:12px 0;background:#f8f8f8;border:1px solid #ccc';
            const title = document.createElement('strong');
            title.textContent = 'Nhập từng field (gửi application/json)';
            panel.appendChild(title);
            for (const [name, field] of Object.entries(schema.properties)) {
                const label = document.createElement('label');
                label.textContent = name + (schema.required?.includes(name) ? ' *' : '');
                label.style.cssText = 'display:grid;gap:4px;font-weight:600';
                const input = document.createElement(name === 'texts' || name === 'raw_output' ? 'textarea' : 'input');
                input.dataset.jsonField = name;
                input.dataset.jsonType = field.type === 'array' ? 'array' : 'string';
                input.dataset.jsonRequired = String(schema.required?.includes(name) || false);
                input.style.cssText = 'box-sizing:border-box;width:100%;padding:8px;font:inherit;font-weight:400';
                if (input.tagName === 'TEXTAREA') input.rows = name === 'texts' ? 6 : 3;
                if (name === 'api_key') input.type = 'password';
                const sample = field.example ?? field.default ?? (field.type === 'array' ? [] : '');
                input.value = valuesByPath.get(path)?.[name] ??
                    (field.type === 'array' ? JSON.stringify(sample, null, 2) : String(sample));
                input.addEventListener('input', () => {
                    const values = valuesByPath.get(path) || {};
                    values[name] = input.value;
                    valuesByPath.set(path, values);
                });
                label.appendChild(input);
                panel.appendChild(label);
            }
            const error = document.createElement('div');
            error.className = 'translator-json-error';
            error.style.color = '#b00020';
            panel.appendChild(error);
            body.prepend(panel);
        }
        const jsonSelected = currentMediaType(block) === 'application/json';
        panel.style.display = jsonSelected ? 'grid' : 'none';
        const editor = body.querySelector('textarea.body-param__text, .body-param textarea');
        if (editor) editor.style.display = jsonSelected ? 'none' : '';
    }

    function start(spec) {
        const scan = () => {
            for (const block of document.querySelectorAll('.opblock-post')) {
                const path = block.querySelector('.opblock-summary-path')?.textContent.trim();
                mount(block, path, spec.paths?.[path]?.post);
            }
        };
        new MutationObserver(scan).observe(document.getElementById('swagger-ui'), { childList: true, subtree: true });
        document.addEventListener('change', scan, true);
        scan();
    }

    const api = { buildBody, requestInterceptor, mount };
    if (typeof module === 'object' && module.exports) {
        module.exports = api;
    } else {
        root.translatorFieldsToJson = requestInterceptor;
        fetch(new URL('openapi.json', root.location.href))
            .then(response => response.json())
            .then(start);
    }
})(globalThis);
